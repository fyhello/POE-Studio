using System.Text;
using PoeStudio.Contracts;

namespace PoeStudio.Core.Agent;

public sealed class AgentPromptBuilder
{
    private const int SummaryMaxLength = 2500;
    private const int ItemMaxLength = 900;

    public string Build(
        AgentSettingsDto settings,
        AgentCapabilityDto capability,
        AgentThreadDto thread,
        IReadOnlyList<AgentMessageDto> messages,
        string goal,
        string? resourcePath)
    {
        return Build(settings, capability, thread, messages, goal, resourcePath, projectContext: null);
    }

    public string Build(
        AgentSettingsDto settings,
        AgentCapabilityDto capability,
        AgentThreadDto thread,
        IReadOnlyList<AgentMessageDto> messages,
        string goal,
        string? resourcePath,
        AgentProjectContextDto? projectContext)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are running inside POE Studio Stage 2 Codex Bridge.");
        builder.AppendLine();
        builder.AppendLine("Execution rules:");
        builder.AppendLine("1. First provide a short plan.");
        builder.AppendLine($"2. Prefer the `{settings.McpServerName}` MCP server for project context.");
        builder.AppendLine("3. Do not call shell commands that write project files.");
        builder.AppendLine("4. Do not write overlay files or modify workspace files directly.");
        builder.AppendLine("5. If this is a write-capable task, output a proposal only. POE Studio backend approval applies it later.");
        builder.AppendLine();
        builder.AppendLine("Task context:");
        builder.AppendLine($"- taskKind: {capability.TaskKind}");
        builder.AppendLine($"- capability: {capability.DisplayName}");
        builder.AppendLine($"- capabilityKind: {capability.Kind}");
        builder.AppendLine($"- requiresApproval: {capability.RequiresApproval}");
        builder.AppendLine($"- profileId: {thread.ProfileId}");
        builder.AppendLine($"- threadId: {thread.Id}");
        builder.AppendLine($"- goal: {goal}");
        if (!string.IsNullOrWhiteSpace(resourcePath))
        {
            builder.AppendLine($"- resourcePath: {resourcePath}");
        }

        AppendProjectContext(builder, projectContext);

        builder.AppendLine();
        builder.AppendLine("Allowed MCP tools:");
        foreach (var tool in capability.RequiredMcpTools)
        {
            builder.AppendLine($"- {settings.McpServerName}.{tool}");
        }

        if (capability.Kind == AgentCapabilityKind.ReadOnly)
        {
            builder.AppendLine();
            builder.AppendLine("This is a read-only task. Do not write overlay, do not create files, and do not request approval.");
        }
        else
        {
            builder.AppendLine();
            builder.AppendLine("This task may propose a write, but you must not write overlay or call any write API.");
        }

        if (string.Equals(capability.TaskKind, "read-only-analysis", StringComparison.Ordinal))
        {
            builder.AppendLine("Check the resource index status before relying on resource search results.");
        }

        AppendHistory(builder, messages);
        AppendOutputContract(builder, capability, thread.ProfileId, resourcePath);
        return builder.ToString();
    }

    private static void AppendProjectContext(StringBuilder builder, AgentProjectContextDto? projectContext)
    {
        builder.AppendLine();
        if (projectContext is null)
        {
            builder.AppendLine("Project context: unavailable");
            return;
        }

        builder.AppendLine("Project context:");
        builder.AppendLine($"- version: {projectContext.Version}");
        builder.AppendLine("- sources:");
        foreach (var source in projectContext.Sources)
        {
            builder.AppendLine($"  - {source.Path}; exists={source.Exists}; hash={source.Hash ?? "none"}");
        }

        builder.AppendLine($"- summary: {Truncate(projectContext.Summary, SummaryMaxLength)}");
        builder.AppendLine("- relevant sections:");
        foreach (var section in projectContext.RelevantSections)
        {
            builder.AppendLine($"  - {section.Key}: {section.Title}: {Truncate(section.Content, ItemMaxLength)}");
        }

        builder.AppendLine("- tool guidance:");
        foreach (var tool in projectContext.ToolGuidance)
        {
            builder.AppendLine($"  - {tool.ToolName}: {Truncate(tool.UseFor + " Limitation: " + tool.Limitation, ItemMaxLength)}");
        }

        builder.AppendLine("- risk boundaries:");
        foreach (var risk in projectContext.RiskBoundaries)
        {
            var approval = risk.RequiresApproval ? "Requires approval" : "No approval required";
            builder.AppendLine($"  - {risk.Action} [{risk.RiskLevel}]: {approval}. {Truncate(risk.Rule, ItemMaxLength)}");
        }

        builder.AppendLine("- unknowns:");
        if (projectContext.Unknowns.Count == 0)
        {
            builder.AppendLine("  - none");
        }
        else
        {
            foreach (var unknown in projectContext.Unknowns)
            {
                builder.AppendLine($"  - {Truncate(unknown, ItemMaxLength)}");
            }
        }
    }

    private static void AppendHistory(StringBuilder builder, IReadOnlyList<AgentMessageDto> messages)
    {
        builder.AppendLine();
        builder.AppendLine("Conversation history:");
        if (messages.Count == 0)
        {
            builder.AppendLine("- none");
            return;
        }

        foreach (var message in messages.OrderBy(x => x.CreatedAt))
        {
            builder.AppendLine($"- {message.Role}: {message.Content}");
        }
    }

    private static void AppendOutputContract(
        StringBuilder builder,
        AgentCapabilityDto capability,
        string profileId,
        string? resourcePath)
    {
        builder.AppendLine();
        builder.AppendLine("Final output:");
        builder.AppendLine($"Return a fenced JSON block using schema `{capability.OutputSchemaName}`.");
        builder.AppendLine("```json");
        if (string.Equals(capability.TaskKind, "datc64-translation", StringComparison.Ordinal))
        {
            builder.AppendLine("{");
            builder.AppendLine("  \"taskKind\": \"datc64-translation\",");
            builder.AppendLine($"  \"profileId\": \"{Escape(profileId)}\",");
            builder.AppendLine($"  \"resourcePath\": \"{Escape(resourcePath ?? string.Empty)}\",");
            builder.AppendLine("  \"candidates\": [");
            builder.AppendLine("    {");
            builder.AppendLine("      \"locator\": \"row:1;column:3;name:text_3 @12\",");
            builder.AppendLine("      \"rowIndex\": 0,");
            builder.AppendLine("      \"columnIndex\": 3,");
            builder.AppendLine("      \"sourceText\": \"NoMana\",");
            builder.AppendLine("      \"translatedText\": \"法力不足\",");
            builder.AppendLine("      \"confidence\": 0.86,");
            builder.AppendLine("      \"notes\": \"game UI prompt text\"");
            builder.AppendLine("    }");
            builder.AppendLine("  ]");
            builder.AppendLine("}");
        }
        else
        {
            builder.AppendLine("{");
            builder.AppendLine($"  \"taskKind\": \"{Escape(capability.TaskKind)}\",");
            builder.AppendLine($"  \"profileId\": \"{Escape(profileId)}\",");
            builder.AppendLine("  \"summary\": \"short answer or analysis\",");
            builder.AppendLine("  \"evidence\": []");
            builder.AppendLine("}");
        }

        builder.AppendLine("```");
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..Math.Max(0, maxLength - 14)].TrimEnd() + " [truncated]";
    }
}
