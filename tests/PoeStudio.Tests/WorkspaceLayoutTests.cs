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

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("C:\\escape")]
    [InlineData("profile/escape")]
    [InlineData("profile\\escape")]
    public void ForProfile_rejects_unsafe_profile_ids(string profileId)
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-tests", Guid.NewGuid().ToString("N"));

        Assert.Throws<ArgumentException>("profileId", () => WorkspaceLayout.ForProfile(root, profileId));
    }
}
