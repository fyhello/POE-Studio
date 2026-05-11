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
    PatchPackageWriterKind WriterKind = PatchPackageWriterKind.Mvp);

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
