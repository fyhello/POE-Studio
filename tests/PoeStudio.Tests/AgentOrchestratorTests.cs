using PoeStudio.Contracts;
using PoeStudio.Core.Agent;
using PoeStudio.Storage.Agent;

namespace PoeStudio.Tests;

public sealed class AgentOrchestratorTests
{
    [Theory]
    [InlineData("question")]
    [InlineData("read-only-analysis")]
    public async Task StartRunAsync_completes_read_only_capabilities_without_approval(string taskKind)
    {
        var workspace = CreateWorkspace();
        var store = new AgentStore(workspace);
        await store.SaveSettingsAsync(Settings(workspace), CancellationToken.None);
        var orchestrator = new AgentOrchestrator(
            store,
            new AgentPromptBuilder(),
            new Datc64TranslationDraftParser(),
            new FakeRunner("""
                ```json
                {"taskKind":"question","profileId":"profile-1","summary":"done","evidence":[]}
                ```
                """));
        var thread = await store.SaveNewThreadAsync("profile-1", "Task", "Goal", taskKind, CancellationToken.None);

        var run = await orchestrator.StartRunAsync(thread.Id, "profile-1", "Goal", taskKind, null, CancellationToken.None);

        Assert.Equal(AgentRunStatus.Succeeded, run.Status);
        Assert.NotNull(run.ResultJson);
        Assert.Empty(await store.ListApprovalsAsync(thread.Id, run.Id, CancellationToken.None));
        Assert.Contains(await store.ListEventsAsync(thread.Id, run.Id, 0, CancellationToken.None), x => x.Type == AgentEventType.RunCreated);
        Assert.Contains(await store.GetPlanAsync(thread.Id, run.Id, CancellationToken.None), x => x.Status == "completed");
    }

    [Fact]
    public async Task StartRunAsync_creates_pending_approval_for_datc64_translation_without_overlay_write()
    {
        var workspace = CreateWorkspace();
        var store = new AgentStore(workspace);
        await store.SaveSettingsAsync(Settings(workspace), CancellationToken.None);
        var orchestrator = new AgentOrchestrator(
            store,
            new AgentPromptBuilder(),
            new Datc64TranslationDraftParser(),
            new FakeRunner("""
                ```json
                {
                  "taskKind": "datc64-translation",
                  "profileId": "profile-1",
                  "resourcePath": "metadata/example.datc64",
                  "candidates": [
                    {
                      "locator": "row:1;column:3;name:text_3 @12",
                      "rowIndex": 0,
                      "columnIndex": 3,
                      "sourceText": "NoMana",
                      "translatedText": "法力不足",
                      "confidence": 0.86,
                      "notes": "game UI prompt text"
                    }
                  ]
                }
                ```
                """));
        var thread = await store.SaveNewThreadAsync("profile-1", "Task", "Goal", "datc64-translation", CancellationToken.None);

        var run = await orchestrator.StartRunAsync(thread.Id, "profile-1", "Goal", "datc64-translation", "metadata/example.datc64", CancellationToken.None);

        Assert.Equal(AgentRunStatus.WaitingForApproval, run.Status);
        Assert.Null(run.ResultJson);
        var approvals = await store.ListApprovalsAsync(thread.Id, run.Id, CancellationToken.None);
        var approval = Assert.Single(approvals);
        Assert.Equal(AgentApprovalStatus.Pending, approval.Status);
        var proposal = new Datc64TranslationDraftParser().Parse(
            $"```json{Environment.NewLine}{approval.ProposalJson}{Environment.NewLine}```",
            "profile-1",
            "metadata/example.datc64");
        Assert.Equal("法力不足", proposal.Candidates[0].TranslatedText);
    }

    [Fact]
    public async Task StartRunAsync_records_failed_run_when_runner_fails()
    {
        var workspace = CreateWorkspace();
        var store = new AgentStore(workspace);
        await store.SaveSettingsAsync(Settings(workspace), CancellationToken.None);
        var orchestrator = new AgentOrchestrator(
            store,
            new AgentPromptBuilder(),
            new Datc64TranslationDraftParser(),
            new FakeRunner("failed", exitCode: 9, stderr: "boom"));
        var thread = await store.SaveNewThreadAsync("profile-1", "Task", "Goal", "question", CancellationToken.None);

        var run = await orchestrator.StartRunAsync(thread.Id, "profile-1", "Goal", "question", null, CancellationToken.None);

        Assert.Equal(AgentRunStatus.Failed, run.Status);
        Assert.Equal("codex_failed", run.ErrorCode);
        Assert.Contains("boom", run.ErrorMessage);
        Assert.Contains(await store.ListEventsAsync(thread.Id, run.Id, 0, CancellationToken.None), x => x.Type == AgentEventType.RunFailed);
    }

    [Fact]
    public async Task RetryAsync_creates_new_attempt_without_overwriting_old_events()
    {
        var workspace = CreateWorkspace();
        var store = new AgentStore(workspace);
        await store.SaveSettingsAsync(Settings(workspace), CancellationToken.None);
        var orchestrator = new AgentOrchestrator(
            store,
            new AgentPromptBuilder(),
            new Datc64TranslationDraftParser(),
            new FakeRunner("""
                ```json
                {"taskKind":"question","profileId":"profile-1","summary":"done","evidence":[]}
                ```
                """));
        var thread = await store.SaveNewThreadAsync("profile-1", "Task", "Goal", "question", CancellationToken.None);
        var first = await orchestrator.StartRunAsync(thread.Id, "profile-1", "Goal", "question", null, CancellationToken.None);

        var retry = await orchestrator.RetryAsync(first.Id, CancellationToken.None);

        Assert.NotEqual(first.Id, retry.Id);
        Assert.NotEmpty(await store.ListEventsAsync(thread.Id, first.Id, 0, CancellationToken.None));
        Assert.NotEmpty(await store.ListEventsAsync(thread.Id, retry.Id, 0, CancellationToken.None));
    }

    private static AgentSettingsDto Settings(string workspace)
    {
        return new AgentSettingsDto("codex", null, null, "workspace-write", "poe-studio", workspace, "manual");
    }

    private static string CreateWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "poe-studio-agent-orchestrator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeRunner : ICodexProcessRunner
    {
        private readonly string _message;
        private readonly int _exitCode;
        private readonly string? _stderr;

        public FakeRunner(string message, int exitCode = 0, string? stderr = null)
        {
            _message = message;
            _exitCode = exitCode;
            _stderr = stderr;
        }

        public Task<CodexRunResult> RunAsync(AgentSettingsDto settings, string prompt, CancellationToken cancellationToken)
        {
            var events = new[]
            {
                new CodexParsedEvent("{}", CodexParsedEventType.McpToolCall, "poe_get_workspace completed", "{}", false, true, "poe_get_workspace"),
                new CodexParsedEvent("{}", CodexParsedEventType.AgentMessage, _message, "{}", true, false, null)
            };
            return Task.FromResult(new CodexRunResult(
                _exitCode,
                _exitCode != 0,
                false,
                events,
                _stderr));
        }
    }
}
