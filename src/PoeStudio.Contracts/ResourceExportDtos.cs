namespace PoeStudio.Contracts;

public sealed record ResourceExportRequest(
    string ProfileId,
    string VirtualPath,
    string? OodlePath = null);

public sealed record ResourceExportResponse(
    string ProfileId,
    string VirtualPath,
    string FileName,
    string ContentType,
    string Base64Content,
    long Size,
    IReadOnlyList<string> Warnings);
