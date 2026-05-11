using PoeStudio.Contracts;

namespace PoeStudio.Core.Native;

public sealed class NativeBundles2IndexReader
{
    private const int BundleHeaderSize = 60;

    public async Task<NativeIndexProbeResponse> ProbeAsync(
        string indexPath,
        bool oodleAvailable,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(indexPath) || !File.Exists(indexPath))
        {
            return new NativeIndexProbeResponse(
                indexPath,
                Exists: false,
                HeaderValid: false,
                NativeIndexProbeStatus.Missing,
                FileSize: 0,
                UncompressedSize: null,
                CompressedSize: null,
                HeadSize: null,
                Compressor: null,
                ChunkCount: null,
                ChunkSize: null,
                CompressedChunkSizes: [],
                Warnings: ["index 文件不存在。"]);
        }

        var info = new FileInfo(indexPath);
        if (info.Length < BundleHeaderSize)
        {
            return Invalid(indexPath, info.Length, "文件小于 Bundles2 bundle header。");
        }

        await using var stream = File.Open(indexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(stream);
        var uncompressedSize = reader.ReadInt32();
        var compressedSize = reader.ReadInt32();
        var headSize = reader.ReadInt32();
        var compressor = reader.ReadInt32();
        _ = reader.ReadInt32();
        var uncompressedSizeLong = reader.ReadInt64();
        var compressedSizeLong = reader.ReadInt64();
        var chunkCount = reader.ReadInt32();
        var chunkSize = reader.ReadInt32();

        if (uncompressedSize < 0
            || compressedSize < 0
            || headSize < 48
            || chunkCount < 0
            || chunkSize <= 0
            || uncompressedSizeLong != uncompressedSize
            || compressedSizeLong != compressedSize)
        {
            return Invalid(indexPath, info.Length, "Bundles2 bundle header 字段不合法。");
        }

        var chunkTableBytes = checked(chunkCount * sizeof(int));
        var expectedHeadSize = 48 + chunkTableBytes;
        if (headSize != expectedHeadSize || info.Length < BundleHeaderSize + chunkTableBytes)
        {
            return Invalid(indexPath, info.Length, "Bundles2 bundle chunk table 不完整。");
        }

        stream.Position = BundleHeaderSize;
        var chunks = new List<int>(chunkCount);
        for (var i = 0; i < chunkCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var size = reader.ReadInt32();
            if (size < 0)
            {
                return Invalid(indexPath, info.Length, "Bundles2 bundle chunk size 不合法。");
            }

            chunks.Add(size);
        }

        var warnings = new List<string>();
        var status = NativeIndexProbeStatus.HeaderReady;
        if (!oodleAvailable)
        {
            status = NativeIndexProbeStatus.HeaderOnlyOodleMissing;
            warnings.Add("Oodle 不可用，只能读取 bundle header，不能解压并解析内部 index。");
        }

        return new NativeIndexProbeResponse(
            indexPath,
            Exists: true,
            HeaderValid: true,
            status,
            info.Length,
            uncompressedSize,
            compressedSize,
            headSize,
            compressor,
            chunkCount,
            chunkSize,
            chunks,
            warnings);
    }

    private static NativeIndexProbeResponse Invalid(string indexPath, long fileSize, string warning)
    {
        return new NativeIndexProbeResponse(
            indexPath,
            Exists: true,
            HeaderValid: false,
            NativeIndexProbeStatus.InvalidHeader,
            fileSize,
            UncompressedSize: null,
            CompressedSize: null,
            HeadSize: null,
            Compressor: null,
            ChunkCount: null,
            ChunkSize: null,
            CompressedChunkSizes: [],
            Warnings: [warning]);
    }
}
