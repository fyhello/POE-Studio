namespace PoeStudio.Contracts;

public sealed record AgentCurrentViewRequestDto(
    string Kind,
    AgentCurrentTableViewDto? Table = null);

public sealed record AgentCurrentTableViewDto(
    string ProfileId,
    string ResourcePath,
    string? SourceProfileId,
    string? SourceResourcePath,
    string? TargetProfileId,
    string? TargetResourcePath,
    string Delimiter,
    int RowCount,
    int PreviewRowCount,
    IReadOnlyList<string> Columns,
    IReadOnlyList<int> EditableColumnIndexes,
    IReadOnlyList<AgentCurrentTableRowDto> TargetRows,
    IReadOnlyList<AgentCurrentTableRowDto>? SourceRows,
    string? ReferenceMatchMode);

public sealed record AgentCurrentTableRowDto(
    int RowNumber,
    IReadOnlyList<string> Cells);

public sealed record AgentCurrentViewSnapshotDto(
    string ContextId,
    DateTimeOffset CreatedAt,
    AgentCurrentViewRequestDto View);

public sealed record AgentUntranslatedCellDto(
    int RowNumber,
    int ColumnIndex,
    string? ColumnName,
    string SourceText,
    string TargetText,
    string Reason);
