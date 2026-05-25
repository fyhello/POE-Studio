namespace PoeStudio.Contracts;

public sealed record AgentTaskFrameDto(
    string? UserGoal,
    string? CurrentState,
    string? Reference,
    string? EditableTarget,
    string? DesiredOutputLanguage,
    string? WriteIntent,
    string? PreferredContext,
    IReadOnlyList<string>? RequiredKnowledge,
    string? ToolFitCheck);

public sealed record AgentCapabilityGapDto(
    string? FailureType,
    string? UserGoal,
    string? MissingCapability,
    string? ProposedNextAction);
