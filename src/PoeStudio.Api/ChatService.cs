using System.Text.Json;
using System.Threading.Channels;
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;
using PoeStudio.Storage.Agent;

namespace PoeStudio.Api;

public sealed record ChatRequest(
    string Message,
    string? ProfileId = null,
    string? ResourcePath = null,
    string? SourceProfileId = null,
    string? TargetProfileId = null,
    string? SourceResourcePath = null,
    string? TargetResourcePath = null,
    AgentCurrentViewRequestDto? CurrentView = null);

public sealed record ChatSseEvent(string EventName, string DataJson);

public sealed class ChatService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICodexProcessRunner _runner;
    private readonly WorkspaceRootProvider _workspaceRoot;
    private readonly IConfiguration _configuration;
    private readonly AgentCurrentViewStore _currentViewStore;
    private readonly AgentRunTraceStore _traceStore;
    private readonly TimeSpan _toolHangThreshold;
    private readonly TimeSpan _watchdogPollInterval;

    public ChatService(
        ICodexProcessRunner runner,
        WorkspaceRootProvider workspaceRoot,
        IConfiguration configuration,
        AgentCurrentViewStore currentViewStore,
        AgentRunTraceStore traceStore,
        TimeSpan? toolHangThreshold = null,
        TimeSpan? watchdogPollInterval = null)
    {
        _runner = runner;
        _workspaceRoot = workspaceRoot;
        _configuration = configuration;
        _currentViewStore = currentViewStore;
        _traceStore = traceStore;
        _toolHangThreshold = toolHangThreshold ?? TimeSpan.FromSeconds(30);
        _watchdogPollInterval = watchdogPollInterval ?? TimeSpan.FromSeconds(1);
    }

    public IAsyncEnumerable<ChatSseEvent> RunCodexAsync(
        string message,
        string? profileId,
        string? resourcePath,
        string? sourceProfileId,
        string? targetProfileId,
        string? sourceResourcePath,
        string? targetResourcePath,
        AgentCurrentViewRequestDto? currentView,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<ChatSseEvent>();
        var settings = BuildSettings(profileId);

        _ = RunInternalAsync(
            channel,
            settings,
            message,
            profileId,
            resourcePath,
            sourceProfileId,
            targetProfileId,
            sourceResourcePath,
            targetResourcePath,
            currentView,
            cancellationToken);

        return channel.Reader.ReadAllAsync(CancellationToken.None);
    }

    private string BuildPrompt(
        string message,
        string? profileId,
        string? resourcePath,
        string? sourceProfileId,
        string? targetProfileId,
        string? sourceResourcePath,
        string? targetResourcePath,
        string? currentViewContextId)
    {
        if (string.IsNullOrWhiteSpace(profileId)
            && string.IsNullOrWhiteSpace(resourcePath)
            && string.IsNullOrWhiteSpace(sourceProfileId)
            && string.IsNullOrWhiteSpace(targetProfileId)
            && string.IsNullOrWhiteSpace(sourceResourcePath)
            && string.IsNullOrWhiteSpace(targetResourcePath)
            && string.IsNullOrWhiteSpace(currentViewContextId))
            return message;

        var lines = new List<string>
        {
            "Current POE Studio session:"
        };

        var workspaceRoot = _workspaceRoot.CurrentRoot;
        if (workspaceRoot is not null)
            lines.Add($"- workspaceRoot: {workspaceRoot}");

        if (!string.IsNullOrWhiteSpace(profileId))
            lines.Add($"- activeProfileId: {profileId}");

        if (!string.IsNullOrWhiteSpace(sourceProfileId))
            lines.Add($"- sourceProfileId: {sourceProfileId}");

        if (!string.IsNullOrWhiteSpace(targetProfileId))
            lines.Add($"- targetProfileId: {targetProfileId}");

        if (!string.IsNullOrWhiteSpace(resourcePath))
            lines.Add($"- selectedResourcePath: {resourcePath}");

        if (!string.IsNullOrWhiteSpace(sourceResourcePath))
            lines.Add($"- sourceResourcePath: {sourceResourcePath}");

        if (!string.IsNullOrWhiteSpace(targetResourcePath))
            lines.Add($"- targetResourcePath: {targetResourcePath}");

        if (!string.IsNullOrWhiteSpace(currentViewContextId))
            lines.Add($"- currentViewContextId: {currentViewContextId}");

        if (sourceProfileId is not null && targetProfileId is not null)
            lines.Add($"- translationPair: {sourceProfileId} → {targetProfileId}");

        lines.Add("");
        lines.Add("User message: " + message);
        lines.Add("---");
        lines.Add("Agent Knowledge Contract:");
        lines.Add("- source/current source means reference table or reference resource.");
        lines.Add("- target/current target means editable target and overlay write target.");
        lines.Add("- Do not infer desired output language from profile names or resource paths.");
        lines.Add("- Current table/draft/comparison tasks must inspect current-view first when currentViewContextId exists.");
        lines.Add("- Use poe_get_project_overview for a short project overview and poe_get_project_knowledge to read workflow-specific knowledge sections by sectionId.");
        lines.Add("- Write changes only to target overlay staging unless the user explicitly changes the editable target.");
        lines.Add("");
        lines.Add("Task Frame: Before choosing tools, internally identify userGoal, currentState, reference, editableTarget, desiredOutputLanguage, writeIntent, preferredContext, requiredKnowledge, and toolFitCheck.");
        lines.Add("Tool Fit: A successful tool result can still be the wrong tool. If the tool semantics do not answer the user's task, choose a better tool or report capability_gap.");
        if (!string.IsNullOrWhiteSpace(currentViewContextId))
        {
            lines.Add("When the user says current table, current draft, opened table, current comparison, or asks to check missing translations in the current table, use current-view MCP tools first.");
            lines.Add("Use poe_get_current_view_context with currentViewContextId to inspect the current UI snapshot.");
            lines.Add("Recommended knowledge sections for current table tasks: core.contract, workflow.current-view, workflow.datc64-translation, diagnostics.tool-fit-and-capability-gap.");
            lines.Add("Choose current-table analysis tools by semantics, such as poe_find_current_table_untranslated_cells for missing-translation candidates.");
            lines.Add("Do not call poe_datc64_extract_translatable_cells for current table checks.");
            lines.Add("Only call raw resource tools such as poe_read_resource or poe_datc64_extract_translatable_cells when currentViewContextId is absent or the user explicitly asks to reread raw files.");
        }
        lines.Add("");
        lines.Add("CRITICAL: Do NOT execute shell commands or read any skill files (.agents/ .codex/). The Skill tool is NOT available in this environment. All project data must be accessed exclusively through the available poe_* MCP tools.");

        return string.Join("\n", lines);
    }

    private async Task RunInternalAsync(
        Channel<ChatSseEvent> channel,
        AgentSettingsDto settings,
        string message,
        string? profileId,
        string? resourcePath,
        string? sourceProfileId,
        string? targetProfileId,
        string? sourceResourcePath,
        string? targetResourcePath,
        AgentCurrentViewRequestDto? currentView,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");
        var runMode = AgentRunModes.Normal;
        var doneSent = false;
        using var runnerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            await _traceStore.AppendAsync(
                runId,
                new AgentRunTraceEventDto(
                    "run",
                    "started",
                    JsonSerializer.Serialize(new { runMode, message }, JsonOptions),
                    DateTimeOffset.UtcNow),
                cancellationToken);
            await WriteSseAsync(
                channel,
                runId,
                Sse("run", new { type = "run_started", runId }),
                cancellationToken);

            var currentViewContextId = await SaveCurrentViewAsync(currentView, cancellationToken);
            var prompt = BuildPrompt(message, profileId, resourcePath, sourceProfileId, targetProfileId, sourceResourcePath, targetResourcePath, currentViewContextId);
            var runnerTask = _runner.RunAsync(settings, prompt, async parsedEvent =>
            {
                await AppendCodexEventAsync(runId, parsedEvent, runnerCancellation.Token);
                foreach (var sseEvent in ConvertToSseEvents(parsedEvent))
                {
                    await WriteSseAsync(channel, runId, sseEvent, runnerCancellation.Token);
                }
            }, runnerCancellation.Token);

            while (!runnerTask.IsCompleted)
            {
                var completed = await Task.WhenAny(
                    runnerTask,
                    Task.Delay(_watchdogPollInterval, CancellationToken.None));
                if (completed == runnerTask)
                {
                    break;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var liveTrace = await _traceStore.ReadAsync(runId, CancellationToken.None);
                var liveFinding = AgentDiagnosticsService.Analyze(runId, liveTrace, DateTimeOffset.UtcNow, _toolHangThreshold);
                if (runMode == AgentRunModes.Normal && liveFinding.ShouldStartDiagnosticRun && IsLiveFindingMature(liveFinding, liveTrace, DateTimeOffset.UtcNow))
                {
                    await WriteSseAsync(
                        channel,
                        runId,
                        Sse("done", new { type = "completed", autoDiagnostic = true }),
                        CancellationToken.None);
                    doneSent = true;

                    await WriteSseAsync(
                        channel,
                        runId,
                        Sse("diagnostic", new { type = "diagnostic_started", finding = liveFinding }),
                        CancellationToken.None);

                    await _traceStore.AppendAsync(
                        runId,
                        new AgentRunTraceEventDto(
                            "run",
                            "cancel_requested",
                            JsonSerializer.Serialize(new { reason = liveFinding.Code }, JsonOptions),
                            DateTimeOffset.UtcNow),
                        CancellationToken.None);
                    await runnerCancellation.CancelAsync();
                    await WaitForRunnerToSettleAsync(runnerTask);
                    await RunDiagnosticAsync(channel, settings, runId, liveFinding, CancellationToken.None);
                    return;
                }
            }

            var result = await runnerTask;

            if (result.Failed && result.StderrSummary is not null)
            {
                await WriteSseAsync(
                    channel,
                    runId,
                    Sse("error", new { type = "error", text = result.StderrSummary }),
                    CancellationToken.None);
            }
            else if (result.Cancelled)
            {
                await WriteSseAsync(
                    channel,
                    runId,
                    Sse("error", new { type = "cancelled", text = "Run cancelled." }),
                    CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            await WriteSseAsync(
                channel,
                runId,
                Sse("error", new { type = "cancelled", text = "Request cancelled." }),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            await WriteSseAsync(
                channel,
                runId,
                Sse("error", new { type = "error", text = ex.Message }),
                CancellationToken.None);
        }
        finally
        {
            if (!doneSent)
            {
                var trace = await _traceStore.ReadAsync(runId, CancellationToken.None);
                var finding = AgentDiagnosticsService.Analyze(runId, trace, DateTimeOffset.UtcNow, _toolHangThreshold);
                await WriteSseAsync(
                    channel,
                    runId,
                    Sse("done", new { type = "completed", autoDiagnostic = finding.ShouldStartDiagnosticRun }),
                    CancellationToken.None);
                doneSent = true;
                if (runMode == AgentRunModes.Normal && finding.ShouldStartDiagnosticRun)
                {
                    await WriteSseAsync(
                        channel,
                        runId,
                        Sse("diagnostic", new { type = "diagnostic_started", finding }),
                        CancellationToken.None);
                    await RunDiagnosticAsync(channel, settings, runId, finding, CancellationToken.None);
                }
            }

            channel.Writer.Complete();
        }
    }

    private async Task RunDiagnosticAsync(
        Channel<ChatSseEvent> channel,
        AgentSettingsDto settings,
        string sourceRunId,
        AgentDiagnosticFindingDto finding,
        CancellationToken cancellationToken)
    {
        var diagnosticRunId = Guid.NewGuid().ToString("N");
        await _traceStore.AppendAsync(
            diagnosticRunId,
            new AgentRunTraceEventDto(
                "run",
                "started",
                JsonSerializer.Serialize(new { runMode = AgentRunModes.Diagnostic, sourceRunId, finding.Code }, JsonOptions),
                DateTimeOffset.UtcNow),
            cancellationToken);

        var diagnosticPrompt = BuildDiagnosticPrompt(sourceRunId, finding);
        try
        {
            var diagnosticResult = await _runner.RunAsync(settings, diagnosticPrompt, async parsedEvent =>
            {
                await AppendCodexEventAsync(diagnosticRunId, parsedEvent, cancellationToken);
                foreach (var sseEvent in ConvertToSseEvents(parsedEvent))
                {
                    await WriteSseAsync(channel, diagnosticRunId, sseEvent, cancellationToken);
                }
            }, cancellationToken);

            if (diagnosticResult.Failed && diagnosticResult.StderrSummary is not null)
            {
                await WriteDiagnosticFailedAsync(channel, diagnosticRunId, finding, diagnosticResult.StderrSummary, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await WriteDiagnosticFailedAsync(channel, diagnosticRunId, finding, ex.Message, cancellationToken);
        }
    }

    private async Task WriteDiagnosticFailedAsync(
        Channel<ChatSseEvent> channel,
        string diagnosticRunId,
        AgentDiagnosticFindingDto finding,
        string summary,
        CancellationToken cancellationToken)
    {
        await WriteSseAsync(
            channel,
            diagnosticRunId,
            Sse("diagnostic", new
            {
                type = "diagnostic_failed",
                finding = finding with
                {
                    Code = "diagnostic_failed",
                    Summary = "诊断失败，不再递归诊断：" + summary,
                    ShouldStartDiagnosticRun = false,
                    RunMode = AgentRunModes.Diagnostic
                }
            }),
            cancellationToken);
    }

    private static async Task WaitForRunnerToSettleAsync(Task<CodexRunResult> runnerTask)
    {
        try
        {
            await Task.WhenAny(runnerTask, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None));
        }
        catch
        {
        }
    }

    private bool IsLiveFindingMature(
        AgentDiagnosticFindingDto finding,
        IReadOnlyList<AgentRunTraceEventDto> trace,
        DateTimeOffset now)
    {
        if (string.Equals(finding.Code, "tool_call_left_in_progress", StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(finding.Code, "no_final_answer_after_tool_result", StringComparison.Ordinal))
        {
            return true;
        }

        var latestCompletedTool = trace.LastOrDefault(evt =>
            evt.EventName == "tool_call"
            && evt.DataJson.Contains("\"status\":\"completed\"", StringComparison.Ordinal));
        return latestCompletedTool is not null && now - latestCompletedTool.CreatedAt >= _toolHangThreshold;
    }

    private static string BuildDiagnosticPrompt(string runId, AgentDiagnosticFindingDto finding)
    {
        return string.Join("\n", [
            "You are diagnosing a failed POE Studio Agent run.",
            $"Original runId: {runId}",
            $"Finding: {finding.Code}",
            "Use poe_get_agent_run_trace first.",
            "Use poe_get_agent_recent_logs if trace is insufficient.",
            "Do not modify files in diagnostic mode.",
            "Return:",
            "1. root cause",
            "2. evidence",
            "3. whether the original user task can continue",
            "4. whether code repair approval is required"
        ]);
    }

    private async Task AppendCodexEventAsync(
        string runId,
        CodexParsedEvent parsedEvent,
        CancellationToken cancellationToken)
    {
        await _traceStore.AppendAsync(
            runId,
            new AgentRunTraceEventDto(
                "codex_event",
                parsedEvent.EventType.ToString(),
                JsonSerializer.Serialize(new
                {
                    parsedEvent.EventType,
                    parsedEvent.Message,
                    parsedEvent.ToolName,
                    parsedEvent.IsTerminal,
                    parsedEvent.PayloadJson
                }, JsonOptions),
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private async Task WriteSseAsync(
        Channel<ChatSseEvent> channel,
        string runId,
        ChatSseEvent sseEvent,
        CancellationToken cancellationToken)
    {
        await _traceStore.AppendAsync(
            runId,
            new AgentRunTraceEventDto(sseEvent.EventName, "observed", sseEvent.DataJson, DateTimeOffset.UtcNow),
            cancellationToken);
        await channel.Writer.WriteAsync(sseEvent, cancellationToken);
    }

    private async Task<string?> SaveCurrentViewAsync(
        AgentCurrentViewRequestDto? currentView,
        CancellationToken cancellationToken)
    {
        if (currentView is null || string.Equals(currentView.Kind, "none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var snapshot = await _currentViewStore.SaveAsync(currentView, cancellationToken);
        return snapshot.ContextId;
    }

    private AgentSettingsDto BuildSettings(string? profileId)
    {
        var section = _configuration.GetSection("CodexSettings");

        return new AgentSettingsDto(
            CodexPath: section["CodexPath"] ?? "codex",
            Model: section["Model"],
            Profile: section["Profile"],
            Sandbox: section["Sandbox"] ?? "read-only",
            McpServerName: section["McpServerName"] ?? "poe-studio",
            WorkingDirectory: Environment.CurrentDirectory,
            ApprovalMode: section["ApprovalMode"] ?? "never",
            OodlePath: section["OodlePath"]);
    }

    private static IEnumerable<ChatSseEvent> ConvertToSseEvents(CodexParsedEvent parsedEvent)
    {
        switch (parsedEvent.EventType)
        {
            case CodexParsedEventType.AgentMessage:
                yield return Sse("message", new { type = "agent_message", text = parsedEvent.Message });
                break;

            case CodexParsedEventType.McpToolCall when parsedEvent.PayloadJson is not null:
                foreach (var e in ParseToolCallEvent(parsedEvent.PayloadJson))
                    yield return e;
                break;

            case CodexParsedEventType.FinalMessage when !string.IsNullOrWhiteSpace(parsedEvent.Message):
                yield return Sse("message", new { type = "final_message", text = parsedEvent.Message });
                break;

            case CodexParsedEventType.Error:
                yield return Sse("error", new { type = "error", text = parsedEvent.Message });
                break;

            case CodexParsedEventType.Cancelled:
                yield return Sse("error", new { type = "cancelled", text = "Run cancelled." });
                break;

            case CodexParsedEventType.CommandExecution when parsedEvent.PayloadJson is not null:
                foreach (var e in ParseCommandEvent(parsedEvent.PayloadJson))
                    yield return e;
                break;
        }
    }

    private static IEnumerable<ChatSseEvent> ParseToolCallEvent(string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;
        var tool = root.TryGetProperty("tool", out var t) ? t.GetString() ?? "unknown" : "unknown";
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "unknown" : "unknown";
        var resultText = root.TryGetProperty("resultText", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString()
            : null;

        object arguments;
        if (root.TryGetProperty("arguments", out var a) && a.ValueKind != JsonValueKind.Null)
        {
            if (a.ValueKind == JsonValueKind.String)
            {
                // Arguments was serialized as a JSON string; unwrap to object
                try
                {
                    arguments = JsonDocument.Parse(a.GetString()!).RootElement.Clone();
                }
                catch
                {
                    arguments = a.GetString()!;
                }
            }
            else
            {
                arguments = a.Clone();
            }
        }
        else
        {
            arguments = new { };
        }

        yield return Sse("tool_call", new
        {
            type = "tool_call",
            tool,
            arguments,
            status,
            resultText
        });
    }

    private static IEnumerable<ChatSseEvent> ParseCommandEvent(string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;
        var command = root.TryGetProperty("command", out var c) ? c.GetString() ?? "" : "";
        var exitCode = root.TryGetProperty("exitCode", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : (int?)null;

        yield return Sse("command", new
        {
            type = "command",
            command,
            exitCode
        });
    }

    private static ChatSseEvent Sse(string eventName, object data)
    {
        return new ChatSseEvent(eventName, JsonSerializer.Serialize(data, JsonOptions));
    }
}
