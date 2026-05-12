using System.Buffers.Binary;

namespace PoeStudio.Core.Native;

public interface INativeBundleCodec : IOodleCodec
{
    int CompressorId { get; }

    byte[] Compress(ReadOnlySpan<byte> input);
}

public sealed class CopyNativeBundleCodec : INativeBundleCodec
{
    public bool IsAvailable => true;

    public int CompressorId => 0;

    public byte[] Compress(ReadOnlySpan<byte> input)
    {
        return input.ToArray();
    }

    public int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, int compressor)
    {
        compressed.CopyTo(output);
        return compressed.Length;
    }
}

public sealed class NativeBundleCompressor
{
    private const int FixedHeaderSize = 60;
    private const int ChunkSize = 256 * 1024;
    private readonly INativeBundleCodec codec;

    public NativeBundleCompressor(INativeBundleCodec codec)
    {
        this.codec = codec;
    }

    public byte[] Compress(ReadOnlySpan<byte> payload)
    {
        if (!codec.IsAvailable)
        {
            throw new InvalidOperationException("Native bundle codec 不可用。");
        }

        var chunks = new List<byte[]>();
        var offset = 0;
        while (offset < payload.Length || chunks.Count == 0)
        {
            var count = Math.Min(ChunkSize, payload.Length - offset);
            chunks.Add(codec.Compress(payload.Slice(offset, Math.Max(0, count))));
            offset += count;
            if (payload.Length == 0)
            {
                break;
            }
        }

        var compressedSize = chunks.Sum(chunk => chunk.Length);
        var headSize = 48 + chunks.Count * sizeof(int);
        var output = new byte[FixedHeaderSize + chunks.Count * sizeof(int) + compressedSize];
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0, 4), payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4, 4), compressedSize);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8, 4), headSize);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(12, 4), codec.CompressorId);
        BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(20, 8), payload.Length);
        BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(28, 8), compressedSize);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(36, 4), chunks.Count);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(40, 4), ChunkSize);

        var cursor = FixedHeaderSize;
        foreach (var chunk in chunks)
        {
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(cursor, sizeof(int)), chunk.Length);
            cursor += sizeof(int);
        }

        foreach (var chunk in chunks)
        {
            chunk.CopyTo(output.AsSpan(cursor));
            cursor += chunk.Length;
        }

        return output;
    }
}
