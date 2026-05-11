using PoeStudio.Contracts;
using PoeStudio.Storage.Overlay;

namespace PoeStudio.Tests;

public sealed class OverlayStoreTests
{
    [Fact]
    public async Task SaveTextAsync_writes_overlay_and_list_returns_entry()
    {
        var store = new OverlayStore(Path.Combine(Path.GetTempPath(), "poe-studio-overlay-tests", Guid.NewGuid().ToString("N")));
        var profileId = Guid.NewGuid().ToString("N");

        var saved = await store.SaveTextAsync(new SaveTextOverlayRequest(profileId, "metadata/items/amulet.ot", "translated"), CancellationToken.None);
        var list = await store.ListAsync(profileId, CancellationToken.None);

        Assert.True(File.Exists(saved.OverlayPath));
        Assert.Equal("metadata/items/amulet.ot", saved.VirtualPath);
        var item = Assert.Single(list.Items);
        Assert.Equal("metadata/items/amulet.ot", item.VirtualPath);
        Assert.Equal(saved.OverlayHash, item.OverlayHash);
    }

    [Fact]
    public async Task SaveBytesAsync_writes_binary_overlay_and_list_returns_entry()
    {
        var store = new OverlayStore(Path.Combine(Path.GetTempPath(), "poe-studio-overlay-tests", Guid.NewGuid().ToString("N")));
        var profileId = Guid.NewGuid().ToString("N");

        var saved = await store.SaveBytesAsync(profileId, "art/icons/item.dds", [1, 2, 3, 4], BasePhysicalPath: null, HasBasePhysicalPath: false, CancellationToken.None);
        var list = await store.ListAsync(profileId, CancellationToken.None);

        Assert.True(File.Exists(saved.OverlayPath));
        Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(saved.OverlayPath));
        var item = Assert.Single(list.Items);
        Assert.Equal("art/icons/item.dds", item.VirtualPath);
        Assert.Equal(4, item.OverlaySize);
    }

    [Fact]
    public async Task SaveTextAsync_rejects_path_traversal()
    {
        var store = new OverlayStore(Path.Combine(Path.GetTempPath(), "poe-studio-overlay-tests", Guid.NewGuid().ToString("N")));

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveTextAsync(new SaveTextOverlayRequest("profile", "../secret.txt", "bad"), CancellationToken.None));
    }

    [Fact]
    public async Task DiffAsync_reports_base_and_overlay_metadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-overlay-tests", Guid.NewGuid().ToString("N"));
        var baseFile = Path.Combine(root, "base.txt");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(baseFile, "base text");
        var store = new OverlayStore(root);
        var profileId = Guid.NewGuid().ToString("N");
        await store.SaveTextAsync(new SaveTextOverlayRequest(profileId, "text/base.txt", "overlay text", baseFile, HasBasePhysicalPath: true), CancellationToken.None);

        var diff = await store.DiffAsync(new OverlayDiffRequest(profileId, "text/base.txt"), CancellationToken.None);

        Assert.True(diff.Exists);
        Assert.True(diff.TextChanged);
        Assert.NotEqual(diff.BaseHash, diff.OverlayHash);
        Assert.Equal(9, diff.BaseSize);
        Assert.Equal(15, diff.OverlaySize);
    }

    [Fact]
    public async Task RevertAsync_removes_overlay_file_and_manifest_entry()
    {
        var store = new OverlayStore(Path.Combine(Path.GetTempPath(), "poe-studio-overlay-tests", Guid.NewGuid().ToString("N")));
        var profileId = Guid.NewGuid().ToString("N");
        var saved = await store.SaveTextAsync(new SaveTextOverlayRequest(profileId, "text/base.txt", "overlay text"), CancellationToken.None);

        var reverted = await store.RevertAsync(new RevertOverlayRequest(profileId, "text/base.txt"), CancellationToken.None);
        var list = await store.ListAsync(profileId, CancellationToken.None);

        Assert.True(reverted.Removed);
        Assert.False(File.Exists(saved.OverlayPath));
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task AuditAsync_lists_overlay_save_and_revert_events()
    {
        var store = new OverlayStore(Path.Combine(Path.GetTempPath(), "poe-studio-overlay-tests", Guid.NewGuid().ToString("N")));
        var profileId = Guid.NewGuid().ToString("N");

        await store.SaveTextAsync(new SaveTextOverlayRequest(profileId, "text/base.txt", "overlay text"), CancellationToken.None);
        await store.RevertAsync(new RevertOverlayRequest(profileId, "text/base.txt"), CancellationToken.None);
        var audit = await store.AuditAsync(new OverlayAuditRequest(profileId), CancellationToken.None);

        Assert.Equal(2, audit.Total);
        Assert.Equal("revert", audit.Items[0].Action);
        Assert.Equal("save", audit.Items[1].Action);
        Assert.All(audit.Items, item => Assert.Equal("text/base.txt", item.VirtualPath));
    }
}
