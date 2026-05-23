namespace PoeStudio.Contracts;

public enum AgentTaskPlanStatus
{
    Ready = 0,
    NeedsClarification = 1,
    Blocked = 2
}

public sealed record AgentTaskPlanDto(
    AgentTaskPlanStatus Status,
    string RequestedTaskKind,
    string? ResolvedTaskKind,
    string ProfileId,
    string? ResourcePath,
    string Summary,
    IReadOnlyList<string> UserConstraints,
    IReadOnlyList<AgentTaskPlanStepDto> Steps,
    IReadOnlyList<string> RequiredApprovals,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Questions,
    AgentMissingCapabilityProposalDto? MissingCapability);

public sealed record AgentTaskPlanStepDto(
    int Order,
    string Title,
    string Reason,
    IReadOnlyList<string> SuggestedTools);

public sealed record AgentMissingCapabilityProposalDto(
    string CapabilityName,
    string Reason,
    IReadOnlyList<string> RequiredTools,
    IReadOnlyList<string> Risks);

public sealed record AgentPlanGuardResultDto(
    bool Ok,
    string? ErrorCode,
    string? ErrorMessage,
    string? ResolvedTaskKind,
    string ProfileId,
    string? ResourcePath,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Blockers);
