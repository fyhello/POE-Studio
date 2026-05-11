using PoeStudio.Contracts;
using PoeStudio.Storage.Resources;

namespace PoeStudio.Tests;

public sealed class ResourceIndexStoreTests
{
    [Fact]
    public async Task Save_then_search_returns_paginated_matches()
    {
        var store = new ResourceIndexStore(Path.Combine(Path.GetTempPath(), "poe-studio-index-store-tests", Guid.NewGuid().ToString("N")));
        var profileId = Guid.NewGuid().ToString("N");
        var resources = new[]
        {
            Resource(profileId, "metadata/items/amulet.ot", ResourceKind.Table, ".ot"),
            Resource(profileId, "metadata/items/ring.ot", ResourceKind.Table, ".ot"),
            Resource(profileId, "art/textures/icon.dds", ResourceKind.Image, ".dds")
        };

        await store.SaveAsync(profileId, resources, ["warning"], CancellationToken.None);
        var result = await store.SearchAsync(new ResourceSearchRequest(profileId, Query: "items", Kind: ResourceKind.Table, Extension: ".ot", Skip: 1, Take: 1), CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Skip);
        Assert.Equal(1, result.Take);
        Assert.Single(result.Items);
        Assert.Equal("metadata/items/ring.ot", result.Items[0].VirtualPath);
    }

    [Fact]
    public async Task Search_returns_empty_result_before_index_exists()
    {
        var store = new ResourceIndexStore(Path.Combine(Path.GetTempPath(), "poe-studio-index-store-tests", Guid.NewGuid().ToString("N")));

        var result = await store.SearchAsync(new ResourceSearchRequest(Guid.NewGuid().ToString("N"), Take: 50), CancellationToken.None);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    private static ResourceSummaryDto Resource(string profileId, string path, ResourceKind kind, string extension)
    {
        return new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: profileId,
            VirtualPath: path,
            NormalizedPath: path,
            Extension: extension,
            Kind: kind,
            Size: 10,
            PhysicalPath: Path.Combine(Path.GetTempPath(), path.Replace('/', Path.DirectorySeparatorChar)),
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
    }
}
