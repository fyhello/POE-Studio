namespace PoeStudio.Contracts;

public sealed record TableInspectRequest(
    string ProfileId,
    string VirtualPath,
    int Limit = 65536,
    string? OodlePath = null,
    TableSchemaDto? Schema = null,
    string? SchemaId = null);

public sealed record TableSchemaDto(
    int RecordSize,
    int HeaderSize,
    IReadOnlyList<TableSchemaFieldDto> Fields);

public sealed record TableSchemaFieldDto(
    string Name,
    int Offset,
    string Type,
    int? Length = null);

public sealed record TableSchemaSaveRequest(
    string ProfileId,
    string VirtualPath,
    string Name,
    TableSchemaDto Schema,
    string? Id = null,
    string? MatchPattern = null);

public sealed record TableSchemaListRequest(
    string ProfileId,
    string? VirtualPath = null);

public sealed record TableSchemaListResponse(
    string ProfileId,
    IReadOnlyList<TableSchemaEntryDto> Items);

public sealed record TableSchemaDeleteRequest(
    string ProfileId,
    string SchemaId);

public sealed record TableSchemaDeleteResponse(
    string ProfileId,
    string SchemaId,
    bool Removed);

public sealed record TableSchemaInferRequest(
    string ProfileId,
    string VirtualPath,
    string? OodlePath = null);

public sealed record TableSchemaInferResponse(
    string ProfileId,
    string VirtualPath,
    string? FormatPath,
    bool Inferred,
    TableSchemaDto? Schema,
    IReadOnlyList<string> Warnings);

public sealed record TableSchemaEntryDto(
    string Id,
    string ProfileId,
    string VirtualPath,
    string Name,
    string? MatchPattern,
    TableSchemaDto Schema,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

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
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string>? HeaderFields = null,
    IReadOnlyList<TableStringCandidateDto>? Strings = null,
    IReadOnlyList<string>? LayoutHints = null,
    IReadOnlyList<string>? Columns = null,
    string? TextEncoding = null,
    IReadOnlyList<int>? EditableColumnIndexes = null);

public sealed record TableStringCandidateDto(
    int Offset,
    int Length,
    string Value,
    string Encoding = "ascii");

public sealed record TableCellEditDto(
    int RowNumber,
    int ColumnIndex,
    string Value);

public sealed record TableSaveRequest(
    string ProfileId,
    string VirtualPath,
    IReadOnlyList<TableCellEditDto> Edits,
    string? OodlePath = null,
    TableSchemaDto? Schema = null,
    string? SchemaId = null);

public sealed record TableSaveResponse(
    string ProfileId,
    string VirtualPath,
    int EditedCells,
    OverlayEntryDto Overlay);

public sealed record TableCsvExportRequest(
    string ProfileId,
    string VirtualPath,
    string? OodlePath = null,
    TableSchemaDto? Schema = null,
    string? SchemaId = null);

public sealed record TableCsvExportResponse(
    string ProfileId,
    string VirtualPath,
    int Rows,
    IReadOnlyList<string> Columns,
    string Csv,
    IReadOnlyList<string> Warnings);

public sealed record TableCsvImportRequest(
    string ProfileId,
    string VirtualPath,
    string Csv,
    string? OodlePath = null,
    TableSchemaDto? Schema = null,
    string? SchemaId = null);

public sealed record TableCsvImportResponse(
    string ProfileId,
    string VirtualPath,
    int Rows,
    int EditedCells,
    OverlayEntryDto Overlay,
    IReadOnlyList<string> Warnings);

public sealed record TableReferenceScanRequest(
    string ProfileId,
    string VirtualPath,
    int ColumnIndex,
    string TargetVirtualPath,
    int TargetColumnIndex,
    string? OodlePath = null,
    TableSchemaDto? Schema = null,
    string? SchemaId = null,
    TableSchemaDto? TargetSchema = null,
    string? TargetSchemaId = null);

public sealed record TableReferenceScanResponse(
    string ProfileId,
    string VirtualPath,
    string TargetVirtualPath,
    int SourceRows,
    int Matched,
    int Missing,
    IReadOnlyList<TableReferenceItemDto> Items,
    IReadOnlyList<string> Warnings);

public sealed record TableReferenceItemDto(
    int RowNumber,
    string Value,
    bool Matched);
