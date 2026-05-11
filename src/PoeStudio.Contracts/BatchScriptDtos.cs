namespace PoeStudio.Contracts;

public sealed record BatchScriptOperationDto(
    string Name,
    string Query,
    string Find,
    string Replace,
    ResourceKind? Kind = null,
    string? Extension = null,
    int Take = 100);

public sealed record BatchScriptRunRequest(
    string ProfileId,
    IReadOnlyList<BatchScriptOperationDto> Operations,
    bool Apply = false);

public sealed record BatchScriptChangeDto(
    string OperationName,
    string VirtualPath,
    string BeforePreview,
    string AfterPreview);

public sealed record BatchScriptRunResponse(
    string ProfileId,
    bool Applied,
    int Matched,
    int Changed,
    IReadOnlyList<BatchScriptChangeDto> Changes,
    IReadOnlyList<string> Warnings);
