using System.Text;
using System.Text.Json;
using PoeStudio.Mcp;

namespace PoeStudio.Tests;

public sealed class McpWriteToolsBehaviorTests
{
    [Fact]
    public async Task Write_overlay_text_appears_in_list()
    {
        var tempDir = CreateTempDirectory();
        var registry = CreateRegistry(tempDir);

        var writeResult = await registry.CallToolAsync("poe_write_overlay_text", Arguments(
            ("profileId", "test-profile"),
            ("resourcePath", "metadata/items/amulet.ot"),
            ("text", "translated text content")
        ), CancellationToken.None);

        Assert.False(writeResult.IsError);
        using var writePayload = JsonDocument.Parse(writeResult.Content.Single().Text);
        Assert.Equal("test-profile", writePayload.RootElement.GetProperty("profileId").GetString());
        Assert.Equal("metadata/items/amulet.ot", writePayload.RootElement.GetProperty("resourcePath").GetString());

        var listResult = await registry.CallToolAsync("poe_list_overlays", Arguments(
            ("profileId", "test-profile")
        ), CancellationToken.None);

        Assert.False(listResult.IsError);
        using var listPayload = JsonDocument.Parse(listResult.Content.Single().Text);
        Assert.Equal("test-profile", listPayload.RootElement.GetProperty("profileId").GetString());
        Assert.Equal(1, listPayload.RootElement.GetProperty("total").GetInt32());

        var items = listPayload.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Single(items);
        Assert.Equal("metadata/items/amulet.ot", items[0].GetProperty("virtualPath").GetString());
    }

