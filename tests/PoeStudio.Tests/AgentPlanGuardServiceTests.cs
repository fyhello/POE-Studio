using PoeStudio.Contracts;
using PoeStudio.Storage.Agent;
using PoeStudio.Storage.Overlay;
using PoeStudio.Storage.Profiles;
using PoeStudio.Storage.Resources;

namespace PoeStudio.Tests;

public sealed class AgentPlanGuardServiceTests
{
    [Fact]
    public async Task ValidateAsync_accepts_datc64_plan_and_warns_existing_overlay()
    {
        using var workspace = new TemporaryWorkspace();
        var profileId = "profile-target";
        var resourcePath = "data/balance/traditional chinese/activeskills.datc64";
        await SaveProfileAndIndexedResourceAsync(workspace.Root, profileId, resourcePath);
        await new OverlayStore(workspace.Root).SaveBytesAsync(profileId, resourcePath, [1, 2, 3], BasePhysicalPath: null, HasBasePhysicalPath: false, CancellationToken.None);

        var guard = CreateGuard(workspace.Root);
        var plan = ReadyPlan(profileId, "datc64-translation", resourcePath, requiredApprovals: ["overlay_draft"]);
        var oodlePath = Path.Combine(workspace.Root, "oo2core.dll");
        await File.WriteAllBytesAsync(oodlePath, [], CancellationToken.None);

        var result = await guard.ValidateAsync(plan, oodlePath, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("datc64-translation", result.ResolvedTaskKind);
        Assert.Contains(result.Warnings, x => x.Contains("overlay", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_blocks_write_capability_without_required_approval()
    {
        using var workspace = new TemporaryWorkspace();
        var profileId = "profile-target";
        var resourcePath = "metadata/example.datc64";
        await SaveProfileAndIndexedResourceAsync(workspace.Root, profileId, resourcePath);

        var guard = CreateGuard(workspace.Root);
        var plan = ReadyPlan(profileId, "datc64-translation", resourcePath, requiredApprovals: []);

        var result = await guard.ValidateAsync(plan, oodlePath: null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("approval_required", result.ErrorCode);
    }

    private static AgentPlanGuardService CreateGuard(string root)
    {
        return new AgentPlanGuardService(
            new ProfileStore(root),
            new ResourceIndexStore(root),
            new OverlayStore(root));
    }

    private static AgentTaskPlanDto ReadyPlan(
        string profileId,
        string taskKind,
        string resourcePath,
        IReadOnlyList<string> requiredApprovals)
    {
        return new AgentTaskPlanDto(
            AgentTaskPlanStatus.Ready,
            "auto",
            taskKind,
            profileId,
            resourcePath,
            "Plan",
            [],
            [],
            requiredApprovals,
            [],
            [],
            null);
    }

    private static async Task SaveProfileAndIndexedResourceAsync(string root, string profileId, string resourcePath)
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new ClientProfileDto(
            profileId,
            "Target",
            ClientPlatform.Official,
            ClientEntryKind.Ggpk,
            root,
            Path.Combine(root, "Content.ggpk"),
            null,
            null,
            OodleStatus.Found,
            "fingerprint",
            now,
            now);

        await new ProfileStore(root).SaveAsync(profile, CancellationToken.None);
        await new ResourceIndexStore(root).SaveAsync(
            profileId,
            [
                new ResourceSummaryDto(
                    "resource-1",
                    profileId,
                    resourcePath,
                    resourcePath,
                    ".datc64",
                    ResourceKind.Table,
                    10,
                    Path.Combine(root, "base.datc64"),
                    ResourceSourceLayer.Base,
                    now)
            ],
            [],
            CancellationToken.None);
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "poe-studio-plan-guard-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
