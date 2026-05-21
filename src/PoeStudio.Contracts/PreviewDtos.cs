namespace PoeStudio.Contracts;

public enum PreviewKind
{
    Unavailable = 0,
    Text = 1,
    Hex = 2,
    Image = 3,
    Audio = 4,
    Font = 5
}

public sealed record ResourcePreviewRequest(
    string ProfileId,
    string VirtualPath,
    int Limit = 65536,
    string? OodlePath = null,
    bool UseOverlay = true);

public sealed record ResourcePreviewResponse(
    string ProfileId,
    string VirtualPath,
    PreviewKind Kind,
    string? Language,
    string? Text,
    string? Hex,
    string? MediaType,
    string? Base64Content,
    bool Truncated,
    string? ErrorCode,
    string? Message,
    ResourceInspectionDto? Inspection = null,
    bool FromOverlay = false);

public sealed record ResourceInspectionDto(
    string Format,
    string Summary,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<string> Warnings);

public sealed record StructuredTextInspectRequest(
    string ProfileId,
    string VirtualPath,
    string? OodlePath = null,
    bool UseOverlay = true);

public sealed record StructuredTextNodeDto(
    string Key,
    string Value,
    int LineNumber);

public sealed record StructuredTextInspectResponse(
    string ProfileId,
    string VirtualPath,
    string Format,
    int NodeCount,
    IReadOnlyList<StructuredTextNodeDto> Nodes,
    IReadOnlyList<string> Warnings);

public sealed record StructuredTextEditDto(
    string Key,
    string Value,
    int? LineNumber = null);

public sealed record StructuredTextSaveRequest(
    string ProfileId,
    string VirtualPath,
    IReadOnlyList<StructuredTextEditDto> Edits,
    string? OodlePath = null,
    bool UseOverlay = true);

public sealed record StructuredTextSaveResponse(
    string ProfileId,
    string VirtualPath,
    int Edited,
    OverlayEntryDto Overlay,
    IReadOnlyList<string> Warnings);

public sealed record TextChunkRequest(
    string ProfileId,
    string VirtualPath,
    int StartLine = 1,
    int LineCount = 400,
    string? OodlePath = null,
    bool UseOverlay = true);

public sealed record TextChunkResponse(
    string ProfileId,
    string VirtualPath,
    string Text,
    int StartLine,
    int EndLine,
    int LineCount,
    int TotalLines,
    bool HasPrevious,
    bool HasNext,
    string NewLine,
    string? TextEncoding,
    bool FromOverlay);

public sealed record TextChunkSaveRequest(
    string ProfileId,
    string VirtualPath,
    int StartLine,
    int OriginalLineCount,
    string Text,
    string? OodlePath = null,
    bool UseOverlay = true,
    string? TextEncoding = null);

public sealed record TextChunkSaveResponse(
    string ProfileId,
    string VirtualPath,
    int StartLine,
    int EndLine,
    int LineCount,
    int TotalLines,
    string NewLine,
    string? TextEncoding,
    OverlayEntryDto Overlay);
