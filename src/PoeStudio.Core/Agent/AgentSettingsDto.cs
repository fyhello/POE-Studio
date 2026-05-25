namespace PoeStudio.Core.Agent;

public sealed record AgentSettingsDto(
    string CodexPath,
    string? Model,
    string? Profile,
    string Sandbox,
    string McpServerName,
    string WorkingDirectory,
    string ApprovalMode,
    string? OodlePath = null,
    bool Memories = false,
    bool Skills = false,
    bool CommandExecution = false);
