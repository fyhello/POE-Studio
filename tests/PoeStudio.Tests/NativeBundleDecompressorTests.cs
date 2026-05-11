using PoeStudio.Core.Native;

namespace PoeStudio.Tests;

public sealed class NativeBundleDecompressorTests
{
    [Fact]
    public void Decompress_returns_oodle_missing_without_codec()
    {
        var bundle = NativeBundleTestData.CreateBundle([1, 2, 3]);
        var decompressor = new NativeBundleDecompressor(new MissingOodleCodec());

        var result = decompressor.Decompress(bundle);

        Assert.False(result.Ok);
        Assert.Equal(NativeBundleDecompressStatus.OodleMissing, result.Status);
    }

    [Fact]
    public void Decompress_returns_payload_with_codec()
    {
        var payload = Enumerable.Range(0, 300_000).Select(item => (byte)(item % 251)).ToArray();
        var bundle = NativeBundleTestData.CreateBundle(payload);
        var decompressor = new NativeBundleDecompressor(new CopyOodleCodec());

        var result = decompressor.Decompress(bundle);

        Assert.True(result.Ok);
        Assert.Equal(NativeBundleDecompressStatus.Decompressed, result.Status);
        Assert.Equal(payload, result.Data);
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

internal static class NativeBundleTestData
{
    public static byte[] CreateBundle(byte[] payload, int chunkSize = 262144)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var chunkCount = (payload.Length + chunkSize - 1) / chunkSize;
        writer.Write(payload.Length);
        writer.Write(payload.Length);
        writer.Write(48 + chunkCount * 4);
        writer.Write(12);
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
        return stream.ToArray();
    }
}
