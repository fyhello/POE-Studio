namespace PoeStudio.Contracts;

public sealed record AgentDiagnosticFindingDto(
    string RunId,
    string Code,
    string Severity,
    string Summary,
    bool ShouldStartDiagnosticRun,
    IReadOnlyList<string> Evidence,
    string RunMode);

public sealed record AgentRepairApproveRequest(
    string RunId,
    string Code);

public sealed record AgentRepairStartResultDto(
    bool Accepted,
    string Message,
    string? RepairRunId);
