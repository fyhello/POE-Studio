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
    string IndexPath);

public sealed record NativeIndexDecompressResponse(
    string ProfileId,
    string IndexPath,
    string CachePath,
    bool Ok,
    NativeIndexDecompressStatus Status,
    long? UncompressedSize,
    int? ChunkCount,
    IReadOnlyList<string> Warnings);
