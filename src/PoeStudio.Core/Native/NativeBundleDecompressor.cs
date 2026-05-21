using System.Buffers.Binary;

namespace PoeStudio.Core.Native;

public enum NativeBundleDecompressStatus
{
    InvalidHeader = 0,
    OodleMissing = 1,
    Failed = 2,
    Decompressed = 3
}

public sealed record NativeBundleDecompressResult(
    bool Ok,
    NativeBundleDecompressStatus Status,
    byte[] Data,
    IReadOnlyList<string> Warnings);

public sealed class NativeBundleDecompressor
{
    private const int BundleHeaderSize = 60;
    private readonly IOodleCodec oodleCodec;

    public NativeBundleDecompressor(IOodleCodec oodleCodec)
    {
        this.oodleCodec = oodleCodec;
    }

    public NativeBundleDecompressResult Decompress(ReadOnlySpan<byte> bundleData)
    {
        if (bundleData.Length < BundleHeaderSize)
        {
            return Fail(NativeBundleDecompressStatus.InvalidHeader, "bundle 数据小于 header。");
        }

        var uncompressedSize = BinaryPrimitives.ReadInt32LittleEndian(bundleData[..4]);
        var compressedSize = BinaryPrimitives.ReadInt32LittleEndian(bundleData.Slice(4, 4));
        var headSize = BinaryPrimitives.ReadInt32LittleEndian(bundleData.Slice(8, 4));
        var compressor = BinaryPrimitives.ReadInt32LittleEndian(bundleData.Slice(12, 4));
        var uncompressedSizeLong = BinaryPrimitives.ReadInt64LittleEndian(bundleData.Slice(20, 8));
        var compressedSizeLong = BinaryPrimitives.ReadInt64LittleEndian(bundleData.Slice(28, 8));
        var chunkCount = BinaryPrimitives.ReadInt32LittleEndian(bundleData.Slice(36, 4));
        var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(bundleData.Slice(40, 4));

        if (uncompressedSize < 0
            || compressedSize < 0
            || headSize < 48
            || chunkCount < 0
            || chunkSize <= 0
            || uncompressedSizeLong != uncompressedSize
            || compressedSizeLong != compressedSize)
        {
            return Fail(NativeBundleDecompressStatus.InvalidHeader, "bundle header 字段不合法。");
        }

        var chunkTableBytes = checked(chunkCount * sizeof(int));
        if (headSize != 48 + chunkTableBytes || bundleData.Length < BundleHeaderSize + chunkTableBytes + compressedSize)
        {
            return Fail(NativeBundleDecompressStatus.InvalidHeader, "bundle chunk table 或压缩数据不完整。");
        }

        if (!oodleCodec.IsAvailable)
        {
            return Fail(NativeBundleDecompressStatus.OodleMissing, "Oodle 不可用，无法解压 bundle 数据。");
        }

        var output = new byte[uncompressedSize];
        var compressedOffset = BundleHeaderSize + chunkTableBytes;
        var outputOffset = 0;

        for (var i = 0; i < chunkCount; i++)
        {
            var compressedChunkSize = BinaryPrimitives.ReadInt32LittleEndian(bundleData.Slice(BundleHeaderSize + i * sizeof(int), sizeof(int)));
            if (compressedChunkSize < 0 || compressedOffset + compressedChunkSize > bundleData.Length)
            {
                return Fail(NativeBundleDecompressStatus.InvalidHeader, "bundle chunk size 不合法。");
            }

            var expected = Math.Min(chunkSize, uncompressedSize - outputOffset);
            var actual = oodleCodec.Decompress(
                bundleData.Slice(compressedOffset, compressedChunkSize),
                output.AsSpan(outputOffset, expected),
                compressor);
            if (actual != expected)
            {
                return Fail(NativeBundleDecompressStatus.Failed, $"Oodle 解压返回长度不匹配：expected={expected}, actual={actual}。");
            }

            compressedOffset += compressedChunkSize;
            outputOffset += expected;
        }

        return new NativeBundleDecompressResult(true, NativeBundleDecompressStatus.Decompressed, output, []);
    }

    private static NativeBundleDecompressResult Fail(NativeBundleDecompressStatus status, string warning)
    {
        return new NativeBundleDecompressResult(false, status, [], [warning]);
    }
}
