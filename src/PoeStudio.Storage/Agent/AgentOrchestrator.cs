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
    private readonly AgentProjectContextService _projectContextService;

    public AgentOrchestrator(
        AgentStore store,
        AgentPromptBuilder promptBuilder,
        Datc64TranslationDraftParser datc64Parser,
        ICodexProcessRunner runner,
        AgentProjectContextService projectContextService)
    {
        _store = store;
        _promptBuilder = promptBuilder;
        _datc64Parser = datc64Parser;
        _runner = runner;
        _projectContextService = projectContextService;
    }

    public async Task<AgentRunDto> StartRunAsync(
        string threadId,
        string profileId,
        string goal,
        string taskKind,
        string? resourcePath,
        string? oodlePath,
        CancellationToken cancellationToken)
    {
        var run = await StartRunShellAsync(threadId, profileId, goal, taskKind, resourcePath, oodlePath, cancellationToken);
        return await ContinueRunAsync(run.Id, cancellationToken);
    }

    public Task<AgentRunDto> StartRunAsync(
        string threadId,
        string profileId,
        string goal,
        string taskKind,
        string? resourcePath,
        CancellationToken cancellationToken)
    {
        return StartRunAsync(threadId, profileId, goal, taskKind, resourcePath, null, cancellationToken);
    }

    public async Task<AgentRunDto> StartRunShellAsync(
        string threadId,
        string profileId,
        string goal,
        string taskKind,
        string? resourcePath,
        string? oodlePath,
        CancellationToken cancellationToken)
    {
        var thread = await _store.GetThreadAsync(threadId, cancellationToken)
            ?? throw new ArgumentException("thread_not_found", nameof(threadId));
        AgentCapabilities.GetRequired(taskKind);
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
            null,
            resourcePath,
            NormalizeOodlePath(oodlePath));
        await _store.SaveRunAsync(run, cancellationToken);
        await _store.AppendEventAsync(threadId, run.Id, AgentEventType.RunCreated, "Run created", null, cancellationToken);
        var plan = CreateInitialPlan(run.Id);
        await _store.SavePlanAsync(threadId, run.Id, plan, cancellationToken);
        await _store.AppendEventAsync(threadId, run.Id, AgentEventType.PlanUpdated, "Initial plan created", null, cancellationToken);
        return run;
    }

    public Task<AgentRunDto> StartRunShellAsync(
        string threadId,
        string profileId,
        string goal,
        string taskKind,
        string? resourcePath,
        CancellationToken cancellationToken)
    {
        return StartRunShellAsync(threadId, profileId, goal, taskKind, resourcePath, null, cancellationToken);
    }

    public async Task<AgentRunDto> ContinueRunAsync(string runId, CancellationToken cancellationToken)
    {
        var run = await _store.FindRunAsync(runId, CancellationToken.None)
            ?? throw new ArgumentException("run_not_found", nameof(runId));
        try
        {
            var thread = await _store.GetThreadAsync(run.ThreadId, CancellationToken.None)
                ?? throw new ArgumentException("thread_not_found", nameof(run.ThreadId));
            var settings = await _store.GetSettingsAsync(CancellationToken.None)
                ?? DefaultSettings();
            settings = EffectiveSettingsForRun(settings, run);
            var capability = AgentCapabilities.GetRequired(run.TaskKind);
            var messages = await _store.ListMessagesAsync(run.ThreadId, CancellationToken.None);
            var resourcePath = run.ResourcePath;
            var goal = run.Goal;
            var profileId = run.ProfileId;
            var taskKind = run.TaskKind;
            var projectContext = await _projectContextService.BuildAsync(
                taskKind,
                goal,
                resourcePath,
                settings.WorkingDirectory,
                CancellationToken.None);
            var preflight = new AgentProjectPreflightDto(
                thread.Id,
                run.Id,
                profileId,
                taskKind,
                goal,
                resourcePath,
                projectContext.Sources.Any(source => source.Exists),
                settings.WorkingDirectory,
                projectContext.Sources,
                projectContext.Summary,
                projectContext.ToolGuidance.Select(tool => tool.ToolName).ToArray(),
                projectContext.Unknowns);
            await _store.AppendEventAsync(
                run.ThreadId,
                run.Id,
                AgentEventType.PlanUpdated,
                "Project context loaded",
                JsonSerializer.Serialize(preflight, JsonOptions),
                CancellationToken.None);
            var prompt = _promptBuilder.Build(settings, capability, thread, messages, goal, resourcePath, projectContext);
            var persistedEventKeys = new HashSet<string>(StringComparer.Ordinal);
            var result = await _runner.RunAsync(
                settings,
                prompt,
                async parsedEvent =>
                {
                    await PersistParsedEventAsync(run.ThreadId, run.Id, parsedEvent, CancellationToken.None);
                    persistedEventKeys.Add(EventKey(parsedEvent));
                },
                cancellationToken);
            foreach (var parsedEvent in result.Events)
            {
                if (persistedEventKeys.Contains(EventKey(parsedEvent)))
                {
                    continue;
                }

                await PersistParsedEventAsync(run.ThreadId, run.Id, parsedEvent, CancellationToken.None);
            }

            if (result.Cancelled)
            {
                return await CompleteRunAsync(run, AgentRunStatus.Cancelled, 0, "Cancelled", "cancelled", "Run cancelled", null, CancellationToken.None);
            }

            if (result.Failed)
            {
                await _store.AppendEventAsync(run.ThreadId, run.Id, AgentEventType.RunFailed, result.StderrSummary ?? "Codex failed", null, CancellationToken.None);
                return await CompleteRunAsync(run, AgentRunStatus.Failed, 0, "Failed", result.ErrorCode ?? "codex_failed", result.StderrSummary ?? "Codex failed", null, CancellationToken.None);
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
                await _store.SaveApprovalsAsync(run.ThreadId, run.Id, [approval], cancellationToken);
                await _store.AppendEventAsync(run.ThreadId, run.Id, AgentEventType.ApprovalRequested, approval.Summary, proposalJson, cancellationToken);
                await _store.SavePlanAsync(run.ThreadId, run.Id, CompletePlan(run.Id, waitingForApproval: true), cancellationToken);
                return await CompleteRunAsync(run, AgentRunStatus.WaitingForApproval, 90, "Waiting for approval", null, null, null, cancellationToken);
            }

            var resultJson = ExtractFinalJsonOrWrap(taskKind, profileId, finalMessage);
            await _store.SavePlanAsync(run.ThreadId, run.Id, CompletePlan(run.Id, waitingForApproval: false), cancellationToken);
            return await CompleteRunAsync(run, AgentRunStatus.Succeeded, 100, "Succeeded", null, null, resultJson, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await _store.AppendEventAsync(run.ThreadId, run.Id, AgentEventType.RunCancelled, "Run cancelled", null, CancellationToken.None);
            return await CompleteRunAsync(run, AgentRunStatus.Cancelled, 0, "Cancelled", "cancelled", "Run cancelled", null, CancellationToken.None);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
        {
            await _store.AppendEventAsync(run.ThreadId, run.Id, AgentEventType.RunFailed, ex.Message, null, CancellationToken.None);
            return await CompleteRunAsync(run, AgentRunStatus.Failed, 0, "Failed", "agent_run_failed", ex.Message, null, CancellationToken.None);
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
            previous.ResourcePath,
            previous.OodlePath,
            cancellationToken);
    }

    public async Task<AgentRunDto> RetryShellAsync(string runId, CancellationToken cancellationToken)
    {
        var previous = await _store.FindRunAsync(runId, cancellationToken)
            ?? throw new ArgumentException("run_not_found", nameof(runId));
        return await StartRunShellAsync(
            previous.ThreadId,
            previous.ProfileId,
            previous.Goal,
            previous.TaskKind,
            previous.ResourcePath,
            previous.OodlePath,
            cancellationToken);
    }

    public async Task<AgentRunDto> FailRunAsync(
        string runId,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var run = await _store.FindRunAsync(runId, CancellationToken.None)
            ?? throw new ArgumentException("run_not_found", nameof(runId));
        await _store.AppendEventAsync(run.ThreadId, run.Id, AgentEventType.RunFailed, errorMessage, null, CancellationToken.None);
        return await CompleteRunAsync(run, AgentRunStatus.Failed, 0, "Failed", errorCode, errorMessage, null, CancellationToken.None);
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
            new AgentPlanStepDto(NewId("step"), runId, 1, "Load project context", "pending", null),
            new AgentPlanStepDto(NewId("step"), runId, 2, "Build prompt", "pending", null),
            new AgentPlanStepDto(NewId("step"), runId, 3, "Run Codex", "pending", null),
            new AgentPlanStepDto(NewId("step"), runId, 4, "Store result", "pending", null)
        ];
    }

    private static IReadOnlyList<AgentPlanStepDto> CompletePlan(string runId, bool waitingForApproval)
    {
        return
        [
            new AgentPlanStepDto(NewId("step"), runId, 1, "Load project context", "completed", "Project context preflight recorded"),
            new AgentPlanStepDto(NewId("step"), runId, 2, "Build prompt", "completed", "AgentPromptBuilder"),
            new AgentPlanStepDto(NewId("step"), runId, 3, "Run Codex", "completed", "Codex events recorded"),
            new AgentPlanStepDto(NewId("step"), runId, 4, waitingForApproval ? "Request approval" : "Store result", "completed", waitingForApproval ? "Pending approval created" : "Result saved")
        ];
    }

    private static string LastAgentMessage(IReadOnlyList<CodexParsedEvent> events)
    {
        return events.LastOrDefault(x => x.EventType is CodexParsedEventType.AgentMessage or CodexParsedEventType.FinalMessage)?.Message
            ?? string.Empty;
    }

    private static string EventKey(CodexParsedEvent parsedEvent)
    {
        return $"{parsedEvent.EventType}|{parsedEvent.RawJson}|{parsedEvent.Message}";
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
        return new AgentSettingsDto("codex", null, null, "workspace-write", "poe-studio", Environment.CurrentDirectory, "manual", null);
    }

    private static AgentSettingsDto EffectiveSettingsForRun(AgentSettingsDto settings, AgentRunDto run)
    {
        if (string.IsNullOrWhiteSpace(run.OodlePath))
        {
            return settings;
        }

        return settings with { OodlePath = run.OodlePath };
    }

    private static string? NormalizeOodlePath(string? oodlePath)
    {
        return string.IsNullOrWhiteSpace(oodlePath) ? null : oodlePath.Trim();
    }

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
