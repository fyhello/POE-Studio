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

    [Fact]
    public async Task SearchAsync_uses_latest_saved_index()
    {
        var store = new ResourceIndexStore(Path.Combine(Path.GetTempPath(), "poe-studio-index-store-tests", Guid.NewGuid().ToString("N")));
        var profileId = Guid.NewGuid().ToString("N");
        await store.SaveAsync(profileId, [Resource(profileId, "text/one.txt", ResourceKind.Text, ".txt")], [], CancellationToken.None);

        var first = await store.SearchAsync(new ResourceSearchRequest(profileId, Query: "one"), CancellationToken.None);
        await store.SaveAsync(profileId, [Resource(profileId, "text/two.txt", ResourceKind.Text, ".txt")], [], CancellationToken.None);
        var oldQuery = await store.SearchAsync(new ResourceSearchRequest(profileId, Query: "one"), CancellationToken.None);
        var newQuery = await store.SearchAsync(new ResourceSearchRequest(profileId, Query: "two"), CancellationToken.None);

        Assert.Single(first.Items);
        Assert.Empty(oldQuery.Items);
        Assert.Single(newQuery.Items);
    }

    [Fact]
    public async Task SaveAsync_writes_streaming_shards_and_new_store_can_search_without_json_cache()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-index-store-tests", Guid.NewGuid().ToString("N"));
        var profileId = Guid.NewGuid().ToString("N");
        var first = new ResourceIndexStore(root);
        await first.SaveAsync(profileId, [
            Resource(profileId, "metadata/items/amulet.ot", ResourceKind.Table, ".ot"),
            Resource(profileId, "text/client/strings.txt", ResourceKind.Text, ".txt"),
            Resource(profileId, "art/textures/icon.dds", ResourceKind.Image, ".dds")
        ], [], CancellationToken.None);

        var shardRoot = Path.Combine(root, "profiles", profileId, "cache", "index", "resources-v2", "shards");
        Assert.True(Directory.Exists(shardRoot));
        Assert.NotEmpty(Directory.EnumerateFiles(shardRoot, "*.jsonl"));

        var second = new ResourceIndexStore(root);
        var tableResult = await second.SearchAsync(new ResourceSearchRequest(profileId, Query: "items", Kind: ResourceKind.Table, Extension: ".ot"), CancellationToken.None);
        var pathResult = await second.GetByPathAsync(profileId, "text/client/strings.txt", CancellationToken.None);

        Assert.Single(tableResult.Items);
        Assert.Equal("metadata/items/amulet.ot", tableResult.Items[0].VirtualPath);
        Assert.NotNull(pathResult);
        Assert.Equal(ResourceKind.Text, pathResult.Kind);
    }

    [Fact]
    public async Task SearchAsync_uses_extension_index_for_extension_only_queries_after_reload()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-index-store-tests", Guid.NewGuid().ToString("N"));
        var profileId = Guid.NewGuid().ToString("N");
        var first = new ResourceIndexStore(root);
        await first.SaveAsync(profileId, [
            Resource(profileId, "metadata/a/base.ot", ResourceKind.Table, ".ot"),
            Resource(profileId, "metadata/b/second.ot", ResourceKind.Table, ".ot"),
            Resource(profileId, "metadata/c/third.datc64", ResourceKind.Table, ".datc64"),
            Resource(profileId, "art/textures/icon.dds", ResourceKind.Image, ".dds")
        ], [], CancellationToken.None);

        var extensionRoot = Path.Combine(root, "profiles", profileId, "cache", "index", "resources-v2", "shards", "extensions");
        Assert.True(File.Exists(Path.Combine(extensionRoot, "_ot.jsonl")));

        var second = new ResourceIndexStore(root);
        var result = await second.SearchAsync(new ResourceSearchRequest(profileId, Extension: ".ot", Skip: 1, Take: 1), CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Single(result.Items);
        Assert.Equal("metadata/b/second.ot", result.Items[0].VirtualPath);
    }

    [Fact]
    public async Task SearchAsync_uses_extension_index_for_extension_and_query_after_reload()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-index-store-tests", Guid.NewGuid().ToString("N"));
        var profileId = Guid.NewGuid().ToString("N");
        var first = new ResourceIndexStore(root);
        await first.SaveAsync(profileId, [
            Resource(profileId, "metadata/ui/ingamestate/characterpanel/characterpanel.ui", ResourceKind.Ui, ".ui"),
            Resource(profileId, "metadata/ui/ingamestate/characterpanel/skillpanel_console.ui", ResourceKind.Ui, ".ui"),
            Resource(profileId, "metadata/ui/credits.ui", ResourceKind.Ui, ".ui"),
            Resource(profileId, "metadata/ui/ingamestate/characterpanel/data.datc64", ResourceKind.Table, ".datc64")
        ], [], CancellationToken.None);

        var second = new ResourceIndexStore(root);
        var result = await second.SearchAsync(new ResourceSearchRequest(profileId, Query: "characterpanel", Extension: ".ui", Take: 10), CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Equal([
            "metadata/ui/ingamestate/characterpanel/characterpanel.ui",
            "metadata/ui/ingamestate/characterpanel/skillpanel_console.ui"
        ], result.Items.Select(item => item.VirtualPath).ToArray());
    }

    [Fact]
    public async Task SearchAsync_allows_large_extension_pages_for_complete_resource_trees()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-index-store-tests", Guid.NewGuid().ToString("N"));
        var profileId = Guid.NewGuid().ToString("N");
        var resources = Enumerable.Range(0, 750)
            .Select(index => Resource(profileId, $"data/balance/folder{index % 5}/table{index:D4}.datc64", ResourceKind.Table, ".datc64"))
            .ToArray();
        var first = new ResourceIndexStore(root);
        await first.SaveAsync(profileId, resources, [], CancellationToken.None);

        var second = new ResourceIndexStore(root);
        var result = await second.SearchAsync(new ResourceSearchRequest(profileId, Extension: ".datc64", Take: 750), CancellationToken.None);

        Assert.Equal(750, result.Total);
        Assert.Equal(750, result.Take);
        Assert.Equal(750, result.Items.Count);
        Assert.Contains(result.Items, item => item.VirtualPath == "data/balance/folder4/table0749.datc64");
    }

    [Fact]
    public async Task SearchAsync_translation_only_returns_known_translation_resources()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-index-store-tests", Guid.NewGuid().ToString("N"));
        var profileId = Guid.NewGuid().ToString("N");
        var store = new ResourceIndexStore(root);
        await store.SaveAsync(profileId, [
            Resource(profileId, "data/balance/languages.dat", ResourceKind.Table, ".dat"),
            Resource(profileId, "data/balance/traditional chinese/clientstrings.datc64", ResourceKind.Table, ".datc64"),
            Resource(profileId, "data/statdescriptions/skill_stat_descriptions.csd", ResourceKind.Text, ".csd"),
            Resource(profileId, "metadata/ui/uisettings.traditional chinese.xml", ResourceKind.Ui, ".xml"),
            Resource(profileId, "art/uiimages1.txt", ResourceKind.Text, ".txt"),
            Resource(profileId, "data/balance/simplified chinese/clientstrings.datc64", ResourceKind.Table, ".datc64"),
            Resource(profileId, "metadata/effects/changed.aoc", ResourceKind.Binary, ".aoc")
        ], [], CancellationToken.None);

        var result = await store.SearchAsync(new ResourceSearchRequest(profileId, TranslationOnly: true, Take: 20), CancellationToken.None);

        Assert.Equal([
            "art/uiimages1.txt",
            "data/balance/languages.dat",
            "data/balance/traditional chinese/clientstrings.datc64",
            "data/statdescriptions/skill_stat_descriptions.csd",
            "metadata/ui/uisettings.traditional chinese.xml"
        ], result.Items.Select(item => item.VirtualPath).ToArray());
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
