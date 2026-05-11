using PoeStudio.Contracts;
using PoeStudio.Core.Workspace;

namespace PoeStudio.Core.Native;

public sealed class NativeIndexCacheService
{
    private const int BundleHeaderSize = 60;

    private readonly string workspaceRoot;
    private readonly IOodleCodec oodleCodec;
    private readonly NativeBundles2IndexReader reader = new();

    public NativeIndexCacheService(string workspaceRoot, IOodleCodec oodleCodec)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
        this.oodleCodec = oodleCodec;
    }

    public async Task<NativeIndexDecompressResponse> DecompressIndexAsync(
        NativeIndexDecompressRequest request,
        CancellationToken cancellationToken)
    {
        var cachePath = GetCachePath(request.ProfileId);
        var probe = await reader.ProbeAsync(request.IndexPath, oodleCodec.IsAvailable, cancellationToken);
        if (!probe.Exists)
        {
            return Response(request, cachePath, false, NativeIndexDecompressStatus.Missing, probe.UncompressedSize, probe.ChunkCount, probe.Warnings);
        }

        if (!probe.HeaderValid)
        {
            return Response(request, cachePath, false, NativeIndexDecompressStatus.InvalidHeader, probe.UncompressedSize, probe.ChunkCount, probe.Warnings);
        }

        if (!oodleCodec.IsAvailable)
        {
            return Response(request, cachePath, false, NativeIndexDecompressStatus.OodleMissing, probe.UncompressedSize, probe.ChunkCount, probe.Warnings);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await using var input = File.Open(request.IndexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        await using var output = File.Create(cachePath);
        input.Position = BundleHeaderSize + (probe.ChunkCount!.Value * sizeof(int));

        var chunkSize = probe.ChunkSize!.Value;
        var remaining = probe.UncompressedSize!.Value;
        foreach (var compressedChunkSize in probe.CompressedChunkSizes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compressed = new byte[compressedChunkSize];
            await input.ReadExactlyAsync(compressed, cancellationToken);
            var expected = Math.Min(chunkSize, remaining);
            var decompressed = new byte[expected];
            var actual = oodleCodec.Decompress(compressed, decompressed, probe.Compressor!.Value);
            if (actual != expected)
            {
                return Response(
                    request,
                    cachePath,
                    false,
                    NativeIndexDecompressStatus.Failed,
                    probe.UncompressedSize,
                    probe.ChunkCount,
                    [$"Oodle 解压返回长度不匹配：expected={expected}, actual={actual}。"]);
            }

            await output.WriteAsync(decompressed, cancellationToken);
            remaining -= expected;
        }

        return Response(request, cachePath, true, NativeIndexDecompressStatus.Cached, probe.UncompressedSize, probe.ChunkCount, []);
    }

    private string GetCachePath(string profileId)
    {
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, profileId);
        return Path.Combine(layout.RawCacheRoot, "native", "bundles2", "index.decompressed.bin");
    }

    private static NativeIndexDecompressResponse Response(
        NativeIndexDecompressRequest request,
        string cachePath,
        bool ok,
        NativeIndexDecompressStatus status,
        long? uncompressedSize,
        int? chunkCount,
        IReadOnlyList<string> warnings)
    {
        return new NativeIndexDecompressResponse(
            request.ProfileId,
            request.IndexPath,
            cachePath,
            ok,
            status,
            uncompressedSize,
            chunkCount,
            warnings);
    }
}
