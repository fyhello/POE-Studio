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
    public async Task SaveTextAsync_preserves_utf16_little_endian_bom_from_base_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-overlay-tests", Guid.NewGuid().ToString("N"));
        var basePath = Path.Combine(root, "base.csd");
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(basePath, System.Text.Encoding.Unicode.GetPreamble()
            .Concat(System.Text.Encoding.Unicode.GetBytes("old text"))
            .ToArray());
        var store = new OverlayStore(root);
        var profileId = Guid.NewGuid().ToString("N");

        var saved = await store.SaveTextAsync(new SaveTextOverlayRequest(profileId, "data/statdescriptions/base.csd", "new text", basePath, true), CancellationToken.None);
        var bytes = await File.ReadAllBytesAsync(saved.OverlayPath);

        Assert.Equal(0xff, bytes[0]);
        Assert.Equal(0xfe, bytes[1]);
        Assert.Equal("new text", System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2));
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
    public async Task SyncExternalAsync_uses_files_txt_as_virtual_path_list()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-overlay-tests", Guid.NewGuid().ToString("N"));
        var store = new OverlayStore(root);
        var profileId = Guid.NewGuid().ToString("N");
        var filesRoot = Path.Combine(root, "profiles", profileId, "overlay", "files");
        var datPath = Path.Combine(filesRoot, "data", "balance", "traditional chinese", "items.datc64");
        var ignoredPath = Path.Combine(filesRoot, "data", "balance", "ignored.datc64");
        Directory.CreateDirectory(Path.GetDirectoryName(datPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(ignoredPath)!);
        await File.WriteAllBytesAsync(datPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(ignoredPath, [9, 9, 9]);
        await File.WriteAllTextAsync(
            Path.Combine(root, "profiles", profileId, "overlay", "files.txt"),
            """
            # 只同步列表里的虚拟路径
            data/balance/traditional chinese/items.datc64
            data/balance/missing.datc64
            """);

        var synced = await store.SyncExternalAsync(new OverlaySyncExternalRequest(profileId), CancellationToken.None);
        var list = await store.ListAsync(profileId, CancellationToken.None);

        Assert.Equal("files.txt", synced.Mode);
        Assert.Equal(2, synced.Discovered);
        Assert.Equal(1, synced.Imported);
        Assert.Equal(1, synced.Skipped);
        var item = Assert.Single(list.Items);
        Assert.Equal("data/balance/traditional chinese/items.datc64", item.VirtualPath);
        Assert.DoesNotContain(list.Items, entry => entry.VirtualPath.EndsWith("ignored.datc64", StringComparison.Ordinal));
        Assert.Contains(synced.Warnings, warning => warning.Contains("missing.datc64", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SyncExternalAsync_scans_overlay_files_when_files_txt_is_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-overlay-tests", Guid.NewGuid().ToString("N"));
        var store = new OverlayStore(root);
        var profileId = Guid.NewGuid().ToString("N");
        var filesRoot = Path.Combine(root, "profiles", profileId, "overlay", "files");
        var datPath = Path.Combine(filesRoot, "data", "balance", "items.datc64");
        var uiPath = Path.Combine(filesRoot, "metadata", "ui", "panel.ui");
        Directory.CreateDirectory(Path.GetDirectoryName(datPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(uiPath)!);
        await File.WriteAllBytesAsync(datPath, [1, 2, 3]);
        await File.WriteAllTextAsync(uiPath, "ui");

        var synced = await store.SyncExternalAsync(new OverlaySyncExternalRequest(profileId), CancellationToken.None);
        var list = await store.ListAsync(profileId, CancellationToken.None);

        Assert.Equal("scan", synced.Mode);
        Assert.Equal(2, synced.Discovered);
        Assert.Equal(2, synced.Imported);
        Assert.Equal(filesRoot, synced.OverlayFilesRoot);
        Assert.Equal(filesRoot, list.OverlayFilesRoot);
        Assert.Equal(["data/balance/items.datc64", "metadata/ui/panel.ui"], list.Items.Select(item => item.VirtualPath).Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [Fact]
    public async Task SyncExternalAsync_rebuilds_manifest_from_current_overlay_files_only()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-overlay-tests", Guid.NewGuid().ToString("N"));
        var store = new OverlayStore(root);
        var profileId = Guid.NewGuid().ToString("N");
        var filesRoot = Path.Combine(root, "profiles", profileId, "overlay", "files");
        var keptPath = Path.Combine(filesRoot, "data", "balance", "traditional chinese", "items.datc64");
        Directory.CreateDirectory(Path.GetDirectoryName(keptPath)!);
        await File.WriteAllBytesAsync(keptPath, [1, 2, 3]);
        await store.SaveTextAsync(new SaveTextOverlayRequest(profileId, "art/old/icon.dds", "stale"), CancellationToken.None);
        File.Delete(Path.Combine(filesRoot, "art", "old", "icon.dds"));

        var synced = await store.SyncExternalAsync(new OverlaySyncExternalRequest(profileId), CancellationToken.None);
        var list = await store.ListAsync(profileId, CancellationToken.None);

        Assert.Equal("scan", synced.Mode);
        Assert.Equal(1, synced.Discovered);
        Assert.Equal(1, synced.Imported);
        Assert.Equal(0, synced.Skipped);
        var item = Assert.Single(list.Items);
        Assert.Equal("data/balance/traditional chinese/items.datc64", item.VirtualPath);
        Assert.DoesNotContain(list.Items, entry => entry.VirtualPath.StartsWith("art/", StringComparison.OrdinalIgnoreCase));
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
        Assert.Equal(12, diff.OverlaySize);
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
