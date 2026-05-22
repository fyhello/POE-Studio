using PoeStudio.Contracts;
using PoeStudio.Core.Agent;

namespace PoeStudio.Tests;

public sealed class AgentPromptBuilderTests
{
    private readonly AgentPromptBuilder _builder = new();

    [Fact]
    public void Build_includes_question_goal_mcp_server_read_only_tools_and_write_boundary()
    {
        var prompt = Build("question", "How does indexing work?", resourcePath: null);

        Assert.Contains("How does indexing work?", prompt);
        Assert.Contains("poe-studio", prompt);
        Assert.Contains("poe_get_workspace", prompt);
        Assert.Contains("read-only", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not write overlay", prompt);
    }

    [Fact]
    public void Build_includes_read_only_analysis_context_and_json_result_contract()
    {
        var prompt = Build("read-only-analysis", "Inspect this resource", "metadata/items.datc64");

        Assert.Contains("profile-1", prompt);
        Assert.Contains("metadata/items.datc64", prompt);
        Assert.Contains("Check the resource index status", prompt);
        Assert.Contains("agentReadOnlyAnalysisResult", prompt);
        Assert.Contains("```json", prompt);
    }

    [Fact]
    public void Build_includes_datc64_schema_and_forbids_direct_overlay_writes()
    {
        var prompt = Build("datc64-translation", "Translate safe strings", "metadata/items.datc64");

        Assert.Contains("poe_datc64_extract_translatable_cells", prompt);
        Assert.Contains("datc64TranslationProposal", prompt);
        Assert.Contains("\"translatedText\"", prompt);
        Assert.Contains("must not write overlay", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_includes_conversation_history()
    {
        var now = DateTimeOffset.Parse("2026-05-22T00:00:00Z");
        var settings = Settings();
        var thread = Thread("question");
        var capability = AgentCapabilities.GetRequired("question");
        var messages = new[]
        {
            new AgentMessageDto("message-1", thread.Id, AgentMessageRole.User, "Previous user ask", null, now),
            new AgentMessageDto("message-2", thread.Id, AgentMessageRole.Assistant, "Previous answer", null, now.AddSeconds(1))
        };

        var prompt = _builder.Build(settings, capability, thread, messages, "Follow up", null);

        Assert.Contains("Conversation history", prompt);
        Assert.Contains("Previous user ask", prompt);
        Assert.Contains("Previous answer", prompt);
    }

    private string Build(string taskKind, string goal, string? resourcePath)
    {
        return _builder.Build(
            Settings(),
            AgentCapabilities.GetRequired(taskKind),
            Thread(taskKind),
            [],
            goal,
            resourcePath);
    }

    private static AgentSettingsDto Settings()
    {
        return new AgentSettingsDto("codex", null, null, "workspace-write", "poe-studio", "C:/repo", "manual");
    }

    private static AgentThreadDto Thread(string taskKind)
    {
        var now = DateTimeOffset.Parse("2026-05-22T00:00:00Z");
        return new AgentThreadDto(
            "thread-1",
            "profile-1",
            "Agent task",
            "Original goal",
            taskKind,
            AgentThreadStatus.Active,
            now,
            now);
    }
}
