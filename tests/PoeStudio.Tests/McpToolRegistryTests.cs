using System.Text.Json;
using PoeStudio.Mcp;

namespace PoeStudio.Tests;

public sealed class McpToolRegistryTests
{
    private static readonly string[] ReadOnlyToolNames =
    [
        "poe_get_project_overview",
        "poe_get_project_knowledge",
        "poe_get_workspace",
        "poe_list_profiles",
        "poe_get_profile",
        "poe_get_index_status",
        "poe_search_resources",
        "poe_read_resource",
        "poe_datc64_extract_translatable_cells",
        "poe_get_current_view_context",
        "poe_find_current_table_untranslated_cells",
        "poe_get_agent_run_trace",
        "poe_get_agent_recent_logs"
    ];

    private static readonly string[] WriteToolNames =
    [
        "poe_write_overlay_text",
        "poe_write_overlay_binary",
        "poe_list_overlays",
        "poe_revert_overlay"
    ];

    private static readonly string[] RequiredToolNames = [.. ReadOnlyToolNames, .. WriteToolNames];

    [Fact]
    public void Tools_list_returns_all_required_tools_with_descriptions_and_input_schemas()
    {
        var registry = McpToolRegistry.CreateDefault();

        var tools = registry.ListTools().ToArray();

        Assert.Equal(RequiredToolNames, tools.Select(tool => tool.Name));
        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
            Assert.Equal(JsonValueKind.Object, tool.InputSchema.ValueKind);
            Assert.True(tool.InputSchema.TryGetProperty("type", out var type));
            Assert.Equal("object", type.GetString());
            Assert.True(tool.InputSchema.TryGetProperty("required", out var required));
            Assert.Equal(JsonValueKind.Array, required.ValueKind);
            Assert.True(required.GetArrayLength() == 0 || tool.InputSchema.TryGetProperty("properties", out _));
        }
    }

    [Fact]
    public void Tools_list_marks_stage1_tools_as_read_only()
    {
        var registry = McpToolRegistry.CreateDefault();

        var tools = registry.ListTools().ToArray();

        Assert.All(tools.Where(t => ReadOnlyToolNames.Contains(t.Name)), tool =>
        {
            Assert.NotNull(tool.Annotations);
            Assert.True(tool.Annotations!.ReadOnlyHint);
            Assert.False(tool.Annotations.OpenWorldHint);
        });
    }

    [Fact]
    public void Tools_list_marks_write_tools_as_not_read_only()
    {
        var registry = McpToolRegistry.CreateDefault();

        var tools = registry.ListTools().ToArray();

        Assert.All(tools.Where(t => WriteToolNames.Contains(t.Name)), tool =>
        {
            Assert.NotNull(tool.Annotations);
            Assert.False(tool.Annotations!.ReadOnlyHint);
            Assert.False(tool.Annotations.OpenWorldHint);
        });
    }

    [Fact]
    public void Tools_list_includes_current_view_tools_as_read_only()
    {
        var registry = McpToolRegistry.CreateDefault();

        var tools = registry.ListTools();

        var getContext = Assert.Single(tools, tool => tool.Name == "poe_get_current_view_context");
        var findCells = Assert.Single(tools, tool => tool.Name == "poe_find_current_table_untranslated_cells");
        Assert.Contains("current UI", getContext.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing translations", findCells.Description, StringComparison.OrdinalIgnoreCase);
        Assert.True(getContext.Annotations?.ReadOnlyHint);
        Assert.True(findCells.Annotations?.ReadOnlyHint);
    }

    [Fact]
    public void Tools_list_includes_project_knowledge_as_read_only()
    {
        var registry = McpToolRegistry.CreateDefault();

        var tools = registry.ListTools();

        var knowledge = Assert.Single(tools, tool => tool.Name == "poe_get_project_knowledge");
        Assert.Contains("project knowledge", knowledge.Description, StringComparison.OrdinalIgnoreCase);
        Assert.True(knowledge.Annotations?.ReadOnlyHint);
    }

    [Fact]
    public async Task Unknown_tool_call_returns_is_error_true()
    {
        var registry = McpToolRegistry.CreateDefault();

        var result = await registry.CallToolAsync("poe_missing_tool", JsonDocument.Parse("{}").RootElement, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Unknown tool", result.Content.Single().Text);
    }
}
