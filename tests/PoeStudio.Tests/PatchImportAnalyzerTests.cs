using System.IO.Compression;
using System.Text;
using PoeStudio.Contracts;
using PoeStudio.Core.Native;
using PoeStudio.Core.Patching;

namespace PoeStudio.Tests;

public sealed class PatchImportAnalyzerTests
{
    [Fact]
    public async Task AnalyzeZipAsync_detects_epic_bundles2_patch_and_verifies_records()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-patch-import-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var zipPath = Path.Combine(root, "epic.zip");
        var hash = NativeIndexPathResolver.MurmurHash64A(Encoding.UTF8.GetBytes("text/sample.txt"));
        var indexPayload = await BuildIndexPayloadAsync(root, hash);
        var compressedIndex = new NativeBundleCompressor(new CopyNativeBundleCodec()).Compress(indexPayload);
        var compressedBundle = new NativeBundleCompressor(new CopyNativeBundleCodec()).Compress([1, 2, 3]);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(zip, "PathOfExile2/Bundles2/_.index.bin", compressedIndex);
            await WriteEntryAsync(zip, "PathOfExile2/Bundles2/Tiny.V0.1.bundle.bin", compressedBundle);
        }

        var result = await new PatchImportAnalyzer().AnalyzeZipAsync(
            new PatchZipAnalyzeRequest(zipPath, "Tiny.V0.1.bundle.bin", "__copy__"),
            CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(PatchZipTemplate.Epic, result.Template);
        Assert.True(result.HasBundlesDirectory);
        Assert.True(result.HasIndex);
        Assert.True(result.HasPatchBundle);
        Assert.Equal("PathOfExile2/Bundles2/", result.BundlesRoot);
        Assert.Equal(2, result.EntryCount);
        Assert.NotNull(result.Verification);
        Assert.True(result.Verification!.Ok);
        Assert.Equal(1, result.Verification.PatchedFileRecords);
    }

    [Fact]
    public async Task AnalyzeZipAsync_reports_missing_bundles2_structure()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-patch-import-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var zipPath = Path.Combine(root, "broken.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(zip, "readme.txt", Encoding.UTF8.GetBytes("not a patch"));
        }

        var result = await new PatchImportAnalyzer().AnalyzeZipAsync(
            new PatchZipAnalyzeRequest(zipPath),
            CancellationToken.None);

        Assert.False(result.Ok);
        Assert.False(result.HasBundlesDirectory);
        Assert.Contains(result.Warnings, warning => warning.Contains("Bundles2", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes);
    }

    private static async Task<byte[]> BuildIndexPayloadAsync(string root, ulong hash)
    {
        var path = Path.Combine(root, "index.payload.bin");
        await using (var stream = File.Create(path))
        await using (var writer = new BinaryWriter(stream))
        {
            writer.Write(2);
            WriteBundle(writer, "Base", 4096);
            WriteBundle(writer, "Tiny.V0.1", 3);
            writer.Write(1);
            writer.Write(hash);
            writer.Write(1);
            writer.Write(0);
            writer.Write(3);
            writer.Write(0);
        }

        return await File.ReadAllBytesAsync(path);
    }

    private static void WriteBundle(BinaryWriter writer, string path, int uncompressedSize)
    {
        var bytes = Encoding.UTF8.GetBytes(path);
        writer.Write(bytes.Length);
        writer.Write(bytes);
        writer.Write(uncompressedSize);
    }
}
