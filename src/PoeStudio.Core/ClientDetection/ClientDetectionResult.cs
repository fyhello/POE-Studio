using PoeStudio.Contracts;

namespace PoeStudio.Core.ClientDetection;

public sealed record ClientDetectionResult(
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
