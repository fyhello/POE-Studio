namespace PoeStudio.Contracts;

public sealed record TableInspectRequest(
    string ProfileId,
    string VirtualPath,
    int Limit = 65536,
    string? OodlePath = null);

public sealed record TablePreviewRowDto(
    int RowNumber,
    IReadOnlyList<string> Cells,
    string Raw);

public sealed record TableInspectResponse(
    string ProfileId,
    string VirtualPath,
    string Format,
    bool Structured,
    string? Delimiter,
    int PreviewRowCount,
    IReadOnlyList<TablePreviewRowDto> Rows,
    string? HexPreview,
    IReadOnlyList<string> Warnings);

public sealed record TableCellEditDto(
    int RowNumber,
    int ColumnIndex,
    string Value);

public sealed record TableSaveRequest(
    string ProfileId,
    string VirtualPath,
    IReadOnlyList<TableCellEditDto> Edits,
    string? OodlePath = null);

public sealed record TableSaveResponse(
    string ProfileId,
    string VirtualPath,
    int EditedCells,
    OverlayEntryDto Overlay);
