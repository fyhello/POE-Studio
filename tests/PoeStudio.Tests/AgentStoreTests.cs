using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Storage.Agent;

namespace PoeStudio.Tests;

public sealed class AgentStoreTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Agent_dtos_serialize_with_stage2_contract_fields()
    {
        var now = DateTimeOffset.Parse("2026-05-22T00:00:00Z");
        var settings = new AgentSettingsDto(
            "codex",
            "gpt-5.4",
            "default",
            "workspace-write",
            "poe-studio",
            "C:/workspace",
            "manual",
            "C:/Game/oo2core.dll");
        var thread = new AgentThreadDto(
            "thread-1",
            "profile-1",
            "Translate UI text",
            "Find translation candidates",
            "datc64-translation",
            AgentThreadStatus.Active,
            now,
            now);
        var message = new AgentMessageDto(
            "message-1",
            thread.Id,
            AgentMessageRole.User,
            "Please translate safe cells.",
            null,
            now);
        var run = new AgentRunDto(
            "run-1",
            thread.Id,
            thread.ProfileId,
            thread.Goal,
            thread.TaskKind,
            AgentRunStatus.Queued,
            0,
            "Queued",
            now,
            now,
            0,
            null,
            null,
            null);
        var evt = new AgentEventDto(
            "event-1",
            run.Id,
            1,
            AgentEventType.RunCreated,
            "Run created",
            null,
            now);
        var approval = new AgentApprovalDto(
            "approval-1",
            run.Id,
            thread.ProfileId,
            "datc64-translation",
            AgentApprovalStatus.Pending,
            "One translation candidate",
            "{}",
            now,
            now,
            null);
        var capability = new AgentCapabilityDto(
            "datc64-translation",
            "DATC64 translation",
            AgentCapabilityKind.WriteWithApproval,
            new[] { "poe_datc64_extract_translatable_cells" },
            true,
            "datc64TranslationProposal");

        var json = JsonSerializer.Serialize(new
        {
            settings,
            thread,
            message,
            run,
            evt,
            approval,
            capability
        }, JsonOptions);

        Assert.Contains("\"threadId\"", json);
        Assert.Contains("\"approvalMode\"", json);
        Assert.Contains("\"oodlePath\"", json);
        Assert.Contains("\"taskKind\"", json);
    }

    [Fact]
    public async Task Settings_roundtrip_after_store_restarts()
    {
        var workspace = CreateWorkspace();
        var store = new AgentStore(workspace);
        var settings = new AgentSettingsDto(
            "codex",
            "gpt-5.4",
            "agent-profile",
            "workspace-write",
            "poe-studio",
            workspace,
            "manual");

        await store.SaveSettingsAsync(settings, CancellationToken.None);
        var restarted = new AgentStore(workspace);

        Assert.Equal(settings, await restarted.GetSettingsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Thread_messages_run_events_plan_and_approval_roundtrip_after_store_restarts()
    {
        var workspace = CreateWorkspace();
        var now = DateTimeOffset.Parse("2026-05-22T00:00:00Z");
        var store = new AgentStore(workspace);
        var thread = new AgentThreadDto(
            "thread-1",
            "profile-1",
            "Translate DATC64",
            "Find safe translation candidates",
            "datc64-translation",
            AgentThreadStatus.Active,
            now,
            now);
        var userMessage = new AgentMessageDto(
            "message-1",
            thread.Id,
            AgentMessageRole.User,
            "Translate user-facing cells.",
            null,
            now);
        var assistantMessage = new AgentMessageDto(
            "message-2",
            thread.Id,
            AgentMessageRole.Assistant,
            "I found candidates.",
            "{}",
            now.AddSeconds(1));
        var run = new AgentRunDto(
            "run-1",
            thread.Id,
            thread.ProfileId,
            thread.Goal,
            thread.TaskKind,
            AgentRunStatus.Running,
            25,
            "Inspecting resource",
            now,
            now,
            0,
            null,
            null,
            null);
        var plan = new[]
        {
            new AgentPlanStepDto("step-1", run.Id, 1, "Read resource", "completed", "poe_read_resource"),
            new AgentPlanStepDto("step-2", run.Id, 2, "Propose translations", "pending", null)
        };
        var approval = new AgentApprovalDto(
            "approval-1",
            run.Id,
            thread.ProfileId,
            "datc64-translation",
            AgentApprovalStatus.Pending,
            "One candidate",
            "{\"candidates\":[]}",
            now,
            now,
            null);

        await store.SaveThreadAsync(thread, CancellationToken.None);
        await store.AppendMessageAsync(userMessage, CancellationToken.None);
        await store.AppendMessageAsync(assistantMessage, CancellationToken.None);
        await store.SaveRunAsync(run, CancellationToken.None);
        var firstEvent = await store.AppendEventAsync(run.ThreadId, run.Id, AgentEventType.RunCreated, "Run created", null, CancellationToken.None);
        var secondEvent = await store.AppendEventAsync(run.ThreadId, run.Id, AgentEventType.PlanUpdated, "Plan updated", "{}", CancellationToken.None);
        await store.SavePlanAsync(run.ThreadId, run.Id, plan, CancellationToken.None);
        await store.SaveApprovalsAsync(run.ThreadId, run.Id, [approval], CancellationToken.None);

        var restarted = new AgentStore(workspace);
        var storedThread = await restarted.GetThreadAsync(thread.Id, CancellationToken.None);
        var messages = await restarted.ListMessagesAsync(thread.Id, CancellationToken.None);
        var storedRun = await restarted.GetRunAsync(thread.Id, run.Id, CancellationToken.None);
        var events = await restarted.ListEventsAsync(thread.Id, run.Id, afterSequence: 0, CancellationToken.None);
        var storedPlan = await restarted.GetPlanAsync(thread.Id, run.Id, CancellationToken.None);
        var approvals = await restarted.ListApprovalsAsync(thread.Id, run.Id, CancellationToken.None);

        Assert.Equal(thread, storedThread);
        Assert.Equal(new[] { userMessage, assistantMessage }, messages);
        Assert.Equal(run, storedRun);
        Assert.Equal(1, firstEvent.Sequence);
        Assert.Equal(2, secondEvent.Sequence);
        Assert.Equal(new long[] { 1, 2 }, events.Select(x => x.Sequence).ToArray());
        Assert.Equal(plan, storedPlan);
        Assert.Single(approvals);
        Assert.Equal(AgentApprovalStatus.Pending, approvals[0].Status);
    }

    [Fact]
    public async Task Events_can_be_listed_while_new_events_are_appended()
    {
        var workspace = CreateWorkspace();
        var now = DateTimeOffset.Parse("2026-05-22T00:00:00Z");
        var store = new AgentStore(workspace);
        var thread = new AgentThreadDto(
            "thread-1",
            "profile-1",
            "Question",
            "Goal",
            "question",
            AgentThreadStatus.Active,
            now,
            now);
        var run = new AgentRunDto(
            "run-1",
            thread.Id,
            thread.ProfileId,
            thread.Goal,
            thread.TaskKind,
            AgentRunStatus.Running,
            5,
            "Running",
            now,
            now,
            0,
            null,
            null,
            null);
        await store.SaveThreadAsync(thread, CancellationToken.None);
        await store.SaveRunAsync(run, CancellationToken.None);

        var writer = Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++)
            {
                await store.AppendEventAsync(thread.Id, run.Id, AgentEventType.CodexStdout, $"event {i}", null, CancellationToken.None);
            }
        });
        var reader = Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++)
            {
                _ = await store.ListEventsAsync(thread.Id, run.Id, 0, CancellationToken.None);
            }
        });

        await Task.WhenAll(writer, reader);
        var events = await store.ListEventsAsync(thread.Id, run.Id, 0, CancellationToken.None);

        Assert.Equal(50, events.Count);
        Assert.Equal(Enumerable.Range(1, 50).Select(x => (long)x), events.Select(x => x.Sequence));
    }

    [Fact]
    public async Task Approve_pending_approval_once()
    {
        var workspace = CreateWorkspace();
        var now = DateTimeOffset.Parse("2026-05-22T00:00:00Z");
        var store = new AgentStore(workspace);
        var thread = new AgentThreadDto(
            "thread-1",
            "profile-1",
            "Translate DATC64",
            "Find safe translation candidates",
            "datc64-translation",
            AgentThreadStatus.Active,
            now,
            now);
        var run = new AgentRunDto(
            "run-1",
            thread.Id,
            thread.ProfileId,
            thread.Goal,
            thread.TaskKind,
            AgentRunStatus.WaitingForApproval,
            90,
            "Waiting for approval",
            now,
            now,
            0,
            null,
            null,
            null);
        var approval = new AgentApprovalDto(
            "approval-1",
            run.Id,
            thread.ProfileId,
            "datc64-translation",
            AgentApprovalStatus.Pending,
            "One candidate",
            "{\"candidates\":[]}",
            now,
            now,
            null);
        await store.SaveThreadAsync(thread, CancellationToken.None);
        await store.SaveRunAsync(run, CancellationToken.None);
        await store.SaveApprovalsAsync(thread.Id, run.Id, [approval], CancellationToken.None);

        var approved = await store.TryUpdateApprovalStatusAsync(
            thread.Id,
            run.Id,
            approval.Id,
            AgentApprovalStatus.Pending,
            AgentApprovalStatus.Approved,
            appliedOverlayPath: null,
            CancellationToken.None);
        var repeated = await store.TryUpdateApprovalStatusAsync(
            thread.Id,
            run.Id,
            approval.Id,
            AgentApprovalStatus.Pending,
            AgentApprovalStatus.Approved,
            appliedOverlayPath: null,
            CancellationToken.None);

        Assert.True(approved);
        Assert.False(repeated);
        var approvals = await new AgentStore(workspace).ListApprovalsAsync(thread.Id, run.Id, CancellationToken.None);
        Assert.Equal(AgentApprovalStatus.Approved, approvals[0].Status);
        Assert.True(approvals[0].UpdatedAt > approvals[0].CreatedAt);
    }

    private static string CreateWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "poe-studio-agent-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
