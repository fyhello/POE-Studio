using PoeStudio.Contracts;
using PoeStudio.Core.Resources;

namespace PoeStudio.Tests;

public sealed class FileSystemResourceIndexerTests
{
    [Fact]
    public async Task IndexAsync_enumerates_expanded_resources_and_skips_container_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-indexer-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "metadata", "items"));
        Directory.CreateDirectory(Path.Combine(bundles, "art", "textures"));
        Directory.CreateDirectory(Path.Combine(bundles, "data", "statdescriptions"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "metadata", "items", "amulet.ot"), "item");
        await File.WriteAllBytesAsync(Path.Combine(bundles, "art", "textures", "icon.dds"), [1, 2, 3, 4]);
        await File.WriteAllTextAsync(Path.Combine(bundles, "data", "statdescriptions", "skill_stat_descriptions.csd"), "description");
        await File.WriteAllBytesAsync(Path.Combine(bundles, "_.index.bin"), [9, 9, 9]);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "Tiny.V0.1.bundle.bin"), [8, 8, 8]);
        await File.WriteAllBytesAsync(Path.Combine(root, "Content.ggpk"), [7, 7, 7]);
        var profile = Profile(root, bundles);
        var indexer = new FileSystemResourceIndexer();

        var result = await indexer.IndexAsync(profile, CancellationToken.None);

        Assert.Equal(3, result.Resources.Count);
        Assert.Empty(result.Warnings);
        Assert.Contains(result.Resources, item => item.VirtualPath == "metadata/items/amulet.ot" && item.Kind == ResourceKind.Table);
        Assert.Contains(result.Resources, item => item.VirtualPath == "art/textures/icon.dds" && item.Kind == ResourceKind.Image);
        Assert.Contains(result.Resources, item => item.VirtualPath == "data/statdescriptions/skill_stat_descriptions.csd" && item.Kind == ResourceKind.Text);
        Assert.DoesNotContain(result.Resources, item => item.VirtualPath.EndsWith(".bundle.bin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IndexAsync_returns_warning_when_source_directory_is_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-indexer-tests", Guid.NewGuid().ToString("N"));
        var profile = Profile(root, Path.Combine(root, "Bundles2"));
        var indexer = new FileSystemResourceIndexer();

        var result = await indexer.IndexAsync(profile, CancellationToken.None);

        Assert.Empty(result.Resources);
        Assert.Contains(result.Warnings, warning => warning.Contains("资源目录不存在", StringComparison.Ordinal));
    }

    private static ClientProfileDto Profile(string root, string bundles)
    {
        return new ClientProfileDto(
            Id: Guid.NewGuid().ToString("N"),
            DisplayName: "test",
            Platform: ClientPlatform.WeGame,
            EntryKind: ClientEntryKind.Bundles2,
            RootPath: root,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: Path.Combine(bundles, "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);
    }
}
