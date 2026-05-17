namespace PoeStudio.Contracts;

public enum PatchRiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2
}

public enum PatchZipTemplate
{
    Official = 0,
    Epic = 1,
    Steam = 2,
    WeGame = 3
}

public enum PatchBuildMode
{
    OverlayBundleMvp = 0,
    NativeBundles2 = 1
}

public enum PatchPackageWriterKind
{
    Mvp = 0,
    NativeBundles2 = 1,
    LibGgpk3Adapter = 2
}

public sealed record PatchDryRunRequest(string ProfileId);

public sealed record PatchBuildRequest(
    string ProfileId,
    PatchZipTemplate Template = PatchZipTemplate.Official,
    string BundleName = "Tiny.V0.1.bundle.bin",
    PatchPackageWriterKind WriterKind = PatchPackageWriterKind.Mvp,
    string? OodlePath = null);

public sealed record PatchChangeDto(
    string VirtualPath,
    string Extension,
    ResourceKind Kind,
    PatchRiskLevel RiskLevel,
    long OverlaySize,
    string OverlayHash,
    string? BaseHash);

public sealed record PatchDryRunResponse(
    string ProfileId,
    int TotalChanges,
    IReadOnlyList<PatchChangeDto> Changes,
    IReadOnlyDictionary<ResourceKind, int> KindCounts,
    IReadOnlyDictionary<PatchRiskLevel, int> RiskCounts,
    IReadOnlyList<string> Warnings);

public sealed record PatchReadinessRequest(
    string ProfileId,
    PatchPackageWriterKind WriterKind = PatchPackageWriterKind.NativeBundles2,
    string? OodlePath = null);

public sealed record PatchReadinessResponse(
    string ProfileId,
    PatchPackageWriterKind WriterKind,
    bool Ready,
    int TotalChanges,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings);

public sealed record NativePatchPlanRequest(
    string ProfileId,
    string BundleName = "PoeStudio.NativePatch.bundle.bin");

public sealed record NativePatchPlanItemDto(
    string VirtualPath,
    string BundleName,
    long Offset,
    long Size,
    string OverlayHash,
    bool RequiresIndexUpdate,
    string? Blocker);

public sealed record NativePatchPlanResponse(
    string ProfileId,
    string BundleName,
    bool Ready,
    int TotalItems,
    IReadOnlyList<NativePatchPlanItemDto> Items,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings);

public sealed record NativeDryBundleBuildRequest(
    string ProfileId,
    string BundleName = "PoeStudio.NativePatch.bundle.bin",
    string? OodlePath = null);

public sealed record NativeDryBundleBuildResponse(
    string ProfileId,
    string BundlePath,
    string ContainerBundlePath,
    string ManifestPath,
    string IndexPlanPath,
    string NativeIndexDryPath,
    string? NativeIndexRewriteDryPath,
    long Size,
    NativePatchPlanResponse Plan,
    NativeIndexRewritePlanResponse IndexPlan,
    IReadOnlyList<string> Warnings);

public sealed record NativeIndexRewritePlanRequest(
    string ProfileId,
    string BundleName = "PoeStudio.NativePatch.bundle.bin");

public sealed record NativeIndexRewriteItemDto(
    string VirtualPath,
    string BundleName,
    long Offset,
    long Size,
    string OverlayHash,
    string? PathHash = null,
    string? OriginalBundleName = null,
    long? OriginalOffset = null,
    long? OriginalSize = null,
    string? Blocker = null);

public sealed record NativeIndexRewritePlanResponse(
    string ProfileId,
    bool Ready,
    int TotalItems,
    IReadOnlyList<NativeIndexRewriteItemDto> Items,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings);

public sealed record PatchBuildResponse(
    string ProfileId,
    PatchBuildMode BuildMode,
    PatchZipTemplate Template,
    string OutputDirectory,
    string IndexPath,
    string BundlePath,
    string ManifestPath,
    string RollbackManifestPath,
    string ZipPath,
    int TotalChanges,
    IReadOnlyList<string> Warnings);

