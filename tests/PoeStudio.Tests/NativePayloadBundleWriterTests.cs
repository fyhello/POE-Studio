using PoeStudio.Contracts;
using PoeStudio.Core.Native;
using PoeStudio.Core.Patching;

namespace PoeStudio.Tests;

public sealed class NativePayloadBundleWriterTests
{
    [Fact]
    public async Task WriteAsync_writes_compressed_payload_that_matches_plan_offsets()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-native-payload-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var firstPath = Path.Combine(root, "first.bin");
        var secondPath = Path.Combine(root, "second.bin");
        await File.WriteAllBytesAsync(firstPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(secondPath, [4, 5]);
        var entries = new[]
        {
            Entry("profile", "b.txt", secondPath, 2, "hash-b"),
            Entry("profile", "a.txt", firstPath, 3, "hash-a")
        };
        var plan = new NativePatchPlanResponse(
            "profile",
            "PoeStudio.NativePatch.bundle.bin",
            Ready: true,
            TotalItems: 2,
            Items:
            [
                new NativePatchPlanItemDto("a.txt", "PoeStudio.NativePatch.bundle.bin", 0, 3, "hash-a", true, null),
                new NativePatchPlanItemDto("b.txt", "PoeStudio.NativePatch.bundle.bin", 3, 2, "hash-b", true, null)
            ],
            Blockers: [],
            Warnings: []);

        var result = await new NativePayloadBundleWriter().WriteAsync(root, plan, entries, new CopyNativeBundleCodec(), CancellationToken.None);

        Assert.Equal(Path.Combine(root, "PoeStudio.NativePatch.bundle.bin"), result.BundlePath);
        Assert.Equal(5, result.UncompressedSize);
        Assert.True(File.Exists(result.BundlePath));
        var compressed = await File.ReadAllBytesAsync(result.BundlePath);
        var decompressed = new NativeBundleDecompressor(new CopyNativeBundleCodec()).Decompress(compressed);
        Assert.True(decompressed.Ok);
        Assert.Equal([1, 2, 3, 4, 5], decompressed.Data);
    }

    private static OverlayEntryDto Entry(string profileId, string path, string overlayPath, long size, string hash)
    {
        return new OverlayEntryDto(
            profileId,
            path,
            path,
            overlayPath,
            size,
            hash,
            BaseHash: null,
            BaseSize: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);
    }
}
