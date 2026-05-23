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
        var orchestrator = CreateOrchestrator(
            store,
            workspace,
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
        var orchestrator = CreateOrchestrator(
            store,
            workspace,
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
        var orchestrator = CreateOrchestrator(store, workspace, new FakeRunner("failed", exitCode: 9, stderr: "boom"));
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
        var orchestrator = CreateOrchestrator(
            store,
            workspace,
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

    [Fact]
    public async Task RetryAsync_preserves_original_resource_path()
    {
        var workspace = CreateWorkspace();
        var store = new AgentStore(workspace);
        await store.SaveSettingsAsync(Settings(workspace), CancellationToken.None);
        var runner = new RecordingRunner("""
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
                  "notes": "test"
                }
              ]
            }
            ```
            """);
        var orchestrator = CreateOrchestrator(store, workspace, runner);
        var thread = await store.SaveNewThreadAsync("profile-1", "Task", "Goal", "datc64-translation", CancellationToken.None);
        var first = await orchestrator.StartRunAsync(thread.Id, "profile-1", "Goal", "datc64-translation", "metadata/example.datc64", CancellationToken.None);

        var retry = await orchestrator.RetryAsync(first.Id, CancellationToken.None);

        Assert.Equal(AgentRunStatus.WaitingForApproval, retry.Status);
        Assert.All(runner.Prompts, prompt => Assert.Contains("metadata/example.datc64", prompt));
    }

    [Fact]
    public async Task RetryAsync_preserves_original_oodle_path()
    {
        var workspace = CreateWorkspace();
        var store = new AgentStore(workspace);
        await store.SaveSettingsAsync(Settings(workspace), CancellationToken.None);
        var runner = new RecordingRunner("""
            ```json
            {
              "taskKind": "datc64-translation",
              "profileId": "profile-1",
              "resourcePath": "metadata/example.datc64",
              "candidates": []
            }
            ```
            """);
        var orchestrator = CreateOrchestrator(store, workspace, runner);
        var thread = await store.SaveNewThreadAsync("profile-1", "Task", "Goal", "datc64-translation", CancellationToken.None);
        var first = await orchestrator.StartRunAsync(
            thread.Id,
            "profile-1",
            "Goal",
            "datc64-translation",
            "metadata/example.datc64",
            "C:/Game/oo2core.dll",
            CancellationToken.None);

        var retry = await orchestrator.RetryAsync(first.Id, CancellationToken.None);

        Assert.Equal(AgentRunStatus.WaitingForApproval, retry.Status);
        Assert.Equal(["C:/Game/oo2core.dll", "C:/Game/oo2core.dll"], runner.OodlePaths);
    }

    [Fact]
    public async Task StartRunAsync_loads_project_context_before_runner_and_records_preflight()
    {
        var workspace = CreateWorkspace();
        using var repository = CreateRepositoryRoot();
        var store = new AgentStore(workspace);
        await store.SaveSettingsAsync(Settings(repository.Root), CancellationToken.None);
        var runner = new RecordingRunner("""
            ```json
            {"taskKind":"question","profileId":"profile-1","summary":"done","evidence":[]}
            ```
            """);
        var orchestrator = CreateOrchestrator(store, repository.Root, runner);
        var thread = await store.SaveNewThreadAsync("profile-1", "Task", "Explain current working state", "question", CancellationToken.None);

        var run = await orchestrator.StartRunAsync(thread.Id, "profile-1", "Explain current working state", "question", null, CancellationToken.None);

        Assert.Equal(AgentRunStatus.Succeeded, run.Status);
        var prompt = Assert.Single(runner.Prompts);
        Assert.Contains("Project context", prompt);
        var events = await store.ListEventsAsync(thread.Id, run.Id, 0, CancellationToken.None);
        var preflight = Assert.Single(events, x => x.Type == AgentEventType.PlanUpdated && x.Message == "Project context loaded");
        Assert.Contains("\"projectContextLoaded\": true", preflight.PayloadJson);
        Assert.Contains("\"repositoryRoot\"", preflight.PayloadJson);
        Assert.Contains("\"sources\"", preflight.PayloadJson);
        var plan = await store.GetPlanAsync(thread.Id, run.Id, CancellationToken.None);
        Assert.Equal("Load project context", plan[0].Title);
    }

    [Fact]
    public async Task ContinueRunAsync_auto_plans_guards_and_executes_datc64_translation()
    {
        var workspace = CreateWorkspace();
        var store = new AgentStore(workspace);
        await store.SaveSettingsAsync(Settings(workspace) with { OodlePath = await CreateOodleAsync(workspace) }, CancellationToken.None);
        await SaveProfileAndIndexedResourceAsync(workspace, "profile-1", "data/balance/traditional chinese/activeskills.datc64");
        var runner = new QueueRunner(
            """
            ```json
            {
              "status": "ready",
              "requestedTaskKind": "auto",
              "resolvedTaskKind": "datc64-translation",
              "profileId": "profile-1",
              "resourcePath": "data/balance/traditional chinese/activeskills.datc64",
              "summary": "Translate selected DATC64 table.",
              "userConstraints": ["only changed simplified source cells"],
              "steps": [],
              "requiredApprovals": ["overlay_draft"],
              "warnings": [],
              "questions": [],
              "missingCapability": null
            }
            ```
            """,
            """
            ```json
            {
              "taskKind": "datc64-translation",
              "profileId": "profile-1",
              "resourcePath": "data/balance/traditional chinese/activeskills.datc64",
              "candidates": [
                {
                  "locator": "row:1;column:3;name:text_3 @12",
                  "rowIndex": 0,
                  "columnIndex": 3,
                  "sourceText": "NoMana",
                  "translatedText": "法力不足",
                  "confidence": 0.86,
                  "notes": "test"
                }
              ]
            }
            ```
            """);
        var orchestrator = CreateOrchestrator(store, workspace, runner);
        var thread = await store.SaveNewThreadAsync("profile-1", "Task", "Goal", "auto", CancellationToken.None);

        var shell = await orchestrator.StartAutoRunShellAsync(thread.Id, "profile-1", "重新翻译刚才的表", "data/balance/traditional chinese/activeskills.datc64", null, CancellationToken.None);
        var run = await orchestrator.ContinueRunAsync(shell.Id, CancellationToken.None);

        Assert.Equal(AgentRunStatus.WaitingForApproval, run.Status);
        Assert.Equal("auto", run.RequestedTaskKind);
        Assert.Equal("auto", run.TaskKind);
        Assert.Equal("datc64-translation", run.ResolvedTaskKind);
        var events = await store.ListEventsAsync(thread.Id, run.Id, 0, CancellationToken.None);
        Assert.Contains(events, x => x.Message.Contains("Planner completed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(events, x => x.Message.Contains("Plan guard passed", StringComparison.OrdinalIgnoreCase));
        Assert.Single(await store.ListApprovalsAsync(thread.Id, run.Id, CancellationToken.None));
        Assert.Equal(2, runner.CallCount);
        Assert.Contains("Planner-approved task plan", runner.Prompts[1]);
    }

    [Fact]
    public async Task ContinueRunAsync_auto_waits_for_input_when_planner_needs_clarification()
    {
        var workspace = CreateWorkspace();
        var store = new AgentStore(workspace);
        await store.SaveSettingsAsync(Settings(workspace), CancellationToken.None);
        var runner = new QueueRunner("""
            ```json
            {
              "status": "needs_clarification",
              "requestedTaskKind": "auto",
              "resolvedTaskKind": null,
              "profileId": "profile-1",
              "resourcePath": null,
              "summary": "Need a resource.",
              "userConstraints": [],
              "steps": [],
              "requiredApprovals": [],
              "warnings": [],
              "questions": ["请告诉我要翻译哪个资源路径，或先在资源列表中选中它。"],
              "missingCapability": null
            }
            ```
            """);
        var orchestrator = CreateOrchestrator(store, workspace, runner);
        var thread = await store.SaveNewThreadAsync("profile-1", "Task", "Goal", "auto", CancellationToken.None);

        var shell = await orchestrator.StartAutoRunShellAsync(thread.Id, "profile-1", "翻译这个表", null, null, CancellationToken.None);
        var run = await orchestrator.ContinueRunAsync(shell.Id, CancellationToken.None);

        Assert.Equal(AgentRunStatus.WaitingForInput, run.Status);
        Assert.Equal(1, runner.CallCount);
        Assert.Empty(await store.ListApprovalsAsync(thread.Id, run.Id, CancellationToken.None));
        var events = await store.ListEventsAsync(thread.Id, run.Id, 0, CancellationToken.None);
        Assert.Contains(events, x => x.Message.Contains("请告诉我要翻译哪个资源路径", StringComparison.Ordinal));
    }

    private static AgentSettingsDto Settings(string workspace)
    {
        return new AgentSettingsDto("codex", null, null, "workspace-write", "poe-studio", workspace, "manual");
    }

    private static AgentOrchestrator CreateOrchestrator(
        AgentStore store,
        string repositoryRoot,
        ICodexProcessRunner runner)
    {
        return new AgentOrchestrator(
            store,
            new AgentPromptBuilder(),
            new AgentPlannerPromptBuilder(),
            new AgentTaskPlanParser(),
            new AgentPlanGuardService(
                new PoeStudio.Storage.Profiles.ProfileStore(repositoryRoot),
                new PoeStudio.Storage.Resources.ResourceIndexStore(repositoryRoot),
                new PoeStudio.Storage.Overlay.OverlayStore(repositoryRoot)),
            new Datc64TranslationDraftParser(),
            runner,
            new AgentProjectContextService(new AgentRepositoryRootResolver(repositoryRoot)));
    }

    private static async Task<string> CreateOodleAsync(string workspace)
    {
        var path = Path.Combine(workspace, "oo2core.dll");
        await File.WriteAllBytesAsync(path, [], CancellationToken.None);
        return path;
    }

    private static async Task SaveProfileAndIndexedResourceAsync(string root, string profileId, string resourcePath)
    {
        var now = DateTimeOffset.UtcNow;
        await new PoeStudio.Storage.Profiles.ProfileStore(root).SaveAsync(
            new ClientProfileDto(
                profileId,
                "Target",
                ClientPlatform.Official,
                ClientEntryKind.Ggpk,
                root,
                Path.Combine(root, "Content.ggpk"),
                null,
                null,
                OodleStatus.Found,
                "fingerprint",
                now,
                now),
            CancellationToken.None);
        await new PoeStudio.Storage.Resources.ResourceIndexStore(root).SaveAsync(
            profileId,
            [
                new ResourceSummaryDto(
                    "resource-1",
                    profileId,
                    resourcePath,
                    resourcePath,
                    ".datc64",
                    ResourceKind.Table,
                    10,
                    Path.Combine(root, "base.datc64"),
                    ResourceSourceLayer.Base,
                    now)
            ],
            [],
            CancellationToken.None);
    }

    private static TemporaryDirectory CreateRepositoryRoot()
    {
        var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Root, "PoeStudio.sln"), string.Empty);
        var agentDocs = Directory.CreateDirectory(Path.Combine(directory.Root, "docs", "agent"));
        File.WriteAllText(Path.Combine(agentDocs.FullName, "poe-studio-project-workflows.md"), "# Workflow\n\n## 7. 原始层、草稿层与当前工作态\ncurrent working state overlay mcp approval");
        File.WriteAllText(Path.Combine(agentDocs.FullName, "poe-studio-agent-context.md"), "# Agent Context\n\n## 1. Agent 总目标\nAgent context.");
        File.WriteAllText(Path.Combine(directory.Root, "docs", "ai-project-memory.md"), "# Memory\n\n## 项目定位\nProject memory.");
        return directory;
    }

    private static string CreateWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "poe-studio-agent-orchestrator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "poe-studio-agent-orchestrator-repo-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
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

        public async Task<CodexRunResult> RunAsync(
            AgentSettingsDto settings,
            string prompt,
            Func<CodexParsedEvent, Task>? onEvent,
            CancellationToken cancellationToken)
        {
            var events = new[]
            {
                new CodexParsedEvent("{}", CodexParsedEventType.McpToolCall, "poe_get_workspace completed", "{}", false, true, "poe_get_workspace"),
                new CodexParsedEvent("{}", CodexParsedEventType.AgentMessage, _message, "{}", true, false, null)
            };
            if (onEvent is not null)
            {
                foreach (var parsedEvent in events)
                {
                    await onEvent(parsedEvent);
                }
            }

            return new CodexRunResult(
                _exitCode,
                _exitCode != 0,
                false,
                events,
                _stderr);
        }
    }

    private sealed class RecordingRunner : ICodexProcessRunner
    {
        private readonly string _message;

        public RecordingRunner(string message)
        {
            _message = message;
        }

        public List<string> Prompts { get; } = [];
        public List<string?> OodlePaths { get; } = [];

        public async Task<CodexRunResult> RunAsync(
            AgentSettingsDto settings,
            string prompt,
            Func<CodexParsedEvent, Task>? onEvent,
            CancellationToken cancellationToken)
        {
            Prompts.Add(prompt);
            OodlePaths.Add(settings.OodlePath);
            var events = new[]
            {
                new CodexParsedEvent("{}", CodexParsedEventType.AgentMessage, _message, "{}", true, false, null)
            };
            if (onEvent is not null)
            {
                foreach (var parsedEvent in events)
                {
                    await onEvent(parsedEvent);
                }
            }

            return new CodexRunResult(0, false, false, events, null);
        }
    }

    private sealed class QueueRunner : ICodexProcessRunner
    {
        private readonly Queue<string> _messages;

        public QueueRunner(params string[] messages)
        {
            _messages = new Queue<string>(messages);
        }

        public int CallCount { get; private set; }
        public List<string> Prompts { get; } = [];

        public async Task<CodexRunResult> RunAsync(
            AgentSettingsDto settings,
            string prompt,
            Func<CodexParsedEvent, Task>? onEvent,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Prompts.Add(prompt);
            var message = _messages.Dequeue();
            var events = new[]
            {
                new CodexParsedEvent("{}", CodexParsedEventType.AgentMessage, message, "{}", true, false, null)
            };
            if (onEvent is not null)
            {
                foreach (var parsedEvent in events)
                {
                    await onEvent(parsedEvent);
                }
            }

            return new CodexRunResult(0, false, false, events, null);
        }
    }
}
