using PoeStudio.Contracts;
using PoeStudio.Core.Workspace;

namespace PoeStudio.Core.Native;

public sealed class NativeIndexCacheService
{
    private const int BundleHeaderSize = 60;

    private readonly string workspaceRoot;
    private readonly IOodleCodec oodleCodec;
    private readonly OodleCodecFactory oodleCodecFactory;
    private readonly NativeBundles2IndexReader reader = new();

    public NativeIndexCacheService(string workspaceRoot, IOodleCodec oodleCodec)
        : this(workspaceRoot, oodleCodec, path => new NativeOodleCodec(path))
    {
    }

    public NativeIndexCacheService(string workspaceRoot, IOodleCodec oodleCodec, OodleCodecFactory oodleCodecFactory)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
        this.oodleCodec = oodleCodec;
        this.oodleCodecFactory = oodleCodecFactory;
    }

    public async Task<NativeIndexDecompressResponse> DecompressIndexAsync(
        NativeIndexDecompressRequest request,
        CancellationToken cancellationToken)
    {
        var cachePath = GetCachePath(request.ProfileId);
        using var requestCodec = TryCreateRequestCodec(request.OodlePath, out var oodleWarning);
        var codec = requestCodec ?? oodleCodec;
        var probe = await reader.ProbeAsync(request.IndexPath, codec.IsAvailable, cancellationToken);
        if (!probe.Exists)
        {
            return Response(request, cachePath, false, NativeIndexDecompressStatus.Missing, probe.UncompressedSize, probe.ChunkCount, probe.Warnings);
        }

        if (!probe.HeaderValid)
        {
            return Response(request, cachePath, false, NativeIndexDecompressStatus.InvalidHeader, probe.UncompressedSize, probe.ChunkCount, probe.Warnings);
        }

        if (!codec.IsAvailable)
        {
            var warnings = oodleWarning is null ? probe.Warnings : probe.Warnings.Concat([oodleWarning]).ToArray();
            return Response(request, cachePath, false, NativeIndexDecompressStatus.OodleMissing, probe.UncompressedSize, probe.ChunkCount, warnings);
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
            var actual = codec.Decompress(compressed, decompressed, probe.Compressor!.Value);
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

    private IDisposableOodleCodec? TryCreateRequestCodec(string? oodlePath, out string? warning)
    {
        warning = null;
        if (string.IsNullOrWhiteSpace(oodlePath))
        {
            return null;
        }

        if (!File.Exists(oodlePath))
        {
            warning = $"指定的 oo2core.dll 不存在：{oodlePath}";
            return null;
        }

        try
        {
            return new IDisposableOodleCodec(oodleCodecFactory(oodlePath));
        }
        catch (Exception ex) when (ex is FileNotFoundException or EntryPointNotFoundException or BadImageFormatException or DllNotFoundException)
        {
            warning = $"无法加载 oo2core.dll：{ex.Message}";
            return null;
        }
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

    private sealed class IDisposableOodleCodec(IOodleCodec inner) : IOodleCodec, IDisposable
    {
        public bool IsAvailable => inner.IsAvailable;

        public int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, int compressor)
        {
            return inner.Decompress(compressed, output, compressor);
        }

        public void Dispose()
        {
            if (inner is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
