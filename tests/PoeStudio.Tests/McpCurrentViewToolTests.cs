using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Oodle;
using PoeStudio.Core.Native;
using PoeStudio.Mcp;
using PoeStudio.Storage.Agent;

namespace PoeStudio.Tests;

public sealed class McpCurrentViewToolTests
{
    [Fact]
    public async Task Find_current_table_non_simplified_chinese_cells_flags_traditional_target_text()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-mcp-current-view-simplified-" + Guid.NewGuid().ToString("N"));
        var store = new AgentCurrentViewStore(root);
        var snapshot = await store.SaveAsync(new AgentCurrentViewRequestDto(
            "tableComparison",
            new AgentCurrentTableViewDto(
                "target",
                "data/balance/traditional chinese/activeskills.datc64",
                "source",
                "data/balance/simplified chinese/activeskills.datc64",
                "target",
                "data/balance/traditional chinese/activeskills.datc64",
                "datc64-auto",
                16,
                1,
                ["Id", "Description @16", "ShortDescription @290"],
                [1, 2],
                [
                    new AgentCurrentTableRowDto(16,
                    [
                        "molten_crash",
                        "[Shapeshift|變形]成一名燃燒的兇獸並躍至空中，隨後猛地向大地砸擊，[Slam|重擊]兩次並創造[MoltenFissure|熔岩裂縫]。",
                        "跃向目标区域[Slam|猛击]两次并生成[MoltenFissure|熔岩裂缝]。"
                    ])
                ],
                [
                    new AgentCurrentTableRowDto(16,
                    [
                        "molten_crash",
                        "[Shapeshift|变形]成一名燃烧的凶兽并跃至空中，随后猛地向大地砸击，[Slam|重击]两次并创造[MoltenFissure|熔岩裂缝]。",
                        "跃向目标区域[Slam|猛击]两次并生成[MoltenFissure|熔岩裂缝]。"
                    ])
                ],
                "简体路径")), CancellationToken.None);

        var registry = McpToolRegistry.CreateDefault(
            new PoeWorkspaceResolution(true, root, "test", null),
            new NativeBundleResourceContentResolver(new MissingOodleCodec()));

        var result = await registry.CallToolAsync(
            "poe_find_current_table_non_simplified_chinese_cells",
            JsonDocument.Parse($$"""{"contextId":"{{snapshot.ContextId}}","limit":10}""").RootElement,
            CancellationToken.None);

        Assert.False(result.IsError);
        var text = Assert.Single(result.Content).Text;
        Assert.Contains("\"rowNumber\":16", text);
        Assert.Contains("Description @16", text);
        Assert.Contains("target_contains_traditional_chinese", text);
        Assert.Contains("變形", text);
        Assert.DoesNotContain("ShortDescription @290", text);
    }

    [Fact]
    public async Task Find_current_table_untranslated_cells_uses_snapshot_without_oodle()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-mcp-current-view-" + Guid.NewGuid().ToString("N"));
        var store = new AgentCurrentViewStore(root);
        var snapshot = await store.SaveAsync(new AgentCurrentViewRequestDto(
            "tableComparison",
            new AgentCurrentTableViewDto(
                "target",
                "data/balance/traditional chinese/activeskills.datc64",
                "source",
                "data/balance/simplified chinese/activeskills.datc64",
                "target",
                "data/balance/traditional chinese/activeskills.datc64",
                "datc64-auto",
                3,
                3,
                ["Id", "Name"],
                [1],
                [
                    new AgentCurrentTableRowDto(1, ["same", "火球"]),
                    new AgentCurrentTableRowDto(2, ["english", "Lightning Warp"]),
                    new AgentCurrentTableRowDto(3, ["empty", ""])
                ],
                [
                    new AgentCurrentTableRowDto(1, ["same", "火球"]),
                    new AgentCurrentTableRowDto(2, ["english", "闪电传送"]),
                    new AgentCurrentTableRowDto(3, ["empty", "冰霜新星"])
                ],
                "简体路径")), CancellationToken.None);

        var registry = McpToolRegistry.CreateDefault(
            new PoeWorkspaceResolution(true, root, "test", null),
            new NativeBundleResourceContentResolver(new MissingOodleCodec()));

        var result = await registry.CallToolAsync(
            "poe_find_current_table_untranslated_cells",
            JsonDocument.Parse($$"""{"contextId":"{{snapshot.ContextId}}","limit":10}""").RootElement,
            CancellationToken.None);

        Assert.False(result.IsError);
        var text = Assert.Single(result.Content).Text;
        Assert.Contains("Lightning Warp", text);
        Assert.Contains("冰霜新星", text);
        Assert.DoesNotContain("native_oodle_missing", text);
    }
}
