using PoeStudio.Contracts;
using PoeStudio.Mcp;
using PoeStudio.Storage.Resources;

namespace PoeStudio.Tests;

public sealed class McpResourceContentReaderTests
{
    [Fact]
    public async Task ReadAsync_reads_existing_physical_resource()
    {
        var root = CreateTempDirectory();
        var profileId = "profile-1";
        var physicalPath = Path.Combine(root, "source", "text.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        await File.WriteAllTextAsync(physicalPath, "hello exile");
        await SaveResourcesAsync(root, Resource(profileId, "text/text.txt", ".txt", ResourceKind.Text, physicalPath));
        var reader = new PoeResourceContentReader(new ResourceIndexStore(root), [Path.Combine(root, "source")]);

        var result = await reader.ReadAsync(profileId, "text/text.txt", 1024, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("text/text.txt", result.Resource?.NormalizedPath);
        Assert.Equal("hello exile", System.Text.Encoding.UTF8.GetString(result.Bytes));
        Assert.False(result.Truncated);
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("C:/temp/secret.txt")]
    [InlineData("TEXT/../secret.txt")]
    public async Task ReadAsync_rejects_unsafe_resource_paths(string resourcePath)
    {
        var root = CreateTempDirectory();
        var reader = new PoeResourceContentReader(new ResourceIndexStore(root), [root]);

        var result = await reader.ReadAsync("profile-1", resourcePath, 1024, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("invalid_resource_path", result.ErrorCode);
    }

    [Fact]
    public async Task ReadAsync_returns_stage1_native_error_for_native_bundles2_resource()
    {
        var root = CreateTempDirectory();
        var profileId = "profile-1";
        await SaveResourcesAsync(
            root,
            Resource(
                profileId,
                "metadata/native.datc64",
                ".datc64",
                ResourceKind.Table,
                "native-bundles2://bundle.bin#offset=0&size=12"));
        var reader = new PoeResourceContentReader(new ResourceIndexStore(root), [root]);

        var result = await reader.ReadAsync(profileId, "metadata/native.datc64", 1024, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("native_resource_not_supported_in_stage1", result.ErrorCode);
    }

    [Fact]
    public async Task ReadAsync_rejects_max_bytes_above_one_megabyte()
    {
        var reader = new PoeResourceContentReader(new ResourceIndexStore(CreateTempDirectory()), [CreateTempDirectory()]);

        var result = await reader.ReadAsync("profile-1", "text/text.txt", 1048577, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("invalid_max_bytes", result.ErrorCode);
    }

    [Fact]
    public async Task ReadAsync_rejects_indexed_physical_path_outside_allowed_roots()
    {
        var root = CreateTempDirectory();
        var allowedRoot = Path.Combine(root, "client");
        var outsideRoot = Path.Combine(root, "outside");
        var profileId = "profile-1";
        var outsidePath = Path.Combine(outsideRoot, "secret.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outsidePath)!);
        await File.WriteAllTextAsync(outsidePath, "do not read");
        await SaveResourcesAsync(root, Resource(profileId, "text/safe.txt", ".txt", ResourceKind.Text, outsidePath));
        var reader = new PoeResourceContentReader(new ResourceIndexStore(root), [allowedRoot]);

        var result = await reader.ReadAsync(profileId, "text/safe.txt", 1024, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("physical_path_outside_allowed_roots", result.ErrorCode);
        Assert.Empty(result.Bytes);
    }

    private static async Task SaveResourcesAsync(string root, params ResourceSummaryDto[] resources)
    {
        await new ResourceIndexStore(root).SaveAsync(resources[0].ProfileId, resources, [], CancellationToken.None);
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
        var path = Path.Combine(Path.GetTempPath(), "poe-mcp-resource-reader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
