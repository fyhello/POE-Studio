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

    [Fact]
    public void Build_injects_bounded_project_context_before_mcp_tools()
    {
        var longSection = new string('x', 1_300);
        var projectContext = new AgentProjectContextDto(
            "2026-05-23",
            [
                new AgentProjectContextSourceDto(
                    "docs/agent/poe-studio-project-workflows.md",
                    true,
                    "abc123",
                    DateTimeOffset.Parse("2026-05-23T00:00:00Z"))
            ],
            "DATC64 tasks must inspect current working state, overlay, MCP limitations, and approval boundaries.",
            [
                new AgentProjectContextSectionDto("layering", "Current working state", "current working state overlay draft before base."),
                new AgentProjectContextSectionDto("mcp", "MCP current limits", "poe_read_resource has No useOverlay parameter."),
                new AgentProjectContextSectionDto("approval", "Approval", "Requires approval before writing overlay."),
                new AgentProjectContextSectionDto("datc64", "DATC64", longSection)
            ],
            [
                new AgentToolGuidanceDto("poe_get_project_context", "Read project context", "Returns summary only."),
                new AgentToolGuidanceDto("poe_read_resource", "Read base resource", "No useOverlay parameter.")
            ],
            [
                new AgentRiskBoundaryDto("write overlay", "high", true, "Requires approval.")
            ],
            ["unknowns: missing current UI selection"]);

        var prompt = _builder.Build(
            Settings(),
            AgentCapabilities.GetRequired("datc64-translation"),
            Thread("datc64-translation"),
            [],
            "Translate DATC64",
            "metadata/items.datc64",
            projectContext);

        Assert.Contains("Project context", prompt);
        Assert.Contains("current working state", prompt);
        Assert.Contains("overlay", prompt);
        Assert.Contains("poe_get_project_context", prompt);
        Assert.Contains("No useOverlay parameter", prompt);
        Assert.Contains("Requires approval", prompt);
        Assert.Contains("unknowns", prompt);
        Assert.True(prompt.Length < 16_000);
        Assert.DoesNotContain(longSection, prompt);
        Assert.True(prompt.IndexOf("Project context", StringComparison.Ordinal) < prompt.IndexOf("Allowed MCP tools", StringComparison.Ordinal));
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
