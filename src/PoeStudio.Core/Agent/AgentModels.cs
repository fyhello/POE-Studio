namespace PoeStudio.Core.Agent;

public enum CodexParsedEventType
{
    Unknown = 0,
    McpToolCall = 1,
    AgentMessage = 2,
    CommandExecution = 3,
    Error = 4,
    FinalMessage = 5,
    StdErr = 6,
    Cancelled = 7
}

public sealed record CodexParsedEvent(
    string RawJson,
    CodexParsedEventType EventType,
    string Message,
    string? PayloadJson,
    bool IsTerminal,
    bool IsToolCall,
    string? ToolName);

public sealed record CodexRunResult(
    int? ExitCode,
    bool Failed,
    bool Cancelled,
    IReadOnlyList<CodexParsedEvent> Events,
    string? StderrSummary,
    string? ErrorCode = null);
