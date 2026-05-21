using PoeStudio.Contracts;
using PoeStudio.Core.Native;

namespace PoeStudio.Tests;

public sealed class NativeBundles2IndexReaderTests
{
    [Fact]
    public async Task ProbeAsync_reads_bundle_header_without_decompressing()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), "poe-studio-native-index-tests", Guid.NewGuid().ToString("N"), "_.index.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        await WriteHeaderAsync(indexPath, uncompressedSize: 1024, compressedSize: 512, compressor: 9, chunkCount: 1, chunkSize: 262144);
        var reader = new NativeBundles2IndexReader();

        var result = await reader.ProbeAsync(indexPath, oodleAvailable: false, CancellationToken.None);

        Assert.True(result.Exists);
        Assert.True(result.HeaderValid);
        Assert.Equal(1024, result.UncompressedSize);
        Assert.Equal(512, result.CompressedSize);
        Assert.Equal(9, result.Compressor);
        Assert.Equal(1, result.ChunkCount);
        Assert.Equal(262144, result.ChunkSize);
        Assert.Equal(512, Assert.Single(result.CompressedChunkSizes));
        Assert.Equal(NativeIndexProbeStatus.HeaderOnlyOodleMissing, result.Status);
        Assert.Contains(result.Warnings, warning => warning.Contains("Oodle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProbeAsync_rejects_too_small_file()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), "poe-studio-native-index-tests", Guid.NewGuid().ToString("N"), "_.index.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        await File.WriteAllBytesAsync(indexPath, [1, 2, 3]);
        var reader = new NativeBundles2IndexReader();

        var result = await reader.ProbeAsync(indexPath, oodleAvailable: false, CancellationToken.None);

        Assert.True(result.Exists);
        Assert.False(result.HeaderValid);
        Assert.Equal(NativeIndexProbeStatus.InvalidHeader, result.Status);
    }

    [Fact]
    public async Task ProbeAsync_reports_missing_file()
    {
        var reader = new NativeBundles2IndexReader();

        var result = await reader.ProbeAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "_.index.bin"), oodleAvailable: false, CancellationToken.None);

        Assert.False(result.Exists);
        Assert.Equal(NativeIndexProbeStatus.Missing, result.Status);
    }

    private static async Task WriteHeaderAsync(string path, int uncompressedSize, int compressedSize, int compressor, int chunkCount, int chunkSize)
    {
        await using var stream = File.Create(path);
        await using var writer = new BinaryWriter(stream);
        writer.Write(uncompressedSize);
        writer.Write(compressedSize);
        writer.Write(48 + chunkCount * 4);
        writer.Write(compressor);
        writer.Write(1);
        writer.Write((long)uncompressedSize);
        writer.Write((long)compressedSize);
        writer.Write(chunkCount);
        writer.Write(chunkSize);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(compressedSize);
        writer.Write(new byte[compressedSize]);
    }
}