    [Fact]
    public async Task Write_overlay_binary_appears_in_list()
    {
        var tempDir = CreateTempDirectory();
        var registry = CreateRegistry(tempDir);

        var writeResult = await registry.CallToolAsync("poe_write_overlay_binary", Arguments(
            ("profileId", "test-profile"),
            ("resourcePath", "metadata/items/sword.ot"),
            ("base64", Convert.ToBase64String("binary content"u8))
        ), CancellationToken.None);

        Assert.False(writeResult.IsError);
        using var writePayload = JsonDocument.Parse(writeResult.Content.Single().Text);
        Assert.Equal("test-profile", writePayload.RootElement.GetProperty("profileId").GetString());
        Assert.Equal("metadata/items/sword.ot", writePayload.RootElement.GetProperty("resourcePath").GetString());

        var listResult = await registry.CallToolAsync("poe_list_overlays", Arguments(
            ("profileId", "test-profile")
        ), CancellationToken.None);

        Assert.False(listResult.IsError);
        using var listPayload = JsonDocument.Parse(listResult.Content.Single().Text);
        Assert.Equal(1, listPayload.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task List_overlays_returns_all_written_overlays()
    {
        var tempDir = CreateTempDirectory();
        var registry = CreateRegistry(tempDir);

        await registry.CallToolAsync("poe_write_overlay_text", Arguments(
            ("profileId", "multi-profile"),
            ("resourcePath", "a.txt"),
            ("text", "first")
        ), CancellationToken.None);

        await registry.CallToolAsync("poe_write_overlay_text", Arguments(
            ("profileId", "multi-profile"),
            ("resourcePath", "b.txt"),
            ("text", "second")
        ), CancellationToken.None);

        var listResult = await registry.CallToolAsync("poe_list_overlays", Arguments(
            ("profileId", "multi-profile")
        ), CancellationToken.None);

        Assert.False(listResult.IsError);
        using var listPayload = JsonDocument.Parse(listResult.Content.Single().Text);
        Assert.Equal(2, listPayload.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Revert_overlay_removes_it_from_list()
    {
        var tempDir = CreateTempDirectory();
        var registry = CreateRegistry(tempDir);

        await registry.CallToolAsync("poe_write_overlay_text", Arguments(
            ("profileId", "revert-profile"),
            ("resourcePath", "metadata/items/ring.ot"),
            ("text", "to be reverted")
        ), CancellationToken.None);

        var revertResult = await registry.CallToolAsync("poe_revert_overlay", Arguments(
            ("profileId", "revert-profile"),
            ("resourcePath", "metadata/items/ring.ot")
        ), CancellationToken.None);

        Assert.False(revertResult.IsError);
        using var revertPayload = JsonDocument.Parse(revertResult.Content.Single().Text);
        Assert.True(revertPayload.RootElement.GetProperty("removed").GetBoolean());

        var listResult = await registry.CallToolAsync("poe_list_overlays", Arguments(
            ("profileId", "revert-profile")
        ), CancellationToken.None);

        using var listPayload = JsonDocument.Parse(listResult.Content.Single().Text);
        Assert.Equal(0, listPayload.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Write_overlay_text_rejects_empty_profileId()
    {
        var tempDir = CreateTempDirectory();
        var registry = CreateRegistry(tempDir);

        var result = await registry.CallToolAsync("poe_write_overlay_text", Arguments(
            ("profileId", ""),
            ("resourcePath", "test.txt"),
            ("text", "content")
        ), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("profileId", result.Content.Single().Text);
    }

    [Fact]
    public async Task Write_overlay_text_rejects_missing_profileId()
    {
        var tempDir = CreateTempDirectory();
        var registry = CreateRegistry(tempDir);

        var result = await registry.CallToolAsync("poe_write_overlay_text", Arguments(
            ("resourcePath", "test.txt"),
            ("text", "content")
        ), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("profileId", result.Content.Single().Text);
    }

    [Fact]
    public async Task Write_overlay_text_rejects_empty_resourcePath()
    {
        var tempDir = CreateTempDirectory();
        var registry = CreateRegistry(tempDir);

        var result = await registry.CallToolAsync("poe_write_overlay_text", Arguments(
            ("profileId", "p"),
            ("resourcePath", ""),
            ("text", "content")
        ), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("resourcePath", result.Content.Single().Text);
    }

    [Fact]
    public async Task Write_overlay_binary_rejects_invalid_base64()
    {
        var tempDir = CreateTempDirectory();
        var registry = CreateRegistry(tempDir);

        var result = await registry.CallToolAsync("poe_write_overlay_binary", Arguments(
            ("profileId", "p"),
            ("resourcePath", "test.bin"),
            ("base64", "not-valid-base64!!!")
        ), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("base64", result.Content.Single().Text);
    }

    [Fact]
    public async Task Write_overlay_binary_rejects_empty_base64()
    {
        var tempDir = CreateTempDirectory();
        var registry = CreateRegistry(tempDir);

        var result = await registry.CallToolAsync("poe_write_overlay_binary", Arguments(
            ("profileId", "p"),
            ("resourcePath", "test.bin"),
            ("base64", "")
        ), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("base64", result.Content.Single().Text);
    }

    [Fact]
    public async Task Write_overlay_text_accepts_large_but_reasonable_content()
    {
        var tempDir = CreateTempDirectory();
        var registry = CreateRegistry(tempDir);

        var largeText = new string('x', 1_000_000); // ~1 MB

        var result = await registry.CallToolAsync("poe_write_overlay_text", Arguments(
            ("profileId", "large-profile"),
            ("resourcePath", "large.txt"),
            ("text", largeText)
        ), CancellationToken.None);

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task Write_overlay_text_rejects_content_exceeding_50MB()
    {
        var tempDir = CreateTempDirectory();
        var registry = CreateRegistry(tempDir);

        var oversizedText = new string('z', 60_000_000); // ~60 MB

        var result = await registry.CallToolAsync("poe_write_overlay_text", Arguments(
            ("profileId", "oversize-profile"),
            ("resourcePath", "huge.txt"),
            ("text", oversizedText)
        ), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("exceeds maximum size", result.Content.Single().Text);
    }

    [Fact]
    public async Task Write_overlay_binary_rejects_oversized_content()
    {
        var tempDir = CreateTempDirectory();
        var registry = CreateRegistry(tempDir);

        var oversized = new byte[60_000_000]; // ~60 MB
        var base64 = Convert.ToBase64String(oversized);

        var result = await registry.CallToolAsync("poe_write_overlay_binary", Arguments(
            ("profileId", "oversize-profile"),
            ("resourcePath", "huge.bin"),
            ("base64", base64)
        ), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("exceeds maximum size", result.Content.Single().Text);
    }

    [Fact]
    public async Task Revert_overlay_rejects_empty_profileId()
    {
        var tempDir = CreateTempDirectory();
        var registry = CreateRegistry(tempDir);

        var result = await registry.CallToolAsync("poe_revert_overlay", Arguments(
            ("profileId", ""),
            ("resourcePath", "test.txt")
        ), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("profileId", result.Content.Single().Text);
    }

    [Fact]
    public async Task Revert_overlay_rejects_empty_resourcePath()
    {
        var tempDir = CreateTempDirectory();
        var registry = CreateRegistry(tempDir);

        var result = await registry.CallToolAsync("poe_revert_overlay", Arguments(
            ("profileId", "p"),
            ("resourcePath", "")
        ), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("resourcePath", result.Content.Single().Text);
    }

    [Fact]
    public async Task List_overlays_rejects_empty_profileId()
    {
        var tempDir = CreateTempDirectory();
        var registry = CreateRegistry(tempDir);

        var result = await registry.CallToolAsync("poe_list_overlays", Arguments(
            ("profileId", "")
        ), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("profileId", result.Content.Single().Text);
    }

    private static McpToolRegistry CreateRegistry(string tempDir)
    {
        var workspace = new PoeWorkspaceResolution(true, tempDir, "test", null);
        return McpToolRegistry.CreateDefault(workspace);
    }

    private static JsonElement Arguments(params (string Name, string Value)[] properties)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in properties)
        {
            dict[name] = value;
        }
        return JsonSerializer.SerializeToElement(dict);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "poe-mcp-write-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
