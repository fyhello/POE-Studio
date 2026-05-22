namespace PoeStudio.Contracts;

public enum AgentThreadStatus
{
    Active = 0,
    Archived = 1
}

public enum AgentRunStatus
{
    Queued = 0,
    Running = 1,
    WaitingForApproval = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
    Rejected = 6
}

public enum AgentEventType
{
    RunCreated = 0,
    PlanUpdated = 1,
    CodexStdout = 2,
    CodexStderr = 3,
    McpToolCall = 4,
    AgentMessage = 5,
    ApprovalRequested = 6,
    ApprovalApproved = 7,
    ApprovalRejected = 8,
    OverlayDraftWritten = 9,
    RunFailed = 10,
    RunCancelled = 11
}

public enum AgentApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Applied = 3
}

public enum AgentMessageRole
{
    User = 0,
    Assistant = 1,
    System = 2
}

public enum AgentCapabilityKind
{
    ReadOnly = 0,
    WriteWithApproval = 1
}

public sealed record AgentSettingsDto(
    string CodexPath,
    string? Model,
    string? Profile,
    string Sandbox,
    string McpServerName,
    string WorkingDirectory,
    string ApprovalMode);

public sealed record AgentThreadDto(
    string Id,
    string ProfileId,
    string Title,
    string Goal,
    string TaskKind,
    AgentThreadStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AgentMessageDto(
    string Id,
    string ThreadId,
    AgentMessageRole Role,
    string Content,
    string? PayloadJson,
    DateTimeOffset CreatedAt);

public sealed record AgentRunDto(
    string Id,
    string ThreadId,
    string ProfileId,
    string Goal,
    string TaskKind,
    AgentRunStatus Status,
    int ProgressPercent,
    string Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int EventCount,
    string? ErrorCode,
    string? ErrorMessage,
    string? ResultJson);

public sealed record AgentEventDto(
    string Id,
    string RunId,
    long Sequence,
    AgentEventType Type,
    string Message,
    string? PayloadJson,
    DateTimeOffset CreatedAt);

public sealed record AgentPlanStepDto(
    string Id,
    string RunId,
    int Order,
    string Title,
    string Status,
    string? Evidence);

public sealed record AgentCapabilityDto(
    string TaskKind,
    string DisplayName,
    AgentCapabilityKind Kind,
    IReadOnlyList<string> RequiredMcpTools,
    bool RequiresApproval,
    string OutputSchemaName);

public sealed record AgentApprovalDto(
    string Id,
    string RunId,
    string ProfileId,
    string Kind,
    AgentApprovalStatus Status,
    string Summary,
    string ProposalJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? AppliedOverlayPath);
