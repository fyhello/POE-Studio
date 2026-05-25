namespace PoeStudio.Contracts;

public sealed record AgentRunStartedDto(
    string RunId,
    DateTimeOffset StartedAt,
    string Mode,
    string Message);

public sealed record AgentRunTraceEventDto(
    string EventName,
    string Status,
    string DataJson,
    DateTimeOffset CreatedAt);

public static class AgentRunModes
{
    public const string Normal = "normal";
    public const string Diagnostic = "diagnostic";
    public const string Repair = "repair";
}
