namespace PoeStudio.Contracts;

public sealed record ResourceExportRequest(
    string ProfileId,
    string VirtualPath,
    string? OodlePath = null,
    bool UseOverlay = true);

public sealed record ResourceExportResponse(
    string ProfileId,
    string VirtualPath,
    string FileName,
    string ContentType,
    string Base64Content,
    long Size,
    IReadOnlyList<string> Warnings);

public sealed record ResourceSignatureRequest(
    string ProfileId,
    string VirtualPath,
    string? OodlePath = null,
    bool UseOverlay = true);

public sealed record ResourceSignatureResponse(
    string ProfileId,
    string VirtualPath,
    ResourceKind Kind,
    string Extension,
    long Size,
    string Sha256,
    string HeaderHex,
    string ContentType,
    string SourceLayer,
    IReadOnlyList<string> MatchHints,
    IReadOnlyList<string> Warnings);

public sealed record ResourceBulkSignatureRequest(
    string ProfileId,
    string Query,
    ResourceKind? Kind = null,
    string? Extension = null,
    int Take = 200,
    string? OodlePath = null,
    bool UseOverlay = true);

public sealed record ResourceBulkSignatureResponse(
    string ProfileId,
    int Matched,
    int Signed,
    IReadOnlyList<ResourceSignatureResponse> Items,
    IReadOnlyList<string> Warnings);

public sealed record ResourceMatchRequest(
    string SourceProfileId,
    string TargetProfileId,
    string Query,
    ResourceKind? Kind = null,
    string? Extension = null,
    int Take = 200,
    string? SourceOodlePath = null,
    string? TargetOodlePath = null,
    bool UseOverlay = true);

public sealed record ResourceMatchItemDto(
    string SourcePath,
    string TargetPath,
    int Score,
    bool PathMatched,
    bool HashMatched,
    bool SizeMatched,
    string SourceSha256,
    string TargetSha256,
    long SourceSize,
    long TargetSize);

public sealed record ResourceMatchResponse(
    string SourceProfileId,
    string TargetProfileId,
    int SourceMatched,
    int TargetMatched,
    int Matched,
    IReadOnlyList<ResourceMatchItemDto> Items,
    IReadOnlyList<string> Warnings);

public enum ResourceMigrationStatus
{
    Direct = 0,
    HashMatch = 1,
    Candidate = 2,
    Missing = 3
}

public sealed record ResourceMigrationPlanRequest(
    string SourceProfileId,
    string TargetProfileId,
    string? Query = null,
    ResourceKind? Kind = null,
    string? Extension = null,
    int Take = 200,
    string? SourceOodlePath = null,
    string? TargetOodlePath = null,
    bool UseOverlay = true);

public sealed record ResourceMigrationPlanItemDto(
    string SourcePath,
    string? TargetPath,
    ResourceMigrationStatus Status,
    PatchRiskLevel RiskLevel,
    ResourceKind Kind,
    string Extension,
    int Score,
    bool PathMatched,
    bool HashMatched,
    bool SizeMatched,
    string SourceSha256,
    string? TargetSha256,
    long SourceSize,
    long? TargetSize,
    IReadOnlyList<string> Hints);

public sealed record ResourceMigrationPlanResponse(
    string SourceProfileId,
    string TargetProfileId,
    int SourceMatched,
    int TargetMatched,
    int Planned,
    IReadOnlyDictionary<ResourceMigrationStatus, int> StatusCounts,
    IReadOnlyDictionary<PatchRiskLevel, int> RiskCounts,
    IReadOnlyList<ResourceMigrationPlanItemDto> Items,
    IReadOnlyList<string> Warnings);

public sealed record ResourceMigrationPlanCriteriaDto(
    string SourceProfileId,
    string TargetProfileId,
    string? Query = null,
    ResourceKind? Kind = null,
    string? Extension = null,
    int Take = 200,
    string? SourceOodlePath = null,
    string? TargetOodlePath = null,
    bool UseOverlay = true);

public sealed record ResourceMigrationPlanSaveRequest(
    string? Id,
    string Name,
    ResourceMigrationPlanCriteriaDto Criteria,
    IReadOnlyList<ResourceMigrationPlanItemDto> Items);

public sealed record ResourceMigrationPlanListRequest(
    string SourceProfileId,
    string? TargetProfileId = null);

public sealed record ResourceMigrationPlanListResponse(
    string SourceProfileId,
    IReadOnlyList<ResourceMigrationPlanEntryDto> Items);

public sealed record ResourceMigrationPlanLoadRequest(
    string SourceProfileId,
    string PlanId);

public sealed record ResourceMigrationPlanDeleteRequest(
    string SourceProfileId,
    string PlanId);

