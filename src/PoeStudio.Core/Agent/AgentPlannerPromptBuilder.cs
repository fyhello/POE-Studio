using System.Text;
using PoeStudio.Contracts;

namespace PoeStudio.Core.Agent;

public sealed class AgentPlannerPromptBuilder
{
    private const int SummaryMaxLength = 2000;
    private const int ItemMaxLength = 700;
    private const int GoalMaxLength = 1200;

    public string Build(
        AgentSettingsDto settings,
        AgentThreadDto thread,
        IReadOnlyList<AgentMessageDto> messages,
        string goal,
        string? selectedResourcePath,
        IReadOnlyList<AgentRunDto> recentRuns,
        IReadOnlyList<AgentCapabilityDto> capabilities,
        AgentProjectContextDto? projectContext)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are the planning brain for POE Studio Agent.");
        builder.AppendLine("Do not execute the task in this planner pass.");
        builder.AppendLine("Your job is to understand the user's natural-language request, decide what kind of work is needed, and produce a structured plan.");
        builder.AppendLine("POE Studio will validate your plan before execution.");
        builder.AppendLine();
        builder.AppendLine("Planner boundaries:");
        builder.AppendLine("- Do not call tools.");
        builder.AppendLine("- Do not write files, overlays, resources, or project code.");
        builder.AppendLine("- Do not claim the task is complete.");
        builder.AppendLine("- If required context is missing, return needs_clarification instead of guessing.");
        builder.AppendLine("- If no approved capability can do the work, return blocked with missingCapability.");
        builder.AppendLine();
        builder.AppendLine("Current request:");
        builder.AppendLine($"- requestedTaskKind: {thread.TaskKind}");
        builder.AppendLine($"- threadId: {thread.Id}");
        builder.AppendLine($"- profileId: {thread.ProfileId}");
        builder.AppendLine($"- userGoal: {Truncate(goal, GoalMaxLength)}");
        builder.AppendLine($"- selectedResourcePath: {selectedResourcePath ?? "none"}");
        builder.AppendLine($"- mcpServerName: {settings.McpServerName}");
        builder.AppendLine($"- workingDirectory: {settings.WorkingDirectory}");

        AppendMessages(builder, messages);
        AppendRecentRuns(builder, recentRuns);
        AppendCapabilities(builder, settings, capabilities);
        AppendProjectContext(builder, projectContext);
        AppendSchema(builder, thread.ProfileId, selectedResourcePath);

        return builder.ToString();
    }

    private static void AppendMessages(StringBuilder builder, IReadOnlyList<AgentMessageDto> messages)
    {
        builder.AppendLine();
        builder.AppendLine("Recent conversation messages:");
        if (messages.Count == 0)
        {
            builder.AppendLine("- none");
            return;
        }

        foreach (var message in messages.OrderBy(x => x.CreatedAt).TakeLast(8))
        {
            builder.AppendLine($"- {message.Role}: {Truncate(message.Content, ItemMaxLength)}");
        }
    }

    private static void AppendRecentRuns(StringBuilder builder, IReadOnlyList<AgentRunDto> recentRuns)
    {
        builder.AppendLine();
        builder.AppendLine("Recent runs:");
        if (recentRuns.Count == 0)
        {
            builder.AppendLine("- none");
            return;
        }

        foreach (var run in recentRuns.OrderByDescending(x => x.CreatedAt).Take(5))
        {
            builder.AppendLine($"- runId: {run.Id}; taskKind: {run.TaskKind}; resolvedTaskKind: {run.ResolvedTaskKind ?? "none"}; status: {run.Status}; resourcePath: {run.ResourcePath ?? "none"}");
        }
    }

    private static void AppendCapabilities(
        StringBuilder builder,
        AgentSettingsDto settings,
        IReadOnlyList<AgentCapabilityDto> capabilities)
    {
        builder.AppendLine();
        builder.AppendLine("Available executable capabilities:");
        foreach (var capability in capabilities)
        {
            builder.AppendLine($"- {capability.TaskKind}: {capability.DisplayName}; kind={capability.Kind}; requiresApproval={capability.RequiresApproval}");
            builder.AppendLine($"  tools: {string.Join(", ", capability.RequiredMcpTools.Select(x => settings.McpServerName + "." + x))}");
            builder.AppendLine($"  outputSchema: {capability.OutputSchemaName}");
        }
    }

    private static void AppendProjectContext(StringBuilder builder, AgentProjectContextDto? projectContext)
    {
        builder.AppendLine();
        if (projectContext is null)
        {
            builder.AppendLine("Project context summary: unavailable");
            return;
        }

        builder.AppendLine("Project context summary:");
        builder.AppendLine($"- version: {projectContext.Version}");
        builder.AppendLine($"- summary: {Truncate(projectContext.Summary, SummaryMaxLength)}");
        builder.AppendLine("- relevant sections:");
        foreach (var section in projectContext.RelevantSections.Take(8))
        {
            builder.AppendLine($"  - {section.Key}: {section.Title}: {Truncate(section.Content, ItemMaxLength)}");
        }
        builder.AppendLine("- risk boundaries:");
        foreach (var risk in projectContext.RiskBoundaries.Take(8))
        {
            var approval = risk.RequiresApproval ? "requires approval" : "no approval required";
            builder.AppendLine($"  - {risk.Action} [{risk.RiskLevel}, {approval}]: {Truncate(risk.Rule, ItemMaxLength)}");
        }
    }

    private static void AppendSchema(StringBuilder builder, string profileId, string? selectedResourcePath)
    {
        builder.AppendLine();
        builder.AppendLine("Return only a fenced JSON block using this schema. No prose outside the fence.");
        builder.AppendLine("```json");
        builder.AppendLine("{");
        builder.AppendLine("  \"status\": \"ready | needs_clarification | blocked\",");
        builder.AppendLine("  \"requestedTaskKind\": \"auto\",");
        builder.AppendLine("  \"resolvedTaskKind\": \"question | read-only-analysis | datc64-translation | null\",");
        builder.AppendLine($"  \"profileId\": \"{Escape(profileId)}\",");
        builder.AppendLine($"  \"resourcePath\": \"{Escape(selectedResourcePath ?? string.Empty)}\",");
        builder.AppendLine("  \"summary\": \"short planning summary\",");
        builder.AppendLine("  \"userConstraints\": [],");
        builder.AppendLine("  \"steps\": [");
        builder.AppendLine("    {");
        builder.AppendLine("      \"order\": 1,");
        builder.AppendLine("      \"title\": \"Understand required context\",");
        builder.AppendLine("      \"reason\": \"Why this step is necessary\",");
        builder.AppendLine("      \"suggestedTools\": []");
        builder.AppendLine("    }");
        builder.AppendLine("  ],");
        builder.AppendLine("  \"requiredApprovals\": [],");
        builder.AppendLine("  \"warnings\": [],");
        builder.AppendLine("  \"questions\": [],");
        builder.AppendLine("  \"missingCapability\": null");
        builder.AppendLine("}");
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
