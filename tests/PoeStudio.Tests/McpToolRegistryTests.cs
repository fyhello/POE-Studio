using System.Text.Json;
using PoeStudio.Mcp;

namespace PoeStudio.Tests;

public sealed class McpToolRegistryTests
{
    private static readonly string[] RequiredToolNames =
    [
        "poe_get_workspace",
        "poe_list_profiles",
        "poe_get_profile",
        "poe_get_index_status",
        "poe_search_resources",
        "poe_read_resource",
        "poe_datc64_extract_translatable_cells"
    ];

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
        }
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