public sealed record ResourceMigrationPlanDeleteResponse(
    string SourceProfileId,
    string PlanId,
    bool Removed);

public sealed record ResourceMigrationPlanApplyRequest(
    string SourceProfileId,
    string PlanId,
    bool IncludeHashMatches = true,
    bool IncludeCandidates = false,
    PatchRiskLevel MaxRiskLevel = PatchRiskLevel.Low,
    bool UseOverlay = true,
    string? SourceOodlePath = null);

public enum ResourceMigrationPlanValidationState
{
    Ready = 0,
    Changed = 1,
    Missing = 2,
    Blocked = 3
}

public sealed record ResourceMigrationPlanValidateRequest(
    string SourceProfileId,
    string PlanId,
    bool UseOverlay = true,
    string? SourceOodlePath = null,
    string? TargetOodlePath = null);

public sealed record ResourceMigrationPlanValidationItemDto(
    string SourcePath,
    string? TargetPath,
    ResourceMigrationPlanValidationState State,
    PatchRiskLevel RiskLevel,
    string Reason,
    string? SavedSourceSha256,
    string? CurrentSourceSha256,
    string? SavedTargetSha256,
    string? CurrentTargetSha256);

public sealed record ResourceMigrationPlanValidateResponse(
    string SourceProfileId,
    string TargetProfileId,
    string PlanId,
    int Checked,
    int Ready,
    int Changed,
    int Missing,
    int Blocked,
    IReadOnlyList<ResourceMigrationPlanValidationItemDto> Items,
    IReadOnlyList<string> Warnings);

public sealed record ResourceMigrationPlanEntryDto(
    string Id,
    string Name,
    ResourceMigrationPlanCriteriaDto Criteria,
    int Planned,
    IReadOnlyDictionary<ResourceMigrationStatus, int> StatusCounts,
    IReadOnlyDictionary<PatchRiskLevel, int> RiskCounts,
    IReadOnlyList<ResourceMigrationPlanItemDto> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ResourceMigrationDraftRequest(
    string SourceProfileId,
    string TargetProfileId,
    string? Query = null,
    ResourceKind? Kind = null,
    string? Extension = null,
    int Take = 200,
    string? SourceOodlePath = null,
    string? TargetOodlePath = null,
    bool UseOverlay = true,
    bool IncludeHashMatches = true,
    bool IncludeCandidates = false,
    PatchRiskLevel MaxRiskLevel = PatchRiskLevel.Low);

public sealed record ResourceMigrationDraftItemDto(
    string SourcePath,
    string TargetPath,
    ResourceMigrationStatus Status,
    PatchRiskLevel RiskLevel,
    long Size,
    string Sha256,
    string OverlayPath);

public sealed record ResourceMigrationDraftSkippedItemDto(
    string SourcePath,
    string? TargetPath,
    ResourceMigrationStatus Status,
    PatchRiskLevel RiskLevel,
    string Reason);

public sealed record ResourceMigrationDraftResponse(
    string SourceProfileId,
    string TargetProfileId,
    int Planned,
    int Drafted,
    int Skipped,
    IReadOnlyList<ResourceMigrationDraftItemDto> Items,
    IReadOnlyList<ResourceMigrationDraftSkippedItemDto> SkippedItems,
    IReadOnlyList<string> Warnings);

public sealed record ResourceMigrationApplyItemRequest(
    string SourceProfileId,
    string TargetProfileId,
    string SourcePath,
    string TargetPath,
    string? SourceOodlePath = null,
    bool UseOverlay = true,
    PatchRiskLevel MaxRiskLevel = PatchRiskLevel.Medium);

public sealed record ResourceMigrationApplyItemResponse(
    string SourceProfileId,
    string TargetProfileId,
    string SourcePath,
    string TargetPath,
    PatchRiskLevel RiskLevel,
    long Size,
    string Sha256,
    string OverlayPath);

public sealed record ResourceBulkExportRequest(
    string ProfileId,
    string Query,
    ResourceKind? Kind = null,
    string? Extension = null,
    int Take = 200,
    string? OodlePath = null,
    bool UseOverlay = true);

public sealed record ResourceBulkExportItemDto(
    string VirtualPath,
    string ExportPath,
    long Size);

public sealed record ResourceBulkExportResponse(
    string ProfileId,
    int Matched,
    int Exported,
    string ExportRoot,
    IReadOnlyList<ResourceBulkExportItemDto> Items,
    IReadOnlyList<string> Warnings);

public sealed record ResourceBulkImportOverlayRequest(
    string ProfileId,
    string ExportRoot,
    int Take = 500);

public sealed record ResourceBulkImportOverlayResponse(
    string ProfileId,
    string ExportRoot,
    int Imported,
    IReadOnlyList<string> ImportedPaths,
    IReadOnlyList<string> Warnings);
