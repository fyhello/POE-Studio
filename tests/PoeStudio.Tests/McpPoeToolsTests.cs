using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Mcp;
using PoeStudio.Storage.Profiles;
using PoeStudio.Storage.Resources;

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
        var profile = CreateProfile("profile-1", root);
        await new ProfileStore(root).SaveAsync(profile, CancellationToken.None);
        var registry = McpToolRegistry.CreateDefault(new PoeWorkspaceResolution(true, root, "argument", null));

        var result = await registry.CallToolAsync("poe_get_index_status", JsonSerializer.SerializeToElement(new { profileId = profile.Id }), CancellationToken.None);
        using var payload = ParsePayload(result);

        Assert.False(result.IsError);
        Assert.False(payload.RootElement.GetProperty("exists").GetBoolean());
        Assert.Equal(profile.Id, payload.RootElement.GetProperty("profileId").GetString());
        Assert.Contains("index", payload.RootElement.GetProperty("hint").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_resources_returns_query_matches_up_to_limit()
    {
        var root = CreateTempDirectory();
        var profile = CreateProfile("profile-1", root);
        await new ProfileStore(root).SaveAsync(profile, CancellationToken.None);
        await new ResourceIndexStore(root).SaveAsync(profile.Id, [
            Resource(profile.Id, "metadata/items/amulet.ot", ".ot", ResourceKind.Table, Path.Combine(root, "amulet.ot")),
            Resource(profile.Id, "metadata/items/ring.ot", ".ot", ResourceKind.Table, Path.Combine(root, "ring.ot")),
            Resource(profile.Id, "art/textures/icon.dds", ".dds", ResourceKind.Image, Path.Combine(root, "icon.dds"))
        ], [], CancellationToken.None);
        var registry = McpToolRegistry.CreateDefault(new PoeWorkspaceResolution(true, root, "argument", null));

        var result = await registry.CallToolAsync(
            "poe_search_resources",
            JsonSerializer.SerializeToElement(new { profileId = profile.Id, query = "items", limit = 1 }),
            CancellationToken.None);
        using var payload = ParsePayload(result);

        Assert.False(result.IsError);
        Assert.Equal(2, payload.RootElement.GetProperty("total").GetInt32());
        Assert.Single(payload.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Search_resources_rejects_limit_above_one_hundred()
    {
        var root = CreateTempDirectory();
        await new ProfileStore(root).SaveAsync(CreateProfile("profile-1", root), CancellationToken.None);
        var registry = McpToolRegistry.CreateDefault(new PoeWorkspaceResolution(true, root, "argument", null));

        var result = await registry.CallToolAsync(
            "poe_search_resources",
            JsonSerializer.SerializeToElement(new { profileId = "profile-1", limit = 101 }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("limit", result.Content.Single().Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Read_resource_returns_error_when_path_is_not_indexed()
    {
        var root = CreateTempDirectory();
        var profile = CreateProfile("profile-1", root);
        await new ProfileStore(root).SaveAsync(profile, CancellationToken.None);
        var registry = McpToolRegistry.CreateDefault(new PoeWorkspaceResolution(true, root, "argument", null));

        var result = await registry.CallToolAsync(
            "poe_read_resource",
            JsonSerializer.SerializeToElement(new { profileId = profile.Id, resourcePath = "missing.txt" }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("not found", result.Content.Single().Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Read_resource_returns_text_for_text_resource()
    {
        var root = CreateTempDirectory();
        var profile = CreateProfile("profile-1", root);
        var textPath = Path.Combine(root, "files", "hello.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(textPath)!);
        await File.WriteAllTextAsync(textPath, "hello exile");
        await new ProfileStore(root).SaveAsync(profile, CancellationToken.None);
        await new ResourceIndexStore(root).SaveAsync(profile.Id, [
            Resource(profile.Id, "text/hello.txt", ".txt", ResourceKind.Text, textPath)
        ], [], CancellationToken.None);
        var registry = McpToolRegistry.CreateDefault(new PoeWorkspaceResolution(true, root, "argument", null));

        var result = await registry.CallToolAsync(
            "poe_read_resource",
            JsonSerializer.SerializeToElement(new { profileId = profile.Id, resourcePath = "text/hello.txt", maxBytes = 64 }),
            CancellationToken.None);
        using var payload = ParsePayload(result);

        Assert.False(result.IsError);
        Assert.Equal("text", payload.RootElement.GetProperty("encoding").GetString());
        Assert.Equal("hello exile", payload.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Read_resource_returns_binary_summary_and_truncated_flag()
    {
        var root = CreateTempDirectory();
        var profile = CreateProfile("profile-1", root);
        var binaryPath = Path.Combine(root, "files", "icon.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(binaryPath)!);
        await File.WriteAllBytesAsync(binaryPath, [0, 1, 2, 3, 4, 5]);
        await new ProfileStore(root).SaveAsync(profile, CancellationToken.None);
        await new ResourceIndexStore(root).SaveAsync(profile.Id, [
            Resource(profile.Id, "art/icon.bin", ".bin", ResourceKind.Binary, binaryPath)
        ], [], CancellationToken.None);
        var registry = McpToolRegistry.CreateDefault(new PoeWorkspaceResolution(true, root, "argument", null));

        var result = await registry.CallToolAsync(
            "poe_read_resource",
            JsonSerializer.SerializeToElement(new { profileId = profile.Id, resourcePath = "art/icon.bin", maxBytes = 4 }),
            CancellationToken.None);
        using var payload = ParsePayload(result);

        Assert.False(result.IsError);
        Assert.Equal("base64", payload.RootElement.GetProperty("encoding").GetString());
        Assert.True(payload.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal("AAECAw==", payload.RootElement.GetProperty("base64").GetString());
        Assert.Equal("00010203", payload.RootElement.GetProperty("hexPreview").GetString());
    }

    [Fact]
    public async Task Read_resource_returns_native_error_without_fabricated_content()
    {
        var root = CreateTempDirectory();
        var profile = CreateProfile("profile-1", root);
        await new ProfileStore(root).SaveAsync(profile, CancellationToken.None);
        await new ResourceIndexStore(root).SaveAsync(profile.Id, [
            Resource(profile.Id, "metadata/native.datc64", ".datc64", ResourceKind.Table, "native-bundles2://bundle.bin#offset=0&size=12")
        ], [], CancellationToken.None);
        var registry = McpToolRegistry.CreateDefault(new PoeWorkspaceResolution(true, root, "argument", null));

        var result = await registry.CallToolAsync(
            "poe_read_resource",
            JsonSerializer.SerializeToElement(new { profileId = profile.Id, resourcePath = "metadata/native.datc64" }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("native_resource_not_supported_in_stage1", result.Content.Single().Text);
    }

    [Fact]
    public async Task Read_resource_rejects_indexed_physical_path_outside_profile_roots()
    {
        var root = CreateTempDirectory();
        var clientRoot = Path.Combine(root, "client");
        var outsideRoot = Path.Combine(root, "outside");
        var profile = CreateProfile("profile-1", clientRoot);
        var outsidePath = Path.Combine(outsideRoot, "secret.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outsidePath)!);
        await File.WriteAllTextAsync(outsidePath, "do not read");
        await new ProfileStore(root).SaveAsync(profile, CancellationToken.None);
        await new ResourceIndexStore(root).SaveAsync(profile.Id, [
            Resource(profile.Id, "text/safe.txt", ".txt", ResourceKind.Text, outsidePath)
        ], [], CancellationToken.None);
        var registry = McpToolRegistry.CreateDefault(new PoeWorkspaceResolution(true, root, "argument", null));

        var result = await registry.CallToolAsync(
            "poe_read_resource",
            JsonSerializer.SerializeToElement(new { profileId = profile.Id, resourcePath = "text/safe.txt", maxBytes = 64 }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("physical_path_outside_allowed_roots", result.Content.Single().Text);
        Assert.DoesNotContain("do not read", result.Content.Single().Text);
    }

    private static JsonElement EmptyArguments()
    {
        return JsonSerializer.SerializeToElement(new { });
    }

    private static JsonDocument ParsePayload(McpToolResult result)
    {
        return JsonDocument.Parse(result.Content.Single().Text);
    }

    private static ClientProfileDto CreateProfile(string id, string? root = null)
    {
        var rootPath = root ?? "C:/Game";
        return new ClientProfileDto(
            Id: id,
            DisplayName: "Official",
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Ggpk,
            RootPath: rootPath,
            ContentGgpkPath: Path.Combine(rootPath, "Content.ggpk"),
            Bundles2Path: null,
            IndexPath: null,
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "abc",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);
    }

    private static ResourceSummaryDto Resource(
        string profileId,
        string path,
        string extension,
        ResourceKind kind,
        string physicalPath)
    {
        return new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: profileId,
            VirtualPath: path,
            NormalizedPath: path,
            Extension: extension,
            Kind: kind,
            Size: 10,
            PhysicalPath: physicalPath,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "poe-mcp-tools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