public sealed record PatchManifestDto(
    string ProfileId,
    PatchBuildMode BuildMode,
    PatchZipTemplate Template,
    string BundleName,
    DateTimeOffset BuiltAt,
    IReadOnlyList<PatchChangeDto> Changes,
    IReadOnlyList<string> Warnings);

public sealed record PatchRollbackManifestDto(
    string ProfileId,
    DateTimeOffset BuiltAt,
    IReadOnlyList<PatchRollbackItemDto> Items);

public sealed record PatchRollbackItemDto(
    string VirtualPath,
    string? BaseHash,
    string OverlayHash);

public sealed record PatchBuildHistoryRequest(string ProfileId);

public sealed record PatchImportManifestRequest(
    string ProfileId,
    string BuildId);

public sealed record PatchVerifyRequest(
    string ProfileId,
    string BuildId,
    string BundleName = "PoeStudio.NativePatch.bundle.bin",
    string? OodlePath = null);

public sealed record PatchVerifyResponse(
    string ProfileId,
    string BuildId,
    bool Ok,
    string BundlesDirectory,
    string IndexPath,
    string BundlePath,
    int PatchedFileRecords,
    IReadOnlyList<string> Warnings);

public sealed record PatchZipAnalyzeRequest(
    string ZipPath,
    string BundleName = "PoeStudio.NativePatch.bundle.bin",
    string? OodlePath = null);

public sealed record PatchZipEntryDto(
    string FullName,
    string RelativePath,
    long CompressedSize,
    long Size,
    string Extension,
    ResourceKind Kind,
    PatchRiskLevel RiskLevel);

public sealed record PatchZipAnalyzeResponse(
    string ZipPath,
    bool Ok,
    PatchZipTemplate? Template,
    bool HasBundlesDirectory,
    bool HasIndex,
    bool HasPatchBundle,
    string? BundlesRoot,
    string? IndexEntry,
    string? PatchBundleEntry,
    int EntryCount,
    long TotalSize,
    IReadOnlyList<PatchZipEntryDto> Entries,
    IReadOnlyDictionary<ResourceKind, int> KindCounts,
    IReadOnlyDictionary<PatchRiskLevel, int> RiskCounts,
    PatchVerifyResponse? Verification,
    IReadOnlyList<string> Warnings);

public sealed record PatchZipImportRequest(
    string ProfileId,
    string ZipPath,
    string BundleName = "Tiny.V0.1.bundle.bin",
    string? OodlePath = null);

public sealed record PatchZipImportResponse(
    string ProfileId,
    string BuildId,
    string OutputDirectory,
    string ZipPath,
    string ImportManifestPath,
    PatchZipAnalyzeResponse Analysis,
    IReadOnlyList<string> Warnings);

public sealed record PatchZipImportManifestDto(
    string ProfileId,
    string BuildId,
    string SourceZipPath,
    string ImportedZipPath,
    DateTimeOffset ImportedAt,
    PatchZipAnalyzeResponse Analysis,
    IReadOnlyList<string> Warnings);

public sealed record PatchZipInstallPreviewRequest(
    string ProfileId,
    string ZipPath,
    string BundleName = "Tiny.V0.1.bundle.bin",
    string? OodlePath = null);

public sealed record PatchZipInstallPreviewFileDto(
    string RelativePath,
    string SourceEntry,
    string TargetPath,
    long SourceSize,
    bool TargetExists,
    long? TargetSize,
    bool SameSize,
    bool? SameHash,
    PatchRiskLevel RiskLevel);

public sealed record PatchZipInstallPreviewResponse(
    string ProfileId,
    string ZipPath,
    bool Ok,
    int FileCount,
    int NewFiles,
    int ReplacedFiles,
    int SameFiles,
    int HighRiskFiles,
    IReadOnlyList<PatchZipInstallPreviewFileDto> Files,
    PatchZipAnalyzeResponse Analysis,
    IReadOnlyList<string> Warnings);

public sealed record PatchOverlayDraftRequest(
    string ProfileId,
    string BuildId,
    string BundleName = "Tiny.V0.1.bundle.bin",
    string? OodlePath = null,
    int Take = 200);

public sealed record PatchOverlayDraftItemDto(
    string VirtualPath,
    long Offset,
    long Size,
    string OverlayPath,
    string OverlayHash,
    PatchRiskLevel RiskLevel);

