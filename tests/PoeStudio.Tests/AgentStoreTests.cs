using System.Text.Json;
using PoeStudio.Contracts;

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
            "manual");
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
        Assert.Contains("\"taskKind\"", json);
    }
}
