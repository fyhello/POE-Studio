using PoeStudio.Core.Workspace;

namespace PoeStudio.Tests;

public sealed class WorkspaceLayoutTests
{
    [Fact]
    public void ForProfile_returns_stable_profile_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-tests", Guid.NewGuid().ToString("N"));

        var layout = WorkspaceLayout.ForProfile(root, "profile-1");

        Assert.Equal(Path.Combine(root, "profiles", "profile-1"), layout.ProfileRoot);
        Assert.Equal(Path.Combine(layout.ProfileRoot, "profile.json"), layout.ProfileJsonPath);
        Assert.Equal(Path.Combine(layout.ProfileRoot, "cache"), layout.CacheRoot);
        Assert.Equal(Path.Combine(layout.ProfileRoot, "overlay"), layout.OverlayRoot);
        Assert.Equal(Path.Combine(layout.ProfileRoot, "builds"), layout.BuildsRoot);
    }
}
