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
    bool Apply = false,
    bool UseOverlay = true);

public sealed record BatchScriptTemplateSaveRequest(
    string ProfileId,
    string Name,
    IReadOnlyList<BatchScriptOperationDto> Operations,
    string? Id = null,
    string? Description = null);

public sealed record BatchScriptTemplateListRequest(
    string ProfileId);

public sealed record BatchScriptTemplateListResponse(
    string ProfileId,
    IReadOnlyList<BatchScriptTemplateDto> Items);

public sealed record BatchScriptTemplateRunRequest(
    string ProfileId,
    string TemplateId,
    bool Apply = false,
    bool UseOverlay = true);

public sealed record BatchScriptTemplateDeleteRequest(
    string ProfileId,
    string TemplateId);

public sealed record BatchScriptTemplateDeleteResponse(
    string ProfileId,
    string TemplateId,
    bool Removed);

public sealed record BatchScriptTemplateDto(
    string Id,
    string ProfileId,
    string Name,
    string? Description,
    IReadOnlyList<BatchScriptOperationDto> Operations,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

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
