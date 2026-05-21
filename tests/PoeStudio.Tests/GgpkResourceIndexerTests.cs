using PoeStudio.Contracts;
using PoeStudio.Core.Native;
using System.Text;

namespace PoeStudio.Tests;

public sealed class GgpkResourceIndexerTests
{
    [Fact]
    public async Task Index_reads_file_tree_from_content_ggpk_without_external_libraries()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-ggpk-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var ggpkPath = Path.Combine(root, "Content.ggpk");
        await GgpkTestData.WriteTinyGgpkAsync(ggpkPath);
        var profile = new ClientProfileDto(
            Id: "profile",
            DisplayName: "GGPK",
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Ggpk,
            RootPath: root,
            ContentGgpkPath: ggpkPath,
            Bundles2Path: null,
            IndexPath: null,
            OodleStatus: OodleStatus.Found,
            ClientFingerprint: "fingerprint",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var result = await new GgpkResourceIndexer().IndexAsync(profile, CancellationToken.None);

        if (result.Resources.Count == 0)
        {
            throw new InvalidOperationException(string.Join(" | ", result.Warnings));
        }

        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(3, result.DirectoryCount);
        var resource = Assert.Single(result.Resources);
        Assert.Equal("metadata/items/amulet.ot", resource.VirtualPath);
        Assert.Equal(".ot", resource.Extension);
        Assert.Equal(ResourceKind.Table, resource.Kind);
        Assert.Equal(5, resource.Size);
        Assert.StartsWith("ggpk://", resource.PhysicalPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Index_expands_bundles2_resources_inside_content_ggpk()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-ggpk-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var ggpkPath = Path.Combine(root, "Content.ggpk");
        var virtualPath = "metadata/items/amulet.ot";
        var payload = Encoding.UTF8.GetBytes("translated text");
        var indexBundle = NativeBundleTestData.CreateBundle(BuildNativeIndexPayload(virtualPath, "metadata/items", payload.Length));
        var payloadBundle = NativeBundleTestData.CreateBundle(payload);
        await GgpkTestData.WriteTinyGgpkWithBundles2Async(ggpkPath, indexBundle, payloadBundle);
        var profile = new ClientProfileDto(
            Id: "profile",
            DisplayName: "GGPK",
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Ggpk,
            RootPath: root,
            ContentGgpkPath: ggpkPath,
            Bundles2Path: null,
            IndexPath: null,
            OodleStatus: OodleStatus.Found,
            ClientFingerprint: "fingerprint",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var result = await new GgpkResourceIndexer(new CopyOodleCodec()).IndexAsync(profile, CancellationToken.None);

        var resource = Assert.Single(result.Resources, item => item.VirtualPath == virtualPath);
        Assert.Equal(".ot", resource.Extension);
        Assert.Equal(ResourceKind.Table, resource.Kind);
        Assert.Equal(payload.Length, resource.Size);
        Assert.StartsWith("ggpk-bundles2://", resource.PhysicalPath, StringComparison.Ordinal);
        Assert.NotNull(result.Bundles2Coverage);
        Assert.Equal(1, result.Bundles2Coverage.IndexFileCount);
        Assert.Equal(0, result.Bundles2Coverage.MissingBundleCount);
        Assert.Equal(1, result.Bundles2Coverage.ResourcesInExistingBundles);
        Assert.Equal(0, result.Bundles2Coverage.ResourcesInMissingBundles);
    }

    [Fact]
    public async Task Index_reports_bundles2_missing_bundle_coverage()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-ggpk-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var ggpkPath = Path.Combine(root, "Content.ggpk");
        var virtualPath = "metadata/items/missing.ot";
        var indexBundle = NativeBundleTestData.CreateBundle(BuildNativeIndexPayload(virtualPath, "metadata/items/missing", payloadSize: 7));
        var unrelatedPayloadBundle = NativeBundleTestData.CreateBundle(Encoding.UTF8.GetBytes("unused"));
        await GgpkTestData.WriteTinyGgpkWithBundles2Async(ggpkPath, indexBundle, unrelatedPayloadBundle);
        var profile = new ClientProfileDto(
            Id: "profile",
            DisplayName: "GGPK",
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Ggpk,
            RootPath: root,
            ContentGgpkPath: ggpkPath,
            Bundles2Path: null,
            IndexPath: null,
            OodleStatus: OodleStatus.Found,
            ClientFingerprint: "fingerprint",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var result = await new GgpkResourceIndexer(new CopyOodleCodec()).IndexAsync(profile, CancellationToken.None);

        Assert.DoesNotContain(result.Resources, item => item.VirtualPath == virtualPath);
        Assert.NotNull(result.Bundles2Coverage);
        Assert.Equal(1, result.Bundles2Coverage.IndexBundleCount);
        Assert.Equal(1, result.Bundles2Coverage.IndexFileCount);
        Assert.Equal(0, result.Bundles2Coverage.ExistingBundleCount);
        Assert.Equal(1, result.Bundles2Coverage.MissingBundleCount);
        Assert.Equal(0, result.Bundles2Coverage.ResourcesInExistingBundles);
        Assert.Equal(1, result.Bundles2Coverage.ResourcesInMissingBundles);
        var missing = Assert.Single(result.Bundles2Coverage.TopMissingBundles);
        Assert.Equal("missing.bundle.bin", missing.BundleFileName);
        Assert.Equal(1, missing.ResourceCount);
    }

    private static byte[] BuildNativeIndexPayload(string virtualPath, string bundlePath, int payloadSize)
    {
        var hash = NativeIndexPathResolver.MurmurHash64A(Encoding.UTF8.GetBytes(virtualPath));
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(1);
        WriteBundle(writer, bundlePath, payloadSize);
        writer.Write(1);
        writer.Write(hash);
        writer.Write(0);
        writer.Write(0);
        writer.Write(payloadSize);
        writer.Write(1);
        writer.Write(0xF42A94E69CFF42FEUL);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(NativeBundleTestData.CreateBundle(BuildDirectoryData(virtualPath)));
        return stream.ToArray();
    }

    private static byte[] BuildDirectoryData(string virtualPath)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(-1);
        writer.Write(Encoding.UTF8.GetBytes(virtualPath));
        writer.Write((byte)0);
        return stream.ToArray();
    }

    private static void WriteBundle(BinaryWriter writer, string path, int uncompressedSize)
    {
        var bytes = Encoding.UTF8.GetBytes(path);
        writer.Write(bytes.Length);
        writer.Write(bytes);
        writer.Write(uncompressedSize);
    }

    private sealed class CopyOodleCodec : IOodleCodec
    {
        public bool IsAvailable => true;

        public int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, int compressor)
        {
            compressed.CopyTo(output);
            return compressed.Length;
        }
    }

}
