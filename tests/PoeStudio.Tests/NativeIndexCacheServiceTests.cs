using PoeStudio.Contracts;
using PoeStudio.Core.Native;

namespace PoeStudio.Tests;

public sealed class NativeIndexCacheServiceTests
{
    [Fact]
    public async Task DecompressIndexAsync_returns_oodle_missing_without_codec()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-native-cache-tests", Guid.NewGuid().ToString("N"));
        var indexPath = Path.Combine(root, "_.index.bin");
        Directory.CreateDirectory(root);
        await NativeIndexTestFile.WriteBundleAsync(indexPath, [1, 2, 3], compressor: 12, chunkSize: 262144);
        var service = new NativeIndexCacheService(root, new MissingOodleCodec());

        var result = await service.DecompressIndexAsync(new NativeIndexDecompressRequest("profile", indexPath), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(NativeIndexDecompressStatus.OodleMissing, result.Status);
        Assert.False(File.Exists(result.CachePath));
    }

    [Fact]
    public async Task DecompressIndexAsync_writes_decompressed_cache_with_codec()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-native-cache-tests", Guid.NewGuid().ToString("N"));
        var indexPath = Path.Combine(root, "_.index.bin");
        Directory.CreateDirectory(root);
        var payload = Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251)).ToArray();
        await NativeIndexTestFile.WriteBundleAsync(indexPath, payload, compressor: 12, chunkSize: 262144);
        var service = new NativeIndexCacheService(root, new CopyOodleCodec());

        var result = await service.DecompressIndexAsync(new NativeIndexDecompressRequest("profile", indexPath), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(NativeIndexDecompressStatus.Cached, result.Status);
        Assert.True(File.Exists(result.CachePath));
        Assert.Equal(payload, await File.ReadAllBytesAsync(result.CachePath));
        Assert.Equal(payload.Length, result.UncompressedSize);
        Assert.Equal(2, result.ChunkCount);
    }

    private sealed class MissingOodleCodec : IOodleCodec
    {
        public bool IsAvailable => false;

        public int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, int compressor)
        {
            throw new NotSupportedException();
        }
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

internal static class NativeIndexTestFile
{
    public static async Task WriteBundleAsync(string path, byte[] payload, int compressor, int chunkSize)
    {
        await using var stream = File.Create(path);
        await using var writer = new BinaryWriter(stream);
        var chunkCount = (payload.Length + chunkSize - 1) / chunkSize;
        writer.Write(payload.Length);
        writer.Write(payload.Length);
        writer.Write(48 + chunkCount * 4);
        writer.Write(compressor);
        writer.Write(1);
        writer.Write((long)payload.Length);
        writer.Write((long)payload.Length);
        writer.Write(chunkCount);
        writer.Write(chunkSize);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        for (var offset = 0; offset < payload.Length; offset += chunkSize)
        {
            writer.Write(Math.Min(chunkSize, payload.Length - offset));
        }

        writer.Write(payload);
    }
}
