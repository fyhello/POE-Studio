using System.Text;
using PoeStudio.Contracts;
using PoeStudio.Core.Native;

namespace PoeStudio.Tests;

public sealed class NativeBundles2ResourceIndexerTests
{
    [Fact]
    public void Index_maps_resolved_files_into_standard_resource_summaries()
    {
        var profile = CreateProfile();
        var virtualPath = "metadata/items/amulet.ot";
        var hash = NativeIndexPathResolver.MurmurHash64A(Encoding.UTF8.GetBytes(virtualPath));
        var parsed = new NativeIndexParseResult(
            Ok: true,
            Status: NativeIndexParseStatus.Parsed,
            BundleCount: 1,
            FileCount: 1,
            DirectoryCount: 0,
            Bundles:
            [
                new NativeBundleRecord(0, "Metadata/Items", 4096)
            ],
            Files:
            [
                new NativeFileRecord(hash, 0, 128, 64)
            ],
            Directories: [],
            DirectoryBundleDataOffset: 0,
            DirectoryBundleDataSize: 0,
            Warnings: []);
        var paths = new NativePathResolveResult(1, 0, new Dictionary<ulong, string> { [hash] = virtualPath }, []);
        var indexer = new NativeBundles2ResourceIndexer();

        var result = indexer.Index(profile, parsed, paths);

        var resource = Assert.Single(result.Resources);
        Assert.Equal(profile.Id, resource.ProfileId);
        Assert.Equal(virtualPath, resource.VirtualPath);
        Assert.Equal(".ot", resource.Extension);
        Assert.Equal(ResourceKind.Table, resource.Kind);
        Assert.Equal(64, resource.Size);
        Assert.Equal(ResourceSourceLayer.Base, resource.SourceLayer);
        Assert.Equal("native-bundles2://Metadata/Items.bundle.bin#offset=128&size=64", resource.PhysicalPath);
        Assert.Equal(0, result.FailedPaths);
    }

    [Fact]
    public void Index_reports_unresolved_path_count_without_throwing()
    {
        var profile = CreateProfile();
        var parsed = new NativeIndexParseResult(
            Ok: true,
            Status: NativeIndexParseStatus.Parsed,
            BundleCount: 1,
            FileCount: 1,
            DirectoryCount: 0,
            Bundles:
            [
                new NativeBundleRecord(0, "Tiny/V0.1", 1024)
            ],
            Files:
            [
                new NativeFileRecord(999UL, 0, 0, 12)
            ],
            Directories: [],
            DirectoryBundleDataOffset: 0,
            DirectoryBundleDataSize: 0,
            Warnings: []);
        var paths = new NativePathResolveResult(0, 1, new Dictionary<ulong, string>(), ["1 个文件路径未解析。"]);
        var indexer = new NativeBundles2ResourceIndexer();

        var result = indexer.Index(profile, parsed, paths);

        Assert.Empty(result.Resources);
        Assert.Equal(1, result.FailedPaths);
        Assert.Contains(result.Warnings, warning => warning.Contains("未解析", StringComparison.OrdinalIgnoreCase));
    }

    private static ClientProfileDto CreateProfile()
    {
        var now = DateTimeOffset.UtcNow;
        return new ClientProfileDto(
            Id: "profile",
            DisplayName: "POE2",
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Bundles2,
            RootPath: "C:/Games/Path of Exile 2",
            ContentGgpkPath: null,
            Bundles2Path: "C:/Games/Path of Exile 2/Bundles2",
            IndexPath: "C:/Games/Path of Exile 2/Bundles2/_.index.bin",
            OodleStatus: OodleStatus.Found,
            ClientFingerprint: "fingerprint",
            CreatedAt: now,
            UpdatedAt: now);
    }
}
