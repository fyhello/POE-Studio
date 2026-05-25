using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PoeStudio.Api;
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;
using PoeStudio.Storage.Agent;

namespace PoeStudio.Tests;

public sealed class ChatServiceIntegrationTests
{
    [Fact]
    public async Task RunCodexAsync_returns_message_event()
    {
        var service = CreateChatService(events: [
            new CodexParsedEvent("{}", CodexParsedEventType.AgentMessage, "hello", null, true, false, null)
        ]);

        var results = await CollectEvents(service, "hi", null, null);

        Assert.Contains(results, e => e.EventName == "message" && e.DataJson.Contains("hello"));
    }

    [Fact]
    public async Task RunCodexAsync_returns_tool_call_event()
    {
        var payload = """{"tool":"poe_list_profiles","arguments":{"limit":5},"status":"completed"}""";
        var service = CreateChatService(events: [
            new CodexParsedEvent("{}", CodexParsedEventType.McpToolCall, "", payload, false, true, "poe_list_profiles")
        ]);

        var results = await CollectEvents(service, "list profiles", null, null);

        Assert.Contains(results, e => e.EventName == "tool_call");
        var toolCall = results.First(e => e.EventName == "tool_call");
        Assert.Contains("poe_list_profiles", toolCall.DataJson);
    }

    [Fact]
    public async Task RunCodexAsync_tool_call_arguments_are_json_object_not_escaped_string()
    {
        // arguments in PayloadJson is a JSON object → must serialize as object in SSE, not as escaped string
        var payload = """{"tool":"poe_get_profile","arguments":{"profileId":"test-123"},"status":"completed"}""";
        var service = CreateChatService(events: [
            new CodexParsedEvent("{}", CodexParsedEventType.McpToolCall, "", payload, false, true, "poe_get_profile")
        ]);

        var results = await CollectEvents(service, "get profile", null, null);

        var toolCall = results.First(e => e.EventName == "tool_call");
        using var data = JsonDocument.Parse(toolCall.DataJson);
        var args = data.RootElement.GetProperty("arguments");
        Assert.Equal(JsonValueKind.Object, args.ValueKind);
        Assert.Equal("test-123", args.GetProperty("profileId").GetString());
    }

    [Fact]
    public async Task RunCodexAsync_tool_call_arguments_unwraps_double_encoded_string()
    {
        // arguments in PayloadJson was double-encoded as a JSON string (e.g. "{\"profileId\":\"test-456\"}")
        // → ParseToolCallEvent must unwrap it to a JSON object
        var payload = """{"tool":"poe_get_profile","arguments":"{\"profileId\":\"test-456\"}","status":"completed"}""";
        var service = CreateChatService(events: [
            new CodexParsedEvent("{}", CodexParsedEventType.McpToolCall, "", payload, false, true, "poe_get_profile")
        ]);

        var results = await CollectEvents(service, "get profile", null, null);

        var toolCall = results.First(e => e.EventName == "tool_call");
        using var data = JsonDocument.Parse(toolCall.DataJson);
        var args = data.RootElement.GetProperty("arguments");
        Assert.Equal(JsonValueKind.Object, args.ValueKind);
        Assert.Equal("test-456", args.GetProperty("profileId").GetString());
    }

    [Fact]
    public async Task RunCodexAsync_tool_call_includes_result_text()
    {
        var payload = """{"tool":"poe_find_current_table_untranslated_cells","arguments":{"limit":3},"status":"completed","resultText":"{\"candidates\":3}"}""";
        var service = CreateChatService(events: [
            new CodexParsedEvent("{}", CodexParsedEventType.McpToolCall, "", payload, false, true, "poe_find_current_table_untranslated_cells")
        ]);

        var results = await CollectEvents(service, "find untranslated cells", null, null);

        var toolCall = results.First(e => e.EventName == "tool_call");
        using var data = JsonDocument.Parse(toolCall.DataJson);
        Assert.Equal("{\"candidates\":3}", data.RootElement.GetProperty("resultText").GetString());
    }

