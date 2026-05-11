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
