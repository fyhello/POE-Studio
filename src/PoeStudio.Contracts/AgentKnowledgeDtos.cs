namespace PoeStudio.Contracts;

public sealed record AgentKnowledgeIndexDto(
    string Version,
    DateTimeOffset UpdatedAt,
    string CoreSectionId,
    IReadOnlyList<AgentKnowledgeSectionIndexDto> Sections);

public sealed record AgentKnowledgeSectionIndexDto(
    string SectionId,
    string Title,
    string Summary,
    string File,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> AppliesWhen,
    int Priority);

public sealed record AgentKnowledgeReadResultDto(
    string Version,
    IReadOnlyList<string> RequestedSectionIds,
    IReadOnlyList<AgentKnowledgeSectionDto> Sections,
    IReadOnlyList<string> MissingSectionIds,
    int TotalBytes);

public sealed record AgentKnowledgeSectionDto(
    string SectionId,
    string Title,
    string Summary,
    string SourceFile,
    string Content,
    bool Truncated);
