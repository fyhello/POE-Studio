using PoeStudio.Contracts;
using PoeStudio.Core.Native;
using PoeStudio.Core.Patching;

namespace PoeStudio.Tests;

public sealed class NativeIndexRewriteDryRunTests
{
    [Fact]
    public async Task RewriteAsync_updates_matching_file_record_in_decompressed_index_copy()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-native-index-rewrite-tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(root, "index.decompressed.bin");
        var outputPath = Path.Combine(root, "index.rewritten.bin");
        Directory.CreateDirectory(root);
        await WriteIndexAsync(sourcePath);
        var plan = new NativeIndexRewritePlanResponse(
            ProfileId: "profile",
            Ready: true,
            TotalItems: 1,
            Items:
            [
                new NativeIndexRewriteItemDto(
                    VirtualPath: "text/sample.txt",
                    BundleName: "PoeStudio.NativePatch.bundle.bin",
                    Offset: 2048,
                    Size: 32,
                    OverlayHash: "hash",
                    PathHash: "0x000000000000007b",
                    OriginalBundleName: "Base.bundle.bin",
                    OriginalOffset: 16,
                    OriginalSize: 8)
            ],
            Blockers: [],
            Warnings: []);

        var result = await new NativeIndexRewriteDryRun().RewriteAsync(sourcePath, outputPath, plan, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(1, result.UpdatedRecords);
        var parsed = await new NativeIndexRecordParser().ParseAsync(outputPath, CancellationToken.None);
        Assert.True(parsed.Ok);
        var updated = Assert.Single(parsed.Files);
        Assert.Equal(1, updated.BundleIndex);
        Assert.Equal(2048, updated.Offset);
        Assert.Equal(32, updated.Size);
        Assert.Contains(parsed.Bundles, bundle => bundle.Path == "PoeStudio.NativePatch");
    }

    [Fact]
    public async Task RewriteAsync_blocks_when_original_record_does_not_match_plan()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-native-index-rewrite-tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(root, "index.decompressed.bin");
        var outputPath = Path.Combine(root, "index.rewritten.bin");
        Directory.CreateDirectory(root);
        await WriteIndexAsync(sourcePath);
        var plan = new NativeIndexRewritePlanResponse(
            ProfileId: "profile",
            Ready: true,
            TotalItems: 1,
            Items:
            [
                new NativeIndexRewriteItemDto(
                    "text/sample.txt",
                    "PoeStudio.NativePatch.bundle.bin",
                    Offset: 2048,
                    Size: 32,
                    OverlayHash: "hash",
                    PathHash: "0x000000000000007b",
                    OriginalBundleName: "Base.bundle.bin",
                    OriginalOffset: 999,
                    OriginalSize: 8)
            ],
            Blockers: [],
            Warnings: []);

        var result = await new NativeIndexRewriteDryRun().RewriteAsync(sourcePath, outputPath, plan, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(0, result.UpdatedRecords);
        Assert.Contains(result.Warnings, warning => warning.Contains("原始记录不匹配", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(outputPath));
    }

    private static async Task WriteIndexAsync(string path)
    {
        await using var stream = File.Create(path);
        await using var writer = new BinaryWriter(stream);
        writer.Write(1);
        WriteBundle(writer, "Base", 4096);
        writer.Write(1);
        writer.Write(123UL);
        writer.Write(0);
        writer.Write(16);
        writer.Write(8);
        writer.Write(0);
    }

    private static void WriteBundle(BinaryWriter writer, string path, int uncompressedSize)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(path);
        writer.Write(bytes.Length);
        writer.Write(bytes);
        writer.Write(uncompressedSize);
    }
}
