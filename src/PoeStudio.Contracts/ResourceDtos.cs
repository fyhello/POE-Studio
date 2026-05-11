namespace PoeStudio.Contracts;

public enum ResourceKind
{
    Unknown = 0,
    Text = 1,
    Table = 2,
    Image = 3,
    Audio = 4,
    Font = 5,
    Ui = 6,
    Material = 7,
    Model = 8,
    Binary = 9
}

public enum ResourceSourceLayer
{
    Base = 0,
    Patch = 1,
    Overlay = 2,
    Cache = 3
}

public sealed record ResourceSummaryDto(
    string Id,
    string ProfileId,
    string VirtualPath,
    string NormalizedPath,
    string Extension,
    ResourceKind Kind,
    long Size,
    string? PhysicalPath,
    ResourceSourceLayer SourceLayer,
    DateTimeOffset IndexedAt);

public sealed record ResourceIndexBuildRequest(string ProfileId);

public sealed record ResourceIndexBuildResponse(
    string ProfileId,
    int TotalResources,
    DateTimeOffset IndexedAt,
    IReadOnlyList<string> Warnings);

public sealed record ResourceSearchRequest(
    string ProfileId,
    string? Query = null,
    ResourceKind? Kind = null,
    string? Extension = null,
    int Skip = 0,
    int Take = 100);

public sealed record ResourceSearchResponse(
    string ProfileId,
    int Total,
    int Skip,
    int Take,
    IReadOnlyList<ResourceSummaryDto> Items);
