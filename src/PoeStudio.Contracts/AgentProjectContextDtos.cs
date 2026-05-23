namespace PoeStudio.Contracts;

public sealed record AgentProjectContextDto(
    string Version,
    IReadOnlyList<AgentProjectContextSourceDto> Sources,
    string Summary,
    IReadOnlyList<AgentProjectContextSectionDto> RelevantSections,
    IReadOnlyList<AgentToolGuidanceDto> ToolGuidance,
    IReadOnlyList<AgentRiskBoundaryDto> RiskBoundaries,
    IReadOnlyList<string> Unknowns);

public sealed record AgentProjectContextSourceDto(
    string Path,
    bool Exists,
    string? Hash,
    DateTimeOffset? LastModifiedAt);

public sealed record AgentProjectContextSectionDto(
    string Key,
    string Title,
    string Content);

public sealed record AgentToolGuidanceDto(
    string ToolName,
    string UseFor,
    string Limitation);

public sealed record AgentRiskBoundaryDto(
    string Action,
    string RiskLevel,
    bool RequiresApproval,
    string Rule);

public sealed record AgentProjectPreflightDto(
    string ThreadId,
    string RunId,
    string ProfileId,
    string TaskKind,
    string Goal,
    string? ResourcePath,
    bool ProjectContextLoaded,
    string? RepositoryRoot,
    IReadOnlyList<AgentProjectContextSourceDto> Sources,
    string Summary,
    IReadOnlyList<string> RequiredChecks,
    IReadOnlyList<string> Warnings);
