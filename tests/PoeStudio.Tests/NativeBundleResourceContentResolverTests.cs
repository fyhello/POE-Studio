using System.Text;
using PoeStudio.Contracts;
using PoeStudio.Core.Native;

namespace PoeStudio.Tests;

public sealed class NativeBundleResourceContentResolverTests
{
    [Fact]
    public async Task ReadAsync_decompresses_bundle_and_returns_resource_slice()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-native-content-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "Metadata"));
        var payload = Encoding.UTF8.GetBytes("prefix-desired-text-suffix");
        await File.WriteAllBytesAsync(Path.Combine(bundles, "Metadata", "Items.bundle.bin"), NativeBundleTestData.CreateBundle(payload));
        var profile = CreateProfile(root, bundles);
        var resource = Resource("metadata/items/sample.txt", "native-bundles2://Metadata/Items.bundle.bin#offset=7&size=12");
        var resolver = new NativeBundleResourceContentResolver(new CopyOodleCodec());

        var result = await resolver.ReadAsync(profile, resource, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("desired-text", Encoding.UTF8.GetString(result.Data));
    }

    [Fact]
    public async Task ReadAsync_rejects_resource_slice_outside_bundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-native-content-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "Tiny"));
        await File.WriteAllBytesAsync(Path.Combine(bundles, "Tiny", "V0.1.bundle.bin"), NativeBundleTestData.CreateBundle([1, 2, 3]));
        var profile = CreateProfile(root, bundles);
        var resource = Resource("tiny.bin", "native-bundles2://Tiny/V0.1.bundle.bin#offset=2&size=8");
        var resolver = new NativeBundleResourceContentResolver(new CopyOodleCodec());

        var result = await resolver.ReadAsync(profile, resource, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("native_slice_out_of_range", result.ErrorCode);
    }

    [Fact]
    public async Task ReadAsync_decompresses_ggpk_embedded_bundle_and_returns_resource_slice()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-native-content-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var ggpkPath = Path.Combine(root, "Content.ggpk");
        var payload = Encoding.UTF8.GetBytes("prefix-target-text-suffix");
        var bundle = NativeBundleTestData.CreateBundle(payload);
        await GgpkTestData.WriteTinyGgpkWithBundles2Async(ggpkPath, NativeBundleTestData.CreateBundle([1, 2, 3]), bundle);
        var profile = CreateGgpkProfile(root, ggpkPath);
        var index = await new GgpkResourceIndexer(new CopyOodleCodec()).IndexAsync(profile, CancellationToken.None);
        var bundleShell = Assert.Single(index.Resources, item => item.VirtualPath == "bundles2/metadata/items.bundle.bin");
        var resource = Resource(
            "metadata/items/sample.txt",
            $"ggpk-bundles2://{ggpkPath}#bundleOffset={GetQueryValue(bundleShell.PhysicalPath!, "offset")}&bundleSize={GetQueryValue(bundleShell.PhysicalPath!, "size")}&offset=7&size=11");
        var resolver = new NativeBundleResourceContentResolver(new CopyOodleCodec());

        var result = await resolver.ReadAsync(profile, resource, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("target-text", Encoding.UTF8.GetString(result.Data));
    }

    private static ClientProfileDto CreateProfile(string root, string bundles)
    {
        var now = DateTimeOffset.UtcNow;
        return new ClientProfileDto(
            Id: "profile",
            DisplayName: "POE2",
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Bundles2,
            RootPath: root,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: Path.Combine(bundles, "_.index.bin"),
            OodleStatus: OodleStatus.Found,
            ClientFingerprint: "fingerprint",
            CreatedAt: now,
            UpdatedAt: now);
    }

    private static ClientProfileDto CreateGgpkProfile(string root, string ggpkPath)
    {
        var now = DateTimeOffset.UtcNow;
        return new ClientProfileDto(
            Id: "profile",
            DisplayName: "POE2",
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Ggpk,
            RootPath: root,
            ContentGgpkPath: ggpkPath,
            Bundles2Path: null,
            IndexPath: null,
            OodleStatus: OodleStatus.Found,
            ClientFingerprint: "fingerprint",
            CreatedAt: now,
            UpdatedAt: now);
    }

    private static string GetQueryValue(string physicalPath, string key)
    {
        var query = physicalPath.Split('#', 2)[1];
        foreach (var part in query.Split('&'))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return pair[1];
            }
        }

        throw new InvalidOperationException($"Missing query key: {key}");
    }

    private static ResourceSummaryDto Resource(string path, string physicalPath)
    {
        return new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: path,
            NormalizedPath: path,
            Extension: Path.GetExtension(path),
            Kind: ResourceKind.Text,
            Size: 12,
            PhysicalPath: physicalPath,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
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