public sealed record PatchOverlayDraftReportDto(
    string ProfileId,
    string BuildId,
    DateTimeOffset GeneratedAt,
    int MatchedRecords,
    int Imported,
    IReadOnlyDictionary<ResourceKind, int> KindCounts,
    IReadOnlyDictionary<PatchRiskLevel, int> RiskCounts,
    IReadOnlyList<PatchOverlayDraftItemDto> Items,
    IReadOnlyList<string> Warnings);

public sealed record PatchOverlayDraftResponse(
    string ProfileId,
    string BuildId,
    int MatchedRecords,
    int Imported,
    IReadOnlyDictionary<ResourceKind, int> KindCounts,
    IReadOnlyDictionary<PatchRiskLevel, int> RiskCounts,
    string DraftReportPath,
    IReadOnlyList<PatchOverlayDraftItemDto> Items,
    IReadOnlyList<string> Warnings);

public sealed record PatchBuildHistoryResponse(
    string ProfileId,
    IReadOnlyList<PatchBuildHistoryItemDto> Items);

public sealed record PatchBuildHistoryItemDto(
    string BuildId,
    string OutputDirectory,
    string? ZipPath,
    string? DownloadUrl,
    string? ManifestPath,
    string? RollbackManifestPath,
    string? ImportManifestPath,
    DateTimeOffset CreatedAt,
    long ZipSize);

public sealed record PatchInstallRequest(
    string ProfileId,
    string BuildId,
    bool Apply = false);

public sealed record PatchInstallFileDto(
    string RelativePath,
    string SourcePath,
    string TargetPath,
    long Size,
    bool TargetExists);

public sealed record PatchInstallResponse(
    string ProfileId,
    string BuildId,
    bool Applied,
    int FileCount,
    IReadOnlyList<PatchInstallFileDto> Files,
    string? InstallManifestPath,
    IReadOnlyList<string> Warnings);

public sealed record PatchUninstallRequest(
    string ProfileId,
    string BuildId,
    bool Apply = false);

public sealed record PatchUninstallResponse(
    string ProfileId,
    string BuildId,
    bool Applied,
    int Removed,
    IReadOnlyList<string> RemovedPaths,
    IReadOnlyList<string> Warnings);

public sealed record PatchSandboxValidateRequest(
    string ProfileId,
    string BuildId,
    string SandboxRootPath);

public sealed record PatchSandboxValidateResponse(
    string ProfileId,
    string BuildId,
    string SandboxBundlesPath,
    bool Ok,
    int CheckedFiles,
    int MissingFiles,
    int SizeMismatches,
    IReadOnlyList<PatchInstallFileDto> Files,
    IReadOnlyList<string> Warnings);

public sealed record PatchSandboxPrepareRequest(
    string ProfileId,
    string BuildId,
    string SandboxRootPath,
    bool Overwrite = true);

public sealed record PatchSandboxPrepareResponse(
    string ProfileId,
    string BuildId,
    string SandboxRootPath,
    string SandboxBundlesPath,
    bool Ok,
    int SeededFiles,
    PatchSandboxValidateResponse Validation,
    IReadOnlyList<string> Warnings);

public sealed record PatchPipelineRunRequest(
    string SourceProfileId,
    string TargetProfileId,
    string MigrationPlanId,
    PatchZipTemplate Template = PatchZipTemplate.Official,
    string BundleName = "Tiny.V0.1.bundle.bin",
    PatchPackageWriterKind WriterKind = PatchPackageWriterKind.Mvp,
    string? OodlePath = null,
    bool IncludeCandidates = false,
    PatchRiskLevel MaxRiskLevel = PatchRiskLevel.Low,
    string? SandboxRootPath = null);

public sealed record PatchPipelineRunResponse(
    string SourceProfileId,
    string TargetProfileId,
    string MigrationPlanId,
    bool Ok,
    ResourceMigrationPlanValidateResponse Validation,
    ResourceMigrationDraftResponse Migration,
    PatchBuildResponse Build,
    PatchSandboxPrepareResponse? Sandbox,
    IReadOnlyList<string> Warnings);
