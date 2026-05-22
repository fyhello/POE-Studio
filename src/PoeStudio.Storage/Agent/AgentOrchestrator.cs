using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;

namespace PoeStudio.Storage.Agent;

public sealed class AgentOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AgentStore _store;
    private readonly AgentPromptBuilder _promptBuilder;
    private readonly Datc64TranslationDraftParser _datc64Parser;
    private readonly ICodexProcessRunner _runner;

    public AgentOrchestrator(
        AgentStore store,
        AgentPromptBuilder promptBuilder,
        Datc64TranslationDraftParser datc64Parser,
        ICodexProcessRunner runner)
    {
        _store = store;
        _promptBuilder = promptBuilder;
        _datc64Parser = datc64Parser;
        _runner = runner;
    }

    public async Task<AgentRunDto> StartRunAsync(
        string threadId,
        string profileId,
        string goal,
        string taskKind,
        string? resourcePath,
        CancellationToken cancellationToken)
    {
        var thread = await _store.GetThreadAsync(threadId, cancellationToken)
            ?? throw new ArgumentException("thread_not_found", nameof(threadId));
        var settings = await _store.GetSettingsAsync(cancellationToken)
            ?? DefaultSettings();
        var capability = AgentCapabilities.GetRequired(taskKind);
        var messages = await _store.ListMessagesAsync(threadId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var userMessage = new AgentMessageDto(
            NewId("message"),
            threadId,
            AgentMessageRole.User,
            goal,
            resourcePath is null ? null : JsonSerializer.Serialize(new { resourcePath }, JsonOptions),
            now);
        await _store.AppendMessageAsync(userMessage, cancellationToken);

        var run = new AgentRunDto(
            NewId("run"),
            threadId,
            profileId,
            goal,
            taskKind,
            AgentRunStatus.Running,
            5,
            "Run created",
            now,
            now,
            0,
            null,
            null,
            null);
        await _store.SaveRunAsync(run, cancellationToken);
        await _store.AppendEventAsync(threadId, run.Id, AgentEventType.RunCreated, "Run created", null, cancellationToken);
        var plan = CreateInitialPlan(run.Id);
        await _store.SavePlanAsync(threadId, run.Id, plan, cancellationToken);
        await _store.AppendEventAsync(threadId, run.Id, AgentEventType.PlanUpdated, "Initial plan created", null, cancellationToken);

        try
        {
            var prompt = _promptBuilder.Build(settings, capability, thread, messages.Concat([userMessage]).ToArray(), goal, resourcePath);
            var result = await _runner.RunAsync(settings, prompt, cancellationToken);
            foreach (var parsedEvent in result.Events)
            {
                await PersistParsedEventAsync(threadId, run.Id, parsedEvent, cancellationToken);
            }

            if (result.Cancelled)
            {
                return await CompleteRunAsync(run, AgentRunStatus.Cancelled, 0, "Cancelled", "cancelled", "Run cancelled", null, cancellationToken);
            }

            if (result.Failed)
            {
                await _store.AppendEventAsync(threadId, run.Id, AgentEventType.RunFailed, result.StderrSummary ?? "Codex failed", null, cancellationToken);
                return await CompleteRunAsync(run, AgentRunStatus.Failed, 0, "Failed", "codex_failed", result.StderrSummary ?? "Codex failed", null, cancellationToken);
            }

            var finalMessage = LastAgentMessage(result.Events);
            if (string.Equals(taskKind, "datc64-translation", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(resourcePath))
                {
                    throw new ArgumentException("resource_path_required");
                }

                var proposal = _datc64Parser.Parse(finalMessage, profileId, resourcePath);
                var proposalJson = JsonSerializer.Serialize(proposal, JsonOptions);
                var approval = new AgentApprovalDto(
                    NewId("approval"),
                    run.Id,
                    profileId,
                    taskKind,
                    AgentApprovalStatus.Pending,
                    $"{proposal.Candidates.Count} DATC64 translation candidate(s)",
                    proposalJson,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    null);
                await _store.SaveApprovalsAsync(threadId, run.Id, [approval], cancellationToken);
                await _store.AppendEventAsync(threadId, run.Id, AgentEventType.ApprovalRequested, approval.Summary, proposalJson, cancellationToken);
                await _store.SavePlanAsync(threadId, run.Id, CompletePlan(run.Id, waitingForApproval: true), cancellationToken);
                return await CompleteRunAsync(run, AgentRunStatus.WaitingForApproval, 90, "Waiting for approval", null, null, null, cancellationToken);
            }

            var resultJson = ExtractFinalJsonOrWrap(taskKind, profileId, finalMessage);
            await _store.SavePlanAsync(threadId, run.Id, CompletePlan(run.Id, waitingForApproval: false), cancellationToken);
            return await CompleteRunAsync(run, AgentRunStatus.Succeeded, 100, "Succeeded", null, null, resultJson, cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
        {
            await _store.AppendEventAsync(threadId, run.Id, AgentEventType.RunFailed, ex.Message, null, cancellationToken);
            return await CompleteRunAsync(run, AgentRunStatus.Failed, 0, "Failed", "agent_run_failed", ex.Message, null, cancellationToken);
        }
    }

    public async Task<AgentRunDto> RetryAsync(string runId, CancellationToken cancellationToken)
    {
        var previous = await _store.FindRunAsync(runId, cancellationToken)
            ?? throw new ArgumentException("run_not_found", nameof(runId));
        return await StartRunAsync(
            previous.ThreadId,
            previous.ProfileId,
            previous.Goal,
            previous.TaskKind,
            null,
            cancellationToken);
    }

    private async Task PersistParsedEventAsync(
        string threadId,
        string runId,
        CodexParsedEvent parsedEvent,
        CancellationToken cancellationToken)
    {
        var type = parsedEvent.EventType switch
        {
            CodexParsedEventType.McpToolCall => AgentEventType.McpToolCall,
            CodexParsedEventType.AgentMessage or CodexParsedEventType.FinalMessage => AgentEventType.AgentMessage,
            CodexParsedEventType.StdErr => AgentEventType.CodexStderr,
            CodexParsedEventType.Error => AgentEventType.RunFailed,
            CodexParsedEventType.Cancelled => AgentEventType.RunCancelled,
            _ => AgentEventType.CodexStdout
        };
        await _store.AppendEventAsync(threadId, runId, type, parsedEvent.Message, parsedEvent.PayloadJson, cancellationToken);
    }

    private async Task<AgentRunDto> CompleteRunAsync(
        AgentRunDto run,
        AgentRunStatus status,
        int progress,
        string message,
        string? errorCode,
        string? errorMessage,
        string? resultJson,
        CancellationToken cancellationToken)
    {
        var events = await _store.ListEventsAsync(run.ThreadId, run.Id, 0, cancellationToken);
        var updated = run with
        {
            Status = status,
            ProgressPercent = progress,
            Message = message,
            UpdatedAt = DateTimeOffset.UtcNow,
            EventCount = events.Count,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            ResultJson = resultJson
        };
        await _store.SaveRunAsync(updated, cancellationToken);
        return updated;
    }

    private static IReadOnlyList<AgentPlanStepDto> CreateInitialPlan(string runId)
    {
        return
        [
            new AgentPlanStepDto(NewId("step"), runId, 1, "Build prompt", "completed", "AgentPromptBuilder"),
            new AgentPlanStepDto(NewId("step"), runId, 2, "Run Codex", "running", null),
            new AgentPlanStepDto(NewId("step"), runId, 3, "Store result", "pending", null)
        ];
    }

    private static IReadOnlyList<AgentPlanStepDto> CompletePlan(string runId, bool waitingForApproval)
    {
        return
        [
            new AgentPlanStepDto(NewId("step"), runId, 1, "Build prompt", "completed", "AgentPromptBuilder"),
            new AgentPlanStepDto(NewId("step"), runId, 2, "Run Codex", "completed", "Codex events recorded"),
            new AgentPlanStepDto(NewId("step"), runId, 3, waitingForApproval ? "Request approval" : "Store result", "completed", waitingForApproval ? "Pending approval created" : "Result saved")
        ];
    }

    private static string LastAgentMessage(IReadOnlyList<CodexParsedEvent> events)
    {
        return events.LastOrDefault(x => x.EventType is CodexParsedEventType.AgentMessage or CodexParsedEventType.FinalMessage)?.Message
            ?? string.Empty;
    }

    private static string ExtractFinalJsonOrWrap(string taskKind, string profileId, string finalMessage)
    {
        var start = finalMessage.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            var jsonStart = finalMessage.IndexOf('\n', start);
            var end = finalMessage.IndexOf("```", jsonStart + 1, StringComparison.Ordinal);
            if (jsonStart >= 0 && end > jsonStart)
            {
                return finalMessage[(jsonStart + 1)..end].Trim();
            }
        }

        return JsonSerializer.Serialize(new { taskKind, profileId, summary = finalMessage, evidence = Array.Empty<string>() }, JsonOptions);
    }

    private static AgentSettingsDto DefaultSettings()
    {
        return new AgentSettingsDto("codex", null, null, "workspace-write", "poe-studio", Environment.CurrentDirectory, "manual");
    }

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
