namespace PoeStudio.Contracts;

public sealed record SaveTextOverlayRequest(
    string ProfileId,
    string VirtualPath,
    string Text,
    string? BasePhysicalPath = null,
    bool HasBasePhysicalPath = false,
    string? OodlePath = null,
    string? TextEncoding = null);

public sealed record SaveBinaryOverlayRequest(
    string ProfileId,
    string VirtualPath,
    string Base64Content,
    string? BasePhysicalPath = null,
    bool HasBasePhysicalPath = false);

public sealed record OverlayListRequest(string ProfileId);

public sealed record OverlaySyncExternalRequest(string ProfileId);

public sealed record OverlaySyncExternalResponse(
    string ProfileId,
    string Mode,
    int Discovered,
    int Imported,
    int Skipped,
    string OverlayFilesRoot,
    string ManifestPath,
    IReadOnlyList<OverlayEntryDto> Items,
    IReadOnlyList<string> Warnings);

public sealed record OverlayListResponse(
    string ProfileId,
    int Total,
    string OverlayFilesRoot,
    string ManifestPath,
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

public sealed record OverlayReviewRequest(
    string ProfileId,
    int Take = 200,
    int PreviewChars = 240,
    PatchRiskLevel? RiskLevel = null,
    ResourceKind? Kind = null);

public sealed record OverlayReviewItemDto(
    string VirtualPath,
    ResourceKind Kind,
    PatchRiskLevel RiskLevel,
    long OverlaySize,
    long? BaseSize,
    string OverlayHash,
    string? BaseHash,
    bool TextChanged,
    string? BasePreview,
    string? OverlayPreview,
    int ChangedLines,
    IReadOnlyList<string> BaseChangedLines,
    IReadOnlyList<string> OverlayChangedLines,
    string? TextDiff,
    IReadOnlyList<string> Warnings);

public sealed record OverlayReviewResponse(
    string ProfileId,
    int Total,
    int Reviewed,
    IReadOnlyDictionary<PatchRiskLevel, int> RiskCounts,
    IReadOnlyDictionary<ResourceKind, int> KindCounts,
    IReadOnlyList<OverlayReviewItemDto> Items,
    IReadOnlyList<string> Warnings);

public sealed record RevertOverlayRequest(string ProfileId, string VirtualPath);

public sealed record RevertOverlayResponse(
    string ProfileId,
    string VirtualPath,
    bool Removed);

public sealed record OverlayBulkRevertRequest(
    string ProfileId,
    PatchRiskLevel? RiskLevel = null,
    int Take = 500);

public sealed record OverlayBulkRevertResponse(
    string ProfileId,
    int Matched,
    int Removed,
    IReadOnlyList<string> RemovedPaths,
    IReadOnlyList<string> Warnings);

public sealed record OverlayAuditRequest(
    string ProfileId,
    int Take = 100);

public sealed record OverlayAuditEventDto(
    string Action,
    string VirtualPath,
    string? OverlayHash,
    long? OverlaySize,
    DateTimeOffset At);

public sealed record OverlayAuditResponse(
    string ProfileId,
    int Total,
    IReadOnlyList<OverlayAuditEventDto> Items);

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
