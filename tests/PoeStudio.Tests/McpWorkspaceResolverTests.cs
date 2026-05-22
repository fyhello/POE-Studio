using PoeStudio.Mcp;

namespace PoeStudio.Tests;

public sealed class McpWorkspaceResolverTests
{
    [Fact]
    public void Resolve_prefers_workspace_root_argument()
    {
        var resolver = new PoeWorkspaceResolver();
        var environment = new Dictionary<string, string?>
        {
            ["POE_STUDIO_WORKSPACE_ROOT"] = @"C:\FromEnv",
            ["LOCALAPPDATA"] = @"C:\Local"
        };

        var result = resolver.Resolve(["--workspace-root", @"C:\FromArgs"], environment);

        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(@"C:\FromArgs"), result.WorkspaceRoot);
        Assert.Equal("argument", result.Source);
    }

    [Fact]
    public void Resolve_uses_environment_when_argument_is_missing()
    {
        var resolver = new PoeWorkspaceResolver();
        var environment = new Dictionary<string, string?>
        {
            ["POE_STUDIO_WORKSPACE_ROOT"] = @"C:\FromEnv",
            ["LOCALAPPDATA"] = @"C:\Local"
        };

        var result = resolver.Resolve([], environment);

        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(@"C:\FromEnv"), result.WorkspaceRoot);
        Assert.Equal("environment", result.Source);
    }

    [Fact]
    public void Resolve_uses_local_app_data_settings_when_available()
    {
        var root = CreateTempDirectory();
        var localAppData = CreateTempDirectory();
        var settingsRoot = Path.Combine(localAppData, "PoeStudio");
        Directory.CreateDirectory(settingsRoot);
        File.WriteAllText(Path.Combine(settingsRoot, "workspace-settings.json"), $$"""{"workspaceRoot":"{{root.Replace("\\", "\\\\", StringComparison.Ordinal)}}"}""");
        var resolver = new PoeWorkspaceResolver();

        var result = resolver.Resolve([], new Dictionary<string, string?>
        {
            ["LOCALAPPDATA"] = localAppData
        });

        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(root), result.WorkspaceRoot);
        Assert.Equal("local-settings", result.Source);
    }

    [Fact]
    public void Resolve_returns_actionable_failure_when_workspace_is_not_configured()
    {
        var resolver = new PoeWorkspaceResolver();

        var result = resolver.Resolve([], new Dictionary<string, string?>());

        Assert.False(result.Success);
        Assert.Null(result.WorkspaceRoot);
        Assert.Contains("--workspace-root", result.Error);
        Assert.Contains("POE_STUDIO_WORKSPACE_ROOT", result.Error);
        Assert.Contains("workspace-settings.json", result.Error);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "poe-mcp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
