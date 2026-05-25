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
