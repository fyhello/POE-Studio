using PoeStudio.Contracts;
using PoeStudio.Core.Agent;
using PoeStudio.Core.Resources;
using PoeStudio.Storage.Overlay;
using PoeStudio.Storage.Profiles;
using PoeStudio.Storage.Resources;

namespace PoeStudio.Storage.Agent;

public sealed class AgentPlanGuardService
{
    private readonly ProfileStore profileStore;
    private readonly ResourceIndexStore resourceIndexStore;
    private readonly OverlayStore overlayStore;

    public AgentPlanGuardService(
        ProfileStore profileStore,
        ResourceIndexStore resourceIndexStore,
        OverlayStore overlayStore)
    {
        this.profileStore = profileStore;
        this.resourceIndexStore = resourceIndexStore;
        this.overlayStore = overlayStore;
    }

    public async Task<AgentPlanGuardResultDto> ValidateAsync(
        AgentTaskPlanDto plan,
        string? oodlePath,
        CancellationToken cancellationToken)
    {
        if (plan.Status == AgentTaskPlanStatus.NeedsClarification)
        {
            return Blocked("needs_clarification", "Planner needs more input.", plan, plan.Questions);
        }

        if (plan.Status == AgentTaskPlanStatus.Blocked)
        {
            return Blocked("planner_blocked", plan.Summary, plan, plan.Warnings);
        }

        var blockers = new List<string>();
        var warnings = new List<string>(plan.Warnings);
        var resolvedTaskKind = plan.ResolvedTaskKind;
        if (string.IsNullOrWhiteSpace(resolvedTaskKind)
            || !AgentTaskKindPolicy.IsExecutableTaskKind(resolvedTaskKind))
        {
            blockers.Add("Resolved taskKind is not an approved executable capability.");
            return Blocked("unsupported_task_kind", "Planner resolved an unsupported task kind.", plan, blockers);
        }

        var capability = AgentCapabilities.GetRequired(resolvedTaskKind);
        if (await profileStore.GetAsync(plan.ProfileId, cancellationToken) is null)
        {
            blockers.Add($"Profile not found: {plan.ProfileId}");
            return Blocked("profile_not_found", "Planner selected a missing profile.", plan, blockers);
        }

        if (capability.Kind == AgentCapabilityKind.ReadOnly && plan.RequiredApprovals.Count > 0)
        {
            blockers.Add("Read-only capability cannot request approvals.");
            return Blocked("unexpected_approval", "Planner requested approval for a read-only task.", plan, blockers);
        }

        if (string.Equals(resolvedTaskKind, "datc64-translation", StringComparison.Ordinal))
        {
            var datc64Result = await ValidateDatc64Async(plan, oodlePath, warnings, cancellationToken);
            if (datc64Result is not null)
            {
                return datc64Result;
            }
        }

        return new AgentPlanGuardResultDto(
            true,
            null,
            null,
            resolvedTaskKind,
            plan.ProfileId,
            plan.ResourcePath,
            warnings,
            []);
    }

    private async Task<AgentPlanGuardResultDto?> ValidateDatc64Async(
        AgentTaskPlanDto plan,
        string? oodlePath,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(plan.ResourcePath)
            || !plan.ResourcePath.EndsWith(".datc64", StringComparison.OrdinalIgnoreCase))
        {
            return Blocked("resource_required", "DATC64 translation requires a .datc64 resource.", plan, ["Missing or invalid .datc64 resourcePath."]);
        }

        var normalized = ResourcePath.Normalize(plan.ResourcePath);
        if (await resourceIndexStore.GetByPathAsync(plan.ProfileId, normalized, cancellationToken) is null)
        {
            return Blocked("resource_not_found", "Planner selected a resource that is not in the index.", plan, [$"Resource not indexed: {normalized}"]);
        }

        if (!plan.RequiredApprovals.Any(x => string.Equals(x, "overlay_draft", StringComparison.Ordinal)))
        {
            return Blocked("approval_required", "DATC64 translation requires overlay draft approval.", plan, ["Missing required approval: overlay_draft"]);
        }

        if (!string.IsNullOrWhiteSpace(oodlePath) && !File.Exists(oodlePath))
        {
            return Blocked("invalid_oodle_path", "Configured Oodle path does not exist.", plan, [$"Oodle path not found: {oodlePath}"]);
        }

        if (string.IsNullOrWhiteSpace(oodlePath))
        {
            warnings.Add("Oodle path is not configured; execution may fail if DATC64 decoding requires it.");
        }

        var overlays = await overlayStore.ListAsync(plan.ProfileId, cancellationToken);
        if (overlays.Items.Any(x => string.Equals(x.NormalizedPath, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add($"Existing overlay detected for {normalized}; execution should account for current draft state.");
        }

        return null;
    }

    private static AgentPlanGuardResultDto Blocked(
        string errorCode,
        string? errorMessage,
        AgentTaskPlanDto plan,
        IReadOnlyList<string> blockers)
    {
        return new AgentPlanGuardResultDto(
            false,
            errorCode,
            errorMessage,
            plan.ResolvedTaskKind,
            plan.ProfileId,
            plan.ResourcePath,
            plan.Warnings,
            blockers);
    }
}
