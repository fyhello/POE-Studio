using PoeStudio.Contracts;

namespace PoeStudio.Core.Agent;

public static class AgentCapabilities
{
    public static IReadOnlyList<AgentCapabilityDto> All { get; } =
    [
        new(
            "question",
            "Question",
            AgentCapabilityKind.ReadOnly,
            ["poe_get_workspace", "poe_list_profiles"],
            false,
            "agentQuestionResult"),
        new(
            "read-only-analysis",
            "Read-only analysis",
            AgentCapabilityKind.ReadOnly,
            ["poe_get_workspace", "poe_search_resources", "poe_read_resource"],
            false,
            "agentReadOnlyAnalysisResult"),
        new(
            "datc64-translation",
            "DATC64 translation",
            AgentCapabilityKind.WriteWithApproval,
            ["poe_datc64_extract_translatable_cells", "poe_read_resource"],
            true,
            "datc64TranslationProposal")
    ];

    public static AgentCapabilityDto GetRequired(string taskKind)
    {
        var capability = All.FirstOrDefault(x => string.Equals(x.TaskKind, taskKind, StringComparison.Ordinal));
        return capability ?? throw new ArgumentException("unsupported_task_kind", nameof(taskKind));
    }
}
