using PoeStudio.Contracts;
using PoeStudio.Storage.Agent;

namespace PoeStudio.Tests;

public sealed class AgentCurrentViewStoreTests
{
    [Fact]
    public async Task SaveAsync_persists_snapshot_and_LoadAsync_reads_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-current-view-" + Guid.NewGuid().ToString("N"));
        var store = new AgentCurrentViewStore(root);
        var request = new AgentCurrentViewRequestDto(
            "tableComparison",
            new AgentCurrentTableViewDto(
                "target-profile",
                "data/balance/traditional chinese/activeskills.datc64",
                "source-profile",
                "data/balance/simplified chinese/activeskills.datc64",
                "target-profile",
                "data/balance/traditional chinese/activeskills.datc64",
                "datc64-auto",
                RowCount: 1,
                PreviewRowCount: 1,
                Columns: ["Id", "Name"],
                EditableColumnIndexes: [1],
                TargetRows: [new AgentCurrentTableRowDto(1, ["skill", "Fireball"])],
                SourceRows: [new AgentCurrentTableRowDto(1, ["skill", "火球"])],
                ReferenceMatchMode: "简体路径"));

        var snapshot = await store.SaveAsync(request, CancellationToken.None);
        var loaded = await store.LoadAsync(snapshot.ContextId, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.ContextId, loaded.ContextId);
        Assert.Equal("tableComparison", loaded.View.Kind);
        Assert.Equal("Fireball", loaded.View.Table!.TargetRows[0].Cells[1]);
    }

    [Fact]
    public async Task LoadAsync_rejects_unsafe_context_id()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-current-view-" + Guid.NewGuid().ToString("N"));
        var store = new AgentCurrentViewStore(root);

        var loaded = await store.LoadAsync("../outside", CancellationToken.None);

        Assert.Null(loaded);
    }
}
