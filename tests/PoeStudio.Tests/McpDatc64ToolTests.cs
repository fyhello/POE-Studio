using System.Text;
using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Mcp;
using PoeStudio.Storage.Profiles;
using PoeStudio.Storage.Resources;

namespace PoeStudio.Tests;

public sealed class McpDatc64ToolTests
{
    [Fact]
    public async Task Extract_translatable_cells_returns_resource_path_and_cells_with_locators()
    {
        var root = CreateTempDirectory();
        var profile = CreateProfile("profile-1", root);
        var resourcePath = "data/balance/traditional chinese/combatuiprompts.datc64";
        var data = BuildDatc64PointerTableData([
            ("NoMana", "法力不足"),
            ("OnCooldown", "冷却中")
        ]);
        var physicalPath = await WriteResourceAsync(root, resourcePath, data);
        await SaveProfileAndResourcesAsync(root, profile, Resource(profile.Id, resourcePath, ".datc64", ResourceKind.Table, data.Length, physicalPath));
        var registry = McpToolRegistry.CreateDefault(new PoeWorkspaceResolution(true, root, "argument", null));

        var result = await registry.CallToolAsync(
            "poe_datc64_extract_translatable_cells",
            JsonSerializer.SerializeToElement(new { profileId = profile.Id, resourcePath, limit = 10 }),
            CancellationToken.None);
        using var payload = ParsePayload(result);

        Assert.False(result.IsError);
        Assert.Equal(resourcePath, payload.RootElement.GetProperty("resourcePath").GetString());
        var first = payload.RootElement.GetProperty("cells").EnumerateArray().First();
        Assert.True(first.TryGetProperty("rowIndex", out _));
        Assert.True(first.TryGetProperty("columnName", out _));
        Assert.True(first.TryGetProperty("sourceText", out _));
        Assert.True(first.TryGetProperty("locator", out _));
        Assert.Contains("法力不足", payload.RootElement.GetProperty("cells").EnumerateArray().Select(cell => cell.GetProperty("sourceText").GetString()));
    }

    [Fact]
    public async Task Extract_translatable_cells_returns_empty_array_for_empty_datc64()
    {
        var root = CreateTempDirectory();
        var profile = CreateProfile("profile-1", root);
        var resourcePath = "data/balance/empty.datc64";
        var data = new byte[12];
        WriteUInt32(data, 0, 0);
        Array.Fill(data, (byte)0xbb, 4, 8);
        var physicalPath = await WriteResourceAsync(root, resourcePath, data);
        await SaveProfileAndResourcesAsync(root, profile, Resource(profile.Id, resourcePath, ".datc64", ResourceKind.Table, data.Length, physicalPath));
        var registry = McpToolRegistry.CreateDefault(new PoeWorkspaceResolution(true, root, "argument", null));

        var result = await registry.CallToolAsync(
            "poe_datc64_extract_translatable_cells",
            JsonSerializer.SerializeToElement(new { profileId = profile.Id, resourcePath, limit = 10 }),
            CancellationToken.None);
        using var payload = ParsePayload(result);

        Assert.False(result.IsError);
        Assert.Empty(payload.RootElement.GetProperty("cells").EnumerateArray());
    }

    [Fact]
    public async Task Extract_translatable_cells_returns_error_for_non_table_resource()
    {
        var root = CreateTempDirectory();
        var profile = CreateProfile("profile-1", root);
        var resourcePath = "text/readme.txt";
        var physicalPath = await WriteResourceAsync(root, resourcePath, Encoding.UTF8.GetBytes("hello"));
        await SaveProfileAndResourcesAsync(root, profile, Resource(profile.Id, resourcePath, ".txt", ResourceKind.Text, 5, physicalPath));
        var registry = McpToolRegistry.CreateDefault(new PoeWorkspaceResolution(true, root, "argument", null));

        var result = await registry.CallToolAsync(
            "poe_datc64_extract_translatable_cells",
            JsonSerializer.SerializeToElement(new { profileId = profile.Id, resourcePath, limit = 10 }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("table", result.Content.Single().Text, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonDocument ParsePayload(McpToolResult result)
    {
        return JsonDocument.Parse(result.Content.Single().Text);
    }

    private static async Task SaveProfileAndResourcesAsync(string root, ClientProfileDto profile, params ResourceSummaryDto[] resources)
    {
        await new ProfileStore(root).SaveAsync(profile, CancellationToken.None);
        await new ResourceIndexStore(root).SaveAsync(profile.Id, resources, [], CancellationToken.None);
    }

    private static async Task<string> WriteResourceAsync(string root, string virtualPath, byte[] data)
    {
        var path = Path.Combine(root, "files", Path.Combine(virtualPath.Split('/')));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, data);
        return path;
    }

    private static ClientProfileDto CreateProfile(string id, string root)
    {
        return new ClientProfileDto(
            Id: id,
            DisplayName: "Official",
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Ggpk,
            RootPath: root,
            ContentGgpkPath: Path.Combine(root, "Content.ggpk"),
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
        long size,
        string physicalPath)
    {
        return new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: profileId,
            VirtualPath: path,
            NormalizedPath: path,
            Extension: extension,
            Kind: kind,
            Size: size,
            PhysicalPath: physicalPath,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
    }

    private static byte[] BuildDatc64PointerTableData(IReadOnlyList<(string Id, string Text)> rows)
    {
        const int rowLength = 32;
        var fixedData = new byte[rows.Count * rowLength];
        using var variable = new MemoryStream();
        variable.Write(new byte[] { 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb });

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var rowOffset = rowIndex * rowLength;
            WriteUInt32(fixedData, rowOffset, (uint)rowIndex);
            WriteUInt32(fixedData, rowOffset + 4, AppendDatc64String(variable, rows[rowIndex].Id));
            WriteUInt32(fixedData, rowOffset + 8, 0);
            WriteUInt32(fixedData, rowOffset + 12, AppendDatc64String(variable, rows[rowIndex].Text));
        }

        var variableData = variable.ToArray();
        var data = new byte[4 + fixedData.Length + variableData.Length];
        WriteUInt32(data, 0, (uint)rows.Count);
        fixedData.CopyTo(data, 4);
        variableData.CopyTo(data, 4 + fixedData.Length);
        return data;
    }

    private static uint AppendDatc64String(Stream stream, string value)
    {
        var offset = checked((uint)stream.Position);
        var bytes = Encoding.Unicode.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(0);
        return offset;
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value & 0xff);
        data[offset + 1] = (byte)((value >> 8) & 0xff);
        data[offset + 2] = (byte)((value >> 16) & 0xff);
        data[offset + 3] = (byte)((value >> 24) & 0xff);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "poe-mcp-datc64-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
