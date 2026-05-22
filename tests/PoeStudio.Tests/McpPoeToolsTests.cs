using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Mcp;
using PoeStudio.Storage.Profiles;

namespace PoeStudio.Tests;

public sealed class McpPoeToolsTests
{
    [Fact]
    public async Task Get_workspace_success_returns_root_and_source()
    {
        var root = CreateTempDirectory();
        var registry = McpToolRegistry.CreateDefault(new PoeWorkspaceResolution(true, root, "argument", null));

        var result = await registry.CallToolAsync("poe_get_workspace", EmptyArguments(), CancellationToken.None);
        using var payload = ParsePayload(result);

        Assert.False(result.IsError);
        Assert.Equal(Path.GetFullPath(root), payload.RootElement.GetProperty("workspaceRoot").GetString());
        Assert.Equal("argument", payload.RootElement.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Get_workspace_unresolved_returns_error()
    {
        var registry = McpToolRegistry.CreateDefault(new PoeWorkspaceResolution(false, null, "unresolved", "missing workspace"));

        var result = await registry.CallToolAsync("poe_get_workspace", EmptyArguments(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("missing workspace", result.Content.Single().Text);
    }

    [Fact]
    public async Task List_profiles_returns_array_when_workspace_has_no_profiles()
    {
        var root = CreateTempDirectory();
        var registry = McpToolRegistry.CreateDefault(new PoeWorkspaceResolution(true, root, "argument", null));

        var result = await registry.CallToolAsync("poe_list_profiles", EmptyArguments(), CancellationToken.None);
        using var payload = ParsePayload(result);

        Assert.False(result.IsError);
        Assert.Equal(JsonValueKind.Array, payload.RootElement.GetProperty("profiles").ValueKind);
        Assert.Empty(payload.RootElement.GetProperty("profiles").EnumerateArray());
    }

    [Fact]
    public async Task Get_profile_returns_error_for_missing_profile_id()
    {
        var root = CreateTempDirectory();
        var registry = McpToolRegistry.CreateDefault(new PoeWorkspaceResolution(true, root, "argument", null));

        var result = await registry.CallToolAsync("poe_get_profile", JsonSerializer.SerializeToElement(new { profileId = "missing" }), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("missing", result.Content.Single().Text);
    }

    [Fact]
    public async Task Get_index_status_returns_missing_index_details_and_hint()
    {
        var root = CreateTempDirectory();
        var profile = CreateProfile("profile-1");
        await new ProfileStore(root).SaveAsync(profile, CancellationToken.None);
        var registry = McpToolRegistry.CreateDefault(new PoeWorkspaceResolution(true, root, "argument", null));

        var result = await registry.CallToolAsync("poe_get_index_status", JsonSerializer.SerializeToElement(new { profileId = profile.Id }), CancellationToken.None);
        using var payload = ParsePayload(result);

        Assert.False(result.IsError);
        Assert.False(payload.RootElement.GetProperty("exists").GetBoolean());
        Assert.Equal(profile.Id, payload.RootElement.GetProperty("profileId").GetString());
        Assert.Contains("index", payload.RootElement.GetProperty("hint").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement EmptyArguments()
    {
        return JsonSerializer.SerializeToElement(new { });
    }

    private static JsonDocument ParsePayload(McpToolResult result)
    {
        return JsonDocument.Parse(result.Content.Single().Text);
    }

    private static ClientProfileDto CreateProfile(string id)
    {
        return new ClientProfileDto(
            Id: id,
            DisplayName: "Official",
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Ggpk,
            RootPath: "C:/Game",
            ContentGgpkPath: "C:/Game/Content.ggpk",
            Bundles2Path: null,
            IndexPath: null,
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "abc",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "poe-mcp-tools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
