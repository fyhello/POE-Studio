using System.Text;
using PoeStudio.Contracts;
using PoeStudio.Core.Native;
using PoeStudio.Core.Patching;
using PoeStudio.Core.Workspace;

namespace PoeStudio.Tests;

public sealed class NativeBundles2PackageWriterTests
{
    [Fact]
    public async Task WriteAsync_requires_codec_or_request_oodle_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-native-package-tests", Guid.NewGuid().ToString("N"));
        var profile = Profile(root) with { OodleStatus = OodleStatus.Found };
        var writer = new NativeBundles2PackageWriter(root, new StaticPatchResourceLookup());

        var ex = await Assert.ThrowsAsync<PatchBuildException>(() => writer.WriteAsync(new PatchPackageWriterContext(
            profile,
            new PatchBuildRequest(profile.Id, WriterKind: PatchPackageWriterKind.NativeBundles2),
            Path.Combine(root, "out", "Bundles2"),
            [],
            []), CancellationToken.None));

        Assert.Equal("native_codec_unavailable", ex.ErrorCode);
        Assert.Contains("oo2core", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteAsync_builds_index_and_payload_bundle_from_cache_and_resource_lookup()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-native-package-tests", Guid.NewGuid().ToString("N"));
        var profile = Profile(root) with { OodleStatus = OodleStatus.Found };
        var overlayPath = Path.Combine(root, "overlay.txt");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(overlayPath, "patched");
        var hash = NativeIndexPathResolver.MurmurHash64A(Encoding.UTF8.GetBytes("text/sample.txt"));
        var layout = WorkspaceLayout.ForProfile(root, profile.Id);
        var indexCachePath = Path.Combine(layout.RawCacheRoot, "native", "bundles2", "index.decompressed.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(indexCachePath)!);
        await WriteDecompressedIndexAsync(indexCachePath, hash);
        var overlay = Entry(profile.Id, "text/sample.txt", overlayPath, 7, "hash");
        var resource = new ResourceSummaryDto(
            "resource",
            profile.Id,
            "text/sample.txt",
            "text/sample.txt",
            ".txt",
            ResourceKind.Text,
            Size: 7,
            PhysicalPath: "native-bundles2://Base.bundle.bin#offset=16&size=8",
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var writer = new NativeBundles2PackageWriter(root, new StaticPatchResourceLookup(resource), new CopyNativeBundleCodec());
        var bundlesDirectory = Path.Combine(root, "out", "Bundles2");

        var result = await writer.WriteAsync(new PatchPackageWriterContext(
            profile,
            new PatchBuildRequest(profile.Id, BundleName: "PoeStudio.NativePatch.bundle.bin", WriterKind: PatchPackageWriterKind.NativeBundles2),
            bundlesDirectory,
            [overlay],
            []), CancellationToken.None);

        Assert.Equal(PatchBuildMode.NativeBundles2, result.BuildMode);
        Assert.True(File.Exists(Path.Combine(bundlesDirectory, "_.index.bin")));
        Assert.True(File.Exists(Path.Combine(bundlesDirectory, "PoeStudio.NativePatch.bundle.bin")));
        var bundleData = await File.ReadAllBytesAsync(result.BundlePath);
        var payload = new NativeBundleDecompressor(new CopyNativeBundleCodec()).Decompress(bundleData);
        Assert.True(payload.Ok);
        Assert.Equal(Encoding.UTF8.GetBytes("patched"), payload.Data);
        var indexData = await File.ReadAllBytesAsync(result.IndexPath);
        var rewritten = new NativeBundleDecompressor(new CopyNativeBundleCodec()).Decompress(indexData);
        Assert.True(rewritten.Ok);
        var rewrittenPath = Path.Combine(root, "rewritten.bin");
        await File.WriteAllBytesAsync(rewrittenPath, rewritten.Data);
        var parsed = await new NativeIndexRecordParser().ParseAsync(rewrittenPath, CancellationToken.None);
        Assert.True(parsed.Ok);
        Assert.Equal(1, parsed.Files[0].BundleIndex);
        Assert.Equal(0, parsed.Files[0].Offset);
        Assert.Equal(7, parsed.Files[0].Size);
    }

    private static ClientProfileDto Profile(string root)
    {
        return new ClientProfileDto(
            Id: Guid.NewGuid().ToString("N"),
            DisplayName: "test",
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Bundles2,
            RootPath: root,
            ContentGgpkPath: null,
            Bundles2Path: Path.Combine(root, "Bundles2"),
            IndexPath: Path.Combine(root, "Bundles2", "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);
    }

    private static OverlayEntryDto Entry(string profileId, string path, string overlayPath, long size, string hash)
    {
        return new OverlayEntryDto(profileId, path, path, overlayPath, size, hash, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }

    private static async Task WriteDecompressedIndexAsync(string path, ulong pathHash)
    {
        await using var stream = File.Create(path);
        await using var writer = new BinaryWriter(stream);
        writer.Write(1);
        var bundleBytes = Encoding.UTF8.GetBytes("Base");
        writer.Write(bundleBytes.Length);
        writer.Write(bundleBytes);
        writer.Write(4096);
        writer.Write(1);
        writer.Write(pathHash);
        writer.Write(0);
        writer.Write(16);
        writer.Write(8);
        writer.Write(0);
        writer.Write([1, 2, 3]);
    }

    private sealed class StaticPatchResourceLookup(params ResourceSummaryDto[] resources) : IPatchResourceLookup
    {
        public Task<ResourceSummaryDto?> GetByPathAsync(string profileId, string virtualPath, CancellationToken cancellationToken)
        {
            return Task.FromResult(resources.FirstOrDefault(resource =>
                string.Equals(resource.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(resource.NormalizedPath, virtualPath, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
