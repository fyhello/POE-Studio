namespace PoeStudio.Contracts;

public sealed record ClientProfileDto(
    string Id,
    string DisplayName,
    ClientPlatform Platform,
    ClientEntryKind EntryKind,
    string RootPath,
    string? ContentGgpkPath,
    string? Bundles2Path,
    string? IndexPath,
    OodleStatus OodleStatus,
    string ClientFingerprint,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DetectClientRequest(string RootPath, string? OodleSearchPath = null);

public sealed record DetectClientResponse(
    bool Detected,
    ClientPlatform Platform,
    ClientEntryKind EntryKind,
    string RootPath,
    string? ContentGgpkPath,
    string? Bundles2Path,
    string? IndexPath,
    OodleStatus OodleStatus,
    string? OodlePath,
    string ClientFingerprint,
    IReadOnlyList<string> Warnings);

public sealed record CreateProfileRequest(
    string DisplayName,
    string RootPath,
    ClientPlatform Platform,
    ClientEntryKind EntryKind,
    string? ContentGgpkPath,
    string? Bundles2Path,
    string? IndexPath,
    string ClientFingerprint);
