namespace PoeStudio.Contracts;

public enum NativeIndexProbeStatus
{
    Missing = 0,
    InvalidHeader = 1,
    HeaderOnlyOodleMissing = 2,
    HeaderReady = 3
}

public sealed record NativeIndexProbeRequest(
    string IndexPath,
    bool OodleAvailable = false);

public sealed record NativeIndexProbeResponse(
    string IndexPath,
    bool Exists,
    bool HeaderValid,
    NativeIndexProbeStatus Status,
    long FileSize,
    int? UncompressedSize,
    int? CompressedSize,
    int? HeadSize,
    int? Compressor,
    int? ChunkCount,
    int? ChunkSize,
    IReadOnlyList<int> CompressedChunkSizes,
    IReadOnlyList<string> Warnings);

public enum NativeIndexDecompressStatus
{
    Missing = 0,
    InvalidHeader = 1,
    OodleMissing = 2,
    Failed = 3,
    Cached = 4
}

public sealed record NativeIndexDecompressRequest(
    string ProfileId,
    string IndexPath,
    string? OodlePath = null);

public sealed record NativeIndexDecompressResponse(
    string ProfileId,
    string IndexPath,
    string CachePath,
    bool Ok,
    NativeIndexDecompressStatus Status,
    long? UncompressedSize,
    int? ChunkCount,
    IReadOnlyList<string> Warnings);

public sealed record NativeIndexParseRequest(string DecompressedIndexPath);

public sealed record NativeIndexParseResponse(
    bool Ok,
    string DecompressedIndexPath,
    int BundleCount,
    int FileCount,
    int DirectoryCount,
    long DirectoryBundleDataOffset,
    long DirectoryBundleDataSize,
    IReadOnlyList<string> Warnings);

public sealed record NativeIndexResolvePathsRequest(
    string ProfileId,
    string IndexPath,
    string OodlePath);

public sealed record NativeIndexResolvePathsResponse(
    bool Ok,
    string ProfileId,
    int FileCount,
    int ResolvedCount,
    int FailedCount,
    int BundleCount,
    int DirectoryCount,
    IReadOnlyList<string> SamplePaths,
    IReadOnlyList<string> Warnings);

public sealed record NativeResourceIndexBuildRequest(
    string ProfileId,
    string? IndexPath = null,
    string? OodlePath = null);

public sealed record NativeResourceIndexBuildResponse(
    bool Ok,
    string ProfileId,
    int TotalFiles,
    int ResolvedResources,
    int FailedPaths,
    int BundleCount,
    int DirectoryCount,
    DateTimeOffset IndexedAt,
    IReadOnlyList<string> Warnings);

public sealed record GgpkResourceIndexBuildRequest(
    string ProfileId,
    string? OodlePath = null);

public sealed record GgpkResourceIndexBuildResponse(
    bool Ok,
    string ProfileId,
    string GgpkPath,
    int TotalFiles,
    int ResolvedResources,
    int DirectoryCount,
    DateTimeOffset IndexedAt,
    IReadOnlyList<string> Warnings,
    GgpkBundles2CoverageDto? Bundles2Coverage = null);

public sealed record GgpkBundles2CoverageDto(
    int IndexBundleCount,
    int IndexFileCount,
    int IndexDirectoryCount,
    int ResolvedPathCount,
    int FailedPathCount,
    int ExistingBundleCount,
    int MissingBundleCount,
    int ResourcesInExistingBundles,
    int ResourcesInMissingBundles,
    IReadOnlyList<GgpkMissingBundleDto> TopMissingBundles);

public sealed record GgpkMissingBundleDto(
    string BundleFileName,
    int ResourceCount);