    [Fact]
    public async Task RunCodexAsync_returns_error_event_when_runner_fails()
    {
        var service = CreateChatService(failed: true, stderrSummary: "Something went wrong");

        var results = await CollectEvents(service, "do something", null, null);

        Assert.Contains(results, e => e.EventName == "error" && e.DataJson.Contains("Something went wrong"));
    }

    [Fact]
    public async Task RunCodexAsync_returns_done_after_success()
    {
        var service = CreateChatService(events: [
            new CodexParsedEvent("{}", CodexParsedEventType.AgentMessage, "ok", null, true, false, null)
        ]);

        var results = await CollectEvents(service, "hi", null, null);

        Assert.Contains(results, e => e.EventName == "done");
        Assert.Equal("done", results[^1].EventName);
    }

    [Fact]
    public async Task RunCodexAsync_emits_run_started_and_persists_trace_events()
    {
        var workspaceRoot = CreateWorkspaceRoot();
        var service = CreateChatService(new FakeCodexRunner(async (settings, prompt, onEvent, ct) =>
        {
            if (onEvent is not null)
            {
                await onEvent(new CodexParsedEvent("{}", CodexParsedEventType.McpToolCall, "", """{"tool":"poe_get_workspace","arguments":{},"status":"completed"}""", false, true, "poe_get_workspace"));
            }

            return new CodexRunResult(0, false, false, [], null);
        }), workspaceRoot);

        var results = await CollectEvents(service, "trace this", null, null);

        var runEvent = Assert.Single(results, e => e.EventName == "run");
        using var runPayload = JsonDocument.Parse(runEvent.DataJson);
        var runId = runPayload.RootElement.GetProperty("runId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(runId));
        var trace = await new AgentRunTraceStore(workspaceRoot.CurrentRoot).ReadAsync(runId!, CancellationToken.None);
        Assert.Contains(trace, e => e.EventName == "run" && e.DataJson.Contains("\"runMode\":\"normal\""));
        Assert.Contains(trace, e => e.EventName == "codex_event" && e.DataJson.Contains("poe_get_workspace"));
        Assert.Contains(trace, e => e.EventName == "tool_call" && e.DataJson.Contains("poe_get_workspace"));
    }

    [Fact]
    public async Task RunCodexAsync_sends_done_before_diagnostic_when_final_answer_is_missing()
    {
        var service = CreateChatService(new FakeCodexRunner(async (settings, prompt, onEvent, ct) =>
        {
            if (prompt.Contains("You are diagnosing a failed POE Studio Agent run.", StringComparison.Ordinal))
            {
                if (onEvent is not null)
                {
                    await onEvent(new CodexParsedEvent("{}", CodexParsedEventType.AgentMessage, "root cause", null, false, false, null));
                }

                return new CodexRunResult(0, false, false, [], null);
            }

            if (onEvent is not null)
            {
                await onEvent(new CodexParsedEvent("{}", CodexParsedEventType.McpToolCall, "", """{"tool":"poe_get_workspace","arguments":{},"status":"completed"}""", false, true, "poe_get_workspace"));
            }

            return new CodexRunResult(0, false, false, [], null);
        }), CreateWorkspaceRoot());

        var results = await CollectEvents(service, "tool then silence", null, null);

        var doneIndex = results.FindIndex(e => e.EventName == "done");
        var diagnosticIndex = results.FindIndex(e => e.EventName == "diagnostic");
        Assert.True(doneIndex >= 0);
        Assert.True(diagnosticIndex > doneIndex);
        Assert.Contains("\"autoDiagnostic\":true", results[doneIndex].DataJson);
        Assert.Contains("no_final_answer_after_tool_result", results[diagnosticIndex].DataJson);
        Assert.Contains(results, e => e.EventName == "message" && e.DataJson.Contains("root cause"));
    }

    [Fact]
    public async Task RunCodexAsync_detects_hung_tool_call_while_runner_is_still_running()
    {
        var normalRunCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateChatService(new FakeCodexRunner(async (settings, prompt, onEvent, ct) =>
        {
            if (prompt.Contains("You are diagnosing a failed POE Studio Agent run.", StringComparison.Ordinal))
            {
                if (onEvent is not null)
                {
                    await onEvent(new CodexParsedEvent("{}", CodexParsedEventType.AgentMessage, "diagnosed hung tool", null, false, false, null));
                }

                return new CodexRunResult(0, false, false, [], null);
            }

            if (onEvent is not null)
            {
                await onEvent(new CodexParsedEvent("{}", CodexParsedEventType.McpToolCall, "", """{"tool":"poe_get_workspace","arguments":{},"status":"in_progress"}""", false, true, "poe_get_workspace"));
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                normalRunCancelled.SetResult();
                return new CodexRunResult(null, false, true, [], null);
            }

            throw new InvalidOperationException("The fake normal run should be cancelled by the watchdog.");
        }), CreateWorkspaceRoot(), toolHangThreshold: TimeSpan.FromMilliseconds(50));

        var results = await CollectEvents(service, "hang after tool start", null, null);

        Assert.True(normalRunCancelled.Task.IsCompleted, "The watchdog should cancel the original hung run.");
        var doneIndex = results.FindIndex(e => e.EventName == "done");
        var diagnosticIndex = results.FindIndex(e => e.EventName == "diagnostic");
        Assert.True(doneIndex >= 0);
        Assert.True(diagnosticIndex > doneIndex);
        Assert.Contains("\"autoDiagnostic\":true", results[doneIndex].DataJson);
        Assert.Contains("tool_call_left_in_progress", results[diagnosticIndex].DataJson);
        Assert.Contains(results, e => e.EventName == "message" && e.DataJson.Contains("diagnosed hung tool"));
    }

    [Fact]
    public async Task RunCodexAsync_detects_completed_tool_followed_only_by_thinking_messages_while_runner_is_still_running()
    {
        var normalRunCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateChatService(new FakeCodexRunner(async (settings, prompt, onEvent, ct) =>
        {
            if (prompt.Contains("You are diagnosing a failed POE Studio Agent run.", StringComparison.Ordinal))
            {
                if (onEvent is not null)
                {
                    await onEvent(new CodexParsedEvent("{}", CodexParsedEventType.AgentMessage, "diagnosed thinking loop", null, false, false, null));
                }

                return new CodexRunResult(0, false, false, [], null);
            }

            if (onEvent is not null)
            {
                await onEvent(new CodexParsedEvent("{}", CodexParsedEventType.McpToolCall, "", """{"tool":"poe_get_current_view_context","arguments":{},"status":"completed"}""", false, true, "poe_get_current_view_context"));
                await onEvent(new CodexParsedEvent("{}", CodexParsedEventType.AgentMessage, "思考中...", null, false, false, null));
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                normalRunCancelled.SetResult();
                return new CodexRunResult(null, false, true, [], null);
            }

            throw new InvalidOperationException("The fake normal run should be cancelled by the watchdog.");
        }), CreateWorkspaceRoot(), toolHangThreshold: TimeSpan.FromMilliseconds(50));

        var results = await CollectEvents(service, "current table untranslated", null, null);

        Assert.True(normalRunCancelled.Task.IsCompleted, "The watchdog should cancel the thinking-loop run.");
        var doneIndex = results.FindIndex(e => e.EventName == "done");
        var diagnosticIndex = results.FindIndex(e => e.EventName == "diagnostic");
        Assert.True(doneIndex >= 0);
        Assert.True(diagnosticIndex > doneIndex);
        Assert.Contains("\"autoDiagnostic\":true", results[doneIndex].DataJson);
        Assert.Contains("no_final_answer_after_tool_result", results[diagnosticIndex].DataJson);
        Assert.Contains(results, e => e.EventName == "message" && e.DataJson.Contains("diagnosed thinking loop"));
    }

    [Fact]
    public async Task RunCodexAsync_diagnostic_prompt_requires_trace_first_and_does_not_modify_files()
    {
        var prompts = new List<string>();
        var service = CreateChatService(new FakeCodexRunner(async (settings, prompt, onEvent, ct) =>
        {
            prompts.Add(prompt);
            if (prompts.Count == 1 && onEvent is not null)
            {
                await onEvent(new CodexParsedEvent("{}", CodexParsedEventType.McpToolCall, "", """{"tool":"poe_get_workspace","arguments":{},"status":"completed"}""", false, true, "poe_get_workspace"));
            }

            return new CodexRunResult(0, false, false, [], null);
        }), CreateWorkspaceRoot());

        await CollectEvents(service, "tool then silence", null, null);

        var diagnosticPrompt = Assert.Single(prompts.Where(prompt => prompt.Contains("You are diagnosing a failed POE Studio Agent run.", StringComparison.Ordinal)));
        Assert.Contains("Use poe_get_agent_run_trace first.", diagnosticPrompt);
        Assert.Contains("Do not modify files in diagnostic mode.", diagnosticPrompt);
    }

    [Fact]
    public async Task RunCodexAsync_reports_diagnostic_failure_without_recursive_diagnosis()
    {
        var service = CreateChatService(new FakeCodexRunner((settings, prompt, onEvent, ct) =>
        {
            if (prompt.Contains("You are diagnosing a failed POE Studio Agent run.", StringComparison.Ordinal))
            {
                return Task.FromResult(new CodexRunResult(1, true, false, [], "diagnostic crashed"));
            }

            if (onEvent is not null)
            {
                return onEvent(new CodexParsedEvent("{}", CodexParsedEventType.McpToolCall, "", """{"tool":"poe_get_workspace","arguments":{},"status":"completed"}""", false, true, "poe_get_workspace"))
                    .ContinueWith(_ => new CodexRunResult(0, false, false, [], null));
            }

            return Task.FromResult(new CodexRunResult(0, false, false, [], null));
        }), CreateWorkspaceRoot());

        var results = await CollectEvents(service, "tool then diagnostic failure", null, null);

        var diagnosticEvents = results.Where(e => e.EventName == "diagnostic").ToArray();
        Assert.Equal(2, diagnosticEvents.Length);
        Assert.Contains("diagnostic_started", diagnosticEvents[0].DataJson);
        Assert.Contains("diagnostic_failed", diagnosticEvents[1].DataJson);
        using var failedPayload = JsonDocument.Parse(diagnosticEvents[1].DataJson);
        Assert.Contains(
            "诊断失败，不再递归诊断",
            failedPayload.RootElement.GetProperty("finding").GetProperty("summary").GetString());
    }

    [Fact]
    public async Task RunCodexAsync_returns_done_after_error()
    {
        var service = CreateChatService(failed: true, stderrSummary: "error");

        var results = await CollectEvents(service, "do something", null, null);

        Assert.Contains(results, e => e.EventName == "done");
        Assert.Equal("done", results[^1].EventName);
    }

    [Fact]
    public async Task RunCodexAsync_returns_done_after_cancellation()
    {
        var cts = new CancellationTokenSource();
        var service = CreateChatService(events: [
            new CodexParsedEvent("{}", CodexParsedEventType.AgentMessage, "before cancel", null, false, false, null)
        ], delay: 5000);

        // Start but cancel immediately
        var task = CollectEvents(service, "test", null, null, cts.Token);
        cts.Cancel();
        var results = await task;

        Assert.Contains(results, e => e.EventName == "error" && e.DataJson.Contains("cancelled"));
        Assert.Contains(results, e => e.EventName == "done");
    }

    [Fact]
    public async Task RunCodexAsync_includes_profileId_in_prompt()
    {
        string? capturedPrompt = null;
        var runner = new FakeCodexRunner((settings, prompt, onEvent, ct) =>
        {
            capturedPrompt = prompt;
            return Task.FromResult(new CodexRunResult(0, false, false, [], null));
        });
        var workspaceRoot = CreateWorkspaceRoot();
        var service = CreateChatService(runner, workspaceRoot);

        await CollectEvents(service, "hello", "profile-123", null);

        Assert.NotNull(capturedPrompt);
        Assert.Contains("profile-123", capturedPrompt);
        Assert.Contains("activeProfileId", capturedPrompt);
    }

    [Fact]
    public async Task RunCodexAsync_includes_resourcePath_in_prompt()
    {
        string? capturedPrompt = null;
        var runner = new FakeCodexRunner((settings, prompt, onEvent, ct) =>
        {
            capturedPrompt = prompt;
            return Task.FromResult(new CodexRunResult(0, false, false, [], null));
        });
        var workspaceRoot = CreateWorkspaceRoot();
        var service = CreateChatService(runner, workspaceRoot);

        await CollectEvents(service, "edit this", null, "data/balance/Stats.datc64");

        Assert.NotNull(capturedPrompt);
        Assert.Contains("data/balance/Stats.datc64", capturedPrompt);
        Assert.Contains("selectedResourcePath", capturedPrompt);
    }

    [Fact]
    public async Task RunCodexAsync_includes_sourceTargetProfileId_in_prompt()
    {
        string? capturedPrompt = null;
        var runner = new FakeCodexRunner((settings, prompt, onEvent, ct) =>
        {
            capturedPrompt = prompt;
            return Task.FromResult(new CodexRunResult(0, false, false, [], null));
        });
        var workspaceRoot = CreateWorkspaceRoot();
        var service = CreateChatService(runner, workspaceRoot);

        await foreach (var e in service.RunCodexAsync("translate table", null, null, "src-profile", "tgt-profile", null, null, null, CancellationToken.None))
        {
        }

        Assert.NotNull(capturedPrompt);
        Assert.Contains("sourceProfileId: src-profile", capturedPrompt);
        Assert.Contains("targetProfileId: tgt-profile", capturedPrompt);
        Assert.Contains("translationPair", capturedPrompt);
    }

    [Fact]
    public async Task RunCodexAsync_includes_sourceTargetResourcePath_in_prompt()
    {
        string? capturedPrompt = null;
        var runner = new FakeCodexRunner((settings, prompt, onEvent, ct) =>
        {
            capturedPrompt = prompt;
            return Task.FromResult(new CodexRunResult(0, false, false, [], null));
        });
        var workspaceRoot = CreateWorkspaceRoot();
        var service = CreateChatService(runner, workspaceRoot);

        await foreach (var e in service.RunCodexAsync("translate table", null, null, null, null, "src/data.bin", "tgt/data.bin", null, CancellationToken.None))
        {
        }

        Assert.NotNull(capturedPrompt);
        Assert.Contains("sourceResourcePath: src/data.bin", capturedPrompt);
        Assert.Contains("targetResourcePath: tgt/data.bin", capturedPrompt);
    }

    [Fact]
    public async Task RunCodexAsync_includes_current_view_context_id_and_current_table_rule()
    {
        string? capturedPrompt = null;
        var runner = new FakeCodexRunner((settings, prompt, onEvent, ct) =>
        {
            capturedPrompt = prompt;
            return Task.FromResult(new CodexRunResult(0, false, false, [], null));
        });
        var root = Path.Combine(Path.GetTempPath(), "poe-chat-current-view-" + Guid.NewGuid().ToString("N"));
        var service = CreateChatService(runner, CreateWorkspaceRoot(root));
        var view = new AgentCurrentViewRequestDto(
            "tableComparison",
            new AgentCurrentTableViewDto(
                "target",
                "data/balance/traditional chinese/activeskills.datc64",
                "source",
                "data/balance/simplified chinese/activeskills.datc64",
                "target",
                "data/balance/traditional chinese/activeskills.datc64",
                "datc64-auto",
                1,
                1,
                ["Id", "Name"],
                [1],
                [new AgentCurrentTableRowDto(1, ["skill", "Fireball"])],
                [new AgentCurrentTableRowDto(1, ["skill", "火球"])],
                "简体路径"));

        await foreach (var _ in service.RunCodexAsync(
            "检查当前表格漏翻",
            "target",
            "data/balance/traditional chinese/activeskills.datc64",
            "source",
            "target",
            "data/balance/simplified chinese/activeskills.datc64",
            "data/balance/traditional chinese/activeskills.datc64",
            view,
            CancellationToken.None))
        {
        }

        Assert.NotNull(capturedPrompt);
        Assert.Contains("currentViewContextId:", capturedPrompt);
        Assert.Contains("When the user says current table", capturedPrompt);
        Assert.Contains("poe_get_current_view_context", capturedPrompt);
        Assert.Contains("poe_find_current_table_untranslated_cells", capturedPrompt);
        Assert.Contains("Do not call poe_datc64_extract_translatable_cells for current table checks", capturedPrompt);
    }

    [Fact]
    public async Task Prompt_directs_current_table_missing_translation_to_current_view_tools()
    {
        string? capturedPrompt = null;
        var runner = new FakeCodexRunner((settings, prompt, onEvent, ct) =>
        {
            capturedPrompt = prompt;
            return Task.FromResult(new CodexRunResult(0, false, false, [], null));
        });
        var root = Path.Combine(Path.GetTempPath(), "poe-current-table-rule-" + Guid.NewGuid().ToString("N"));
        var service = CreateChatService(runner, CreateWorkspaceRoot(root));
        var view = new AgentCurrentViewRequestDto(
            "tableComparison",
            new AgentCurrentTableViewDto(
                "target",
                "target.datc64",
                "source",
                "source.datc64",
                "target",
                "target.datc64",
                "datc64-auto",
                1,
                1,
                ["Text"],
                [0],
                [new AgentCurrentTableRowDto(1, ["Fireball"])],
                [new AgentCurrentTableRowDto(1, ["火球"])],
                "简体路径"));

        await foreach (var _ in service.RunCodexAsync("检查当前表格漏翻的内容", "target", "target.datc64", "source", "target", "source.datc64", "target.datc64", view, CancellationToken.None))
        {
        }

        Assert.NotNull(capturedPrompt);
        Assert.Contains("poe_find_current_table_untranslated_cells", capturedPrompt);
        Assert.Contains("Do not call poe_datc64_extract_translatable_cells for current table checks", capturedPrompt);
    }

    [Fact]
    public async Task Prompt_uses_knowledge_contract_and_task_frame_without_full_knowledge_dump()
    {
        string? capturedPrompt = null;
        var runner = new FakeCodexRunner((settings, prompt, onEvent, ct) =>
        {
            capturedPrompt = prompt;
            return Task.FromResult(new CodexRunResult(0, false, false, [], null));
        });
        var root = Path.Combine(Path.GetTempPath(), "poe-chat-knowledge-" + Guid.NewGuid().ToString("N"));
        var service = CreateChatService(runner, CreateWorkspaceRoot(root));
        var view = new AgentCurrentViewRequestDto(
            "tableComparison",
            new AgentCurrentTableViewDto(
                "target",
                "data/balance/traditional chinese/activeskills.datc64",
                "source",
                "data/balance/simplified chinese/activeskills.datc64",
                "target",
                "data/balance/traditional chinese/activeskills.datc64",
                "datc64-auto",
                1,
                1,
                ["Id", "Description @16"],
                [1],
                [new AgentCurrentTableRowDto(16, ["molten_crash", "變形"])],
                [new AgentCurrentTableRowDto(16, ["molten_crash", "变形"])],
                "简体路径"));

        await foreach (var _ in service.RunCodexAsync(
            "检查当前表格中还没有翻译成简中内容的繁中单元格",
            "target",
            "data/balance/traditional chinese/activeskills.datc64",
            "source",
            "target",
            "data/balance/simplified chinese/activeskills.datc64",
            "data/balance/traditional chinese/activeskills.datc64",
            view,
            CancellationToken.None))
        {
        }

        Assert.NotNull(capturedPrompt);
        Assert.Contains("poe_get_project_knowledge", capturedPrompt);
        Assert.Contains("Task Frame", capturedPrompt);
        Assert.Contains("toolFitCheck", capturedPrompt);
        Assert.Contains("source/current source means reference", capturedPrompt);
        Assert.Contains("target/current target means editable", capturedPrompt);
        Assert.DoesNotContain("This file is the always-on POE Studio Agent contract", capturedPrompt);
    }

    private static ChatService CreateChatService(
        List<CodexParsedEvent>? events = null,
        bool failed = false,
        string? stderrSummary = null,
        int delay = 0)
    {
        var runner = new FakeCodexRunner(async (settings, prompt, onEvent, ct) =>
        {
            if (delay > 0) await Task.Delay(delay, ct);
            if (events is not null)
            {
                foreach (var e in events)
                {
                    if (onEvent is not null) await onEvent(e);
                }
            }
            return new CodexRunResult(failed ? 1 : 0, failed, false, events ?? [], failed ? stderrSummary : null);
        });
        var workspaceRoot = CreateWorkspaceRoot();
        return CreateChatService(runner, workspaceRoot);
    }

    private static ChatService CreateChatService(
        FakeCodexRunner runner,
        WorkspaceRootProvider workspaceRoot,
        TimeSpan? toolHangThreshold = null)
    {
        return new ChatService(
            runner,
            workspaceRoot,
            BuildConfig(),
            new AgentCurrentViewStore(workspaceRoot.CurrentRoot),
            new AgentRunTraceStore(workspaceRoot.CurrentRoot),
            toolHangThreshold);
    }

    private static async Task<List<ChatSseEvent>> CollectEvents(
        ChatService service,
        string message,
        string? profileId,
        string? resourcePath,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ChatSseEvent>();
        await foreach (var e in service.RunCodexAsync(message, profileId, resourcePath, null, null, null, null, null, cancellationToken))
        {
            results.Add(e);
        }
        return results;
    }

    private sealed class FakeCodexRunner : ICodexProcessRunner
    {
        private readonly Func<AgentSettingsDto, string, Func<CodexParsedEvent, Task>?, CancellationToken, Task<CodexRunResult>> _handler;

        public FakeCodexRunner(Func<AgentSettingsDto, string, Func<CodexParsedEvent, Task>?, CancellationToken, Task<CodexRunResult>> handler)
        {
            _handler = handler;
        }

        public Task<CodexRunResult> RunAsync(AgentSettingsDto settings, string prompt, Func<CodexParsedEvent, Task>? onEvent, CancellationToken cancellationToken)
            => _handler(settings, prompt, onEvent, cancellationToken);
    }

    private static WorkspaceRootProvider CreateWorkspaceRoot()
    {
        return CreateWorkspaceRoot(Path.Combine(Path.GetTempPath(), "poe-studio-test-" + Guid.NewGuid().ToString("N")));
    }

    private static WorkspaceRootProvider CreateWorkspaceRoot(string root)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PoeStudio:WorkspaceRoot"] = root
            })
            .Build();
        return new WorkspaceRootProvider(config);
    }

    private static IConfiguration BuildConfig()
    {
        return new ConfigurationBuilder().AddInMemoryCollection().Build();
    }
}
