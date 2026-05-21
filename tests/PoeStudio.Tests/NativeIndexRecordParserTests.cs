using PoeStudio.Core.Native;

namespace PoeStudio.Tests;

public sealed class NativeIndexRecordParserTests
{
    [Fact]
    public async Task ParseAsync_reads_bundle_file_and_directory_counts()
    {
        var path = Path.Combine(Path.GetTempPath(), "poe-studio-native-record-tests", Guid.NewGuid().ToString("N"), "index.decompressed.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await WriteIndexAsync(path);
        var parser = new NativeIndexRecordParser();

        var result = await parser.ParseAsync(path, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(2, result.BundleCount);
        Assert.Equal(2, result.FileCount);
        Assert.Equal(1, result.DirectoryCount);
        Assert.Equal("Tiny/V0.1", result.Bundles[1].Path);
        Assert.Equal(123UL, result.Files[0].PathHash);
        Assert.Equal(1, result.Files[0].BundleIndex);
        Assert.Equal(10, result.Files[0].Offset);
        Assert.Equal(20, result.Files[0].Size);
        Assert.Equal(789UL, result.Directories[0].PathHash);
    }

    [Fact]
    public async Task ParseAsync_rejects_truncated_file_table()
    {
        var path = Path.Combine(Path.GetTempPath(), "poe-studio-native-record-tests", Guid.NewGuid().ToString("N"), "index.decompressed.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, [1, 0, 0, 0, 4, 0]);
        var parser = new NativeIndexRecordParser();

        var result = await parser.ParseAsync(path, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(NativeIndexParseStatus.InvalidData, result.Status);
    }

    private static async Task WriteIndexAsync(string path)
    {
        await using var stream = File.Create(path);
        await using var writer = new BinaryWriter(stream);
        writer.Write(2);
        WriteBundle(writer, "Base", 100);
        WriteBundle(writer, "Tiny/V0.1", 200);
        writer.Write(2);
        WriteFile(writer, 123UL, 1, 10, 20);
        WriteFile(writer, 456UL, 0, 30, 40);
        writer.Write(1);
        writer.Write(789UL);
        writer.Write(1000);
        writer.Write(2000);
        writer.Write(3000);
        writer.Write([1, 2, 3]);
    }

    private static void WriteBundle(BinaryWriter writer, string path, int uncompressedSize)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(path);
        writer.Write(bytes.Length);
        writer.Write(bytes);
        writer.Write(uncompressedSize);
    }

    private static void WriteFile(BinaryWriter writer, ulong pathHash, int bundleIndex, int offset, int size)
    {
        writer.Write(pathHash);
        writer.Write(bundleIndex);
        writer.Write(offset);
        writer.Write(size);
    }
}
