using PoeStudio.Contracts;

namespace PoeStudio.Core.Native;

public sealed class NativeIndexPathService
{
    private readonly NativeIndexCacheService cacheService;
    private readonly NativeIndexRecordParser parser = new();
    private readonly OodleCodecFactory oodleCodecFactory;

    public NativeIndexPathService(NativeIndexCacheService cacheService, OodleCodecFactory oodleCodecFactory)
    {
        this.cacheService = cacheService;
        this.oodleCodecFactory = oodleCodecFactory;
    }

    public async Task<NativeIndexResolvePathsResponse> ResolveAsync(
        NativeIndexResolvePathsRequest request,
        CancellationToken cancellationToken)
    {
        var decompressed = await cacheService.DecompressIndexAsync(
            new NativeIndexDecompressRequest(request.ProfileId, request.IndexPath, request.OodlePath),
            cancellationToken);
        if (!decompressed.Ok)
        {
            return new NativeIndexResolvePathsResponse(
                Ok: false,
                request.ProfileId,
                FileCount: 0,
                ResolvedCount: 0,
                FailedCount: 0,
                BundleCount: 0,
                DirectoryCount: 0,
                SamplePaths: [],
                decompressed.Warnings);
        }

        var parsed = await parser.ParseAsync(decompressed.CachePath, cancellationToken);
        if (!parsed.Ok)
        {
            return new NativeIndexResolvePathsResponse(
                Ok: false,
                request.ProfileId,
                FileCount: 0,
                ResolvedCount: 0,
                FailedCount: 0,
                BundleCount: 0,
                DirectoryCount: 0,
                SamplePaths: [],
                parsed.Warnings);
        }

        using var oodle = CreateDisposableCodec(request.OodlePath);
        var bytes = await File.ReadAllBytesAsync(decompressed.CachePath, cancellationToken);
        var directoryBundle = bytes.AsSpan((int)parsed.DirectoryBundleDataOffset, (int)parsed.DirectoryBundleDataSize).ToArray();
        var directoryData = new NativeBundleDecompressor(oodle).Decompress(directoryBundle);
        if (!directoryData.Ok)
        {
            return new NativeIndexResolvePathsResponse(
                Ok: false,
                request.ProfileId,
                parsed.FileCount,
                ResolvedCount: 0,
                FailedCount: 0,
                parsed.BundleCount,
                parsed.DirectoryCount,
                SamplePaths: [],
                directoryData.Warnings);
        }

        var result = new NativeIndexPathResolver().Resolve(parsed.Files, parsed.Directories, directoryData.Data);
        return new NativeIndexResolvePathsResponse(
            Ok: true,
            request.ProfileId,
            parsed.FileCount,
            result.ResolvedCount,
            result.FailedCount,
            parsed.BundleCount,
            parsed.DirectoryCount,
            result.Paths.Values.Take(50).ToArray(),
            result.Warnings);
    }

    private IDisposableOodleCodec CreateDisposableCodec(string oodlePath)
    {
        return new IDisposableOodleCodec(oodleCodecFactory(oodlePath));
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
