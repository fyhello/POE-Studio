using PoeStudio.Api;
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;
using PoeStudio.Storage.Agent;

namespace PoeStudio.Tests;

public sealed class AgentRepairServiceTests
{
    [Fact]
    public async Task StartRepairAsync_rejects_missing_user_approval()
    {
        var service = CreateService();

        var result = await service.StartRepairAsync("abc", "no_final_answer_after_tool_result", userApproved: false, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("approval", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartRepairAsync_uses_repair_codex_capabilities_after_approval()
    {
        AgentSettingsDto? capturedSettings = null;
        string? capturedPrompt = null;
        var runnerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateService((settings, prompt, onEvent, ct) =>
        {
            capturedSettings = settings;
            capturedPrompt = prompt;
            runnerStarted.SetResult();
            return Task.FromResult(new CodexRunResult(0, false, false, [], null));
        });

        var result = await service.StartRepairAsync(Guid.NewGuid().ToString("N"), "no_final_answer_after_tool_result", userApproved: true, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.NotNull(result.RepairRunId);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(capturedSettings);
        Assert.Equal("workspace-write", capturedSettings!.Sandbox);
        Assert.Equal("never", capturedSettings.ApprovalMode);
        Assert.False(capturedSettings.Memories);
        Assert.False(capturedSettings.Skills);
        Assert.True(capturedSettings.CommandExecution);
        Assert.Contains("Run mode: repair.", capturedPrompt);
        Assert.Contains("git status --short --branch", capturedPrompt);
    }

    [Fact]
    public async Task StartRepairAsync_returns_repair_run_id_before_background_runner_finishes_and_traces_events()
    {
        var runnerCanFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerStarted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var root = CreateTempRoot();
        var service = CreateService(root, async (settings, prompt, onEvent, ct) =>
        {
            runnerStarted.SetResult(prompt);
            if (onEvent is not null)
            {
                await onEvent(new CodexParsedEvent("{}", CodexParsedEventType.AgentMessage, "repair step 1", null, false, false, null));
            }

            await runnerCanFinish.Task.WaitAsync(ct);
            return new CodexRunResult(0, false, false, [], null);
        });

        var result = await service.StartRepairAsync(Guid.NewGuid().ToString("N"), "tool_call_left_in_progress", userApproved: true, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.False(runnerCanFinish.Task.IsCompleted);
        Assert.False(string.IsNullOrWhiteSpace(result.RepairRunId));

        var startedTrace = await WaitForTraceEventAsync(root, result.RepairRunId!, evt => evt.EventName == "run" && evt.Status == "started");
        Assert.Contains(startedTrace, evt => evt.EventName == "run" && evt.DataJson.Contains("gitStatus", StringComparison.Ordinal));
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var trace = await WaitForTraceEventAsync(root, result.RepairRunId!, evt => evt.EventName == "codex_event" && evt.DataJson.Contains("repair step 1", StringComparison.Ordinal));
        Assert.Contains(trace, evt => evt.EventName == "run" && evt.DataJson.Contains("\"runMode\":\"repair\"", StringComparison.Ordinal));

        runnerCanFinish.SetResult();
        await WaitForTraceEventAsync(root, result.RepairRunId!, evt => evt.EventName == "run" && evt.Status == "completed");
    }

    private static AgentRepairService CreateService(
        Func<AgentSettingsDto, string, Func<CodexParsedEvent, Task>?, CancellationToken, Task<CodexRunResult>>? handler = null)
    {
        return CreateService(CreateTempRoot(), handler);
    }

    private static AgentRepairService CreateService(
        string root,
        Func<AgentSettingsDto, string, Func<CodexParsedEvent, Task>?, CancellationToken, Task<CodexRunResult>>? handler = null)
    {
        Directory.CreateDirectory(root);
        var runner = new FakeCodexRunner(handler ?? ((settings, prompt, onEvent, ct) => Task.FromResult(new CodexRunResult(0, false, false, [], null))));
        return new AgentRepairService(runner, new AgentRunTraceStore(root), root, "codex", null, null, "poe-studio");
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "poe-agent-repair-tests", Guid.NewGuid().ToString("N"));
    }

    private static async Task<IReadOnlyList<AgentRunTraceEventDto>> WaitForTraceEventAsync(
        string root,
        string runId,
        Func<AgentRunTraceEventDto, bool> predicate)
    {
        var store = new AgentRunTraceStore(root);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var trace = await store.ReadAsync(runId, CancellationToken.None);
            if (trace.Any(predicate))
            {
                return trace;
            }

            await Task.Delay(50);
        }

        return await store.ReadAsync(runId, CancellationToken.None);
    }

    private sealed class FakeCodexRunner : ICodexProcessRunner
    {
        private readonly Func<AgentSettingsDto, string, Func<CodexParsedEvent, Task>?, CancellationToken, Task<CodexRunResult>> handler;

        public FakeCodexRunner(Func<AgentSettingsDto, string, Func<CodexParsedEvent, Task>?, CancellationToken, Task<CodexRunResult>> handler)
        {
            this.handler = handler;
        }

        public Task<CodexRunResult> RunAsync(AgentSettingsDto settings, string prompt, Func<CodexParsedEvent, Task>? onEvent, CancellationToken cancellationToken)
            => handler(settings, prompt, onEvent, cancellationToken);
    }
}
