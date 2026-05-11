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

public sealed record ResourceSignatureRequest(
    string ProfileId,
    string VirtualPath,
    string? OodlePath = null);

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

public sealed record ResourceBulkExportRequest(
    string ProfileId,
    string Query,
    ResourceKind? Kind = null,
    string? Extension = null,
    int Take = 200,
    string? OodlePath = null);

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
