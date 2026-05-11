namespace PoeStudio.Contracts;

public sealed record SaveTextOverlayRequest(
    string ProfileId,
    string VirtualPath,
    string Text,
    string? BasePhysicalPath = null,
    bool HasBasePhysicalPath = false);

public sealed record OverlayListRequest(string ProfileId);

public sealed record OverlayListResponse(
    string ProfileId,
    int Total,
    IReadOnlyList<OverlayEntryDto> Items);

public sealed record OverlayEntryDto(
    string ProfileId,
    string VirtualPath,
    string NormalizedPath,
    string OverlayPath,
    long OverlaySize,
    string OverlayHash,
    string? BaseHash,
    long? BaseSize,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record OverlayDiffRequest(string ProfileId, string VirtualPath);

public sealed record OverlayDiffResponse(
    string ProfileId,
    string VirtualPath,
    bool Exists,
    long? BaseSize,
    long? OverlaySize,
    string? BaseHash,
    string? OverlayHash,
    bool TextChanged,
    string? Message);

public sealed record RevertOverlayRequest(string ProfileId, string VirtualPath);

public sealed record RevertOverlayResponse(
    string ProfileId,
    string VirtualPath,
    bool Removed);

public sealed record BatchSaveTextOverlayRequest(
    string ProfileId,
    string Query,
    string Text,
    int Take = 50);

public sealed record BatchSaveTextOverlayResponse(
    string ProfileId,
    int Matched,
    int Saved,
    IReadOnlyList<string> SavedPaths,
    IReadOnlyList<string> Warnings);

public sealed record BatchReplaceTextOverlayRequest(
    string ProfileId,
    string Query,
    string Find,
    string Replace,
    int Take = 50);

public sealed record BatchReplaceTextOverlayResponse(
    string ProfileId,
    int Matched,
    int Changed,
    IReadOnlyList<string> ChangedPaths,
    IReadOnlyList<string> Warnings);
