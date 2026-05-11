using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Workspace;

namespace PoeStudio.Storage.Resources;

public sealed class ResourceIndexStore
{
    private const int ShardCount = 128;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions JsonLineOptions = new(JsonSerializerDefaults.Web);

    private readonly string workspaceRoot;
    private readonly Dictionary<string, Dictionary<string, ResourceSummaryDto>> pathCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object cacheLock = new();

    public ResourceIndexStore(string workspaceRoot)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public async Task SaveAsync(
        string profileId,
        IReadOnlyList<ResourceSummaryDto> resources,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        var path = GetLegacyIndexPath(profileId);
        var indexRoot = GetIndexRoot(profileId);
        Directory.CreateDirectory(indexRoot);
        var document = new ResourceIndexDocument(profileId, DateTimeOffset.UtcNow, resources, warnings);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            await SaveShardsAsync(profileId, resources, warnings, cancellationToken);
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
            }

            File.Move(tempPath, path, overwrite: true);
            lock (cacheLock)
            {
                pathCache.Remove(profileId);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async Task<ResourceSearchResponse> SearchAsync(ResourceSearchRequest request, CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, 500);
        var matched = await SearchResourcesAsync(request, skip, take, cancellationToken);

        return new ResourceSearchResponse(request.ProfileId, matched.Total, skip, take, matched.Items);
    }

    public async Task<ResourceSummaryDto?> GetByPathAsync(string profileId, string virtualPath, CancellationToken cancellationToken)
    {
        var normalized = PoeStudio.Core.Resources.ResourcePath.Normalize(virtualPath);
        if (Directory.Exists(GetShardRoot(profileId)))
        {
            await foreach (var resource in ReadShardAsync(GetShardPath(profileId, normalized), cancellationToken))
            {
                if (string.Equals(resource.NormalizedPath, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return resource;
                }
            }

            return null;
        }

        var index = await LoadPathIndexAsync(profileId, cancellationToken);
        return index.GetValueOrDefault(normalized);
    }

    private async Task<(int Total, IReadOnlyList<ResourceSummaryDto> Items)> SearchResourcesAsync(
        ResourceSearchRequest request,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(GetShardRoot(request.ProfileId)))
        {
            return await SearchSortedStreamAsync(request, skip, take, cancellationToken);
        }

        var resources = await LoadLegacyResourcesAsync(request.ProfileId, cancellationToken);
        var matched = ApplyFilters(resources, request)
            .OrderBy(resource => resource.NormalizedPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return (matched.Length, matched.Skip(skip).Take(take).ToArray());
    }

    private async Task<(int Total, IReadOnlyList<ResourceSummaryDto> Items)> SearchSortedStreamAsync(
        ResourceSearchRequest request,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var total = 0;
        var page = new List<ResourceSummaryDto>(take);
        await foreach (var resource in ReadShardAsync(GetSortedIndexPath(request.ProfileId), cancellationToken))
        {
            if (!Matches(resource, request))
            {
                continue;
            }

            if (total >= skip && page.Count < take)
            {
                page.Add(resource);
            }

            total++;
        }

        return (total, page);
    }

    private async Task<Dictionary<string, ResourceSummaryDto>> LoadPathIndexAsync(string profileId, CancellationToken cancellationToken)
    {
        lock (cacheLock)
        {
            if (pathCache.TryGetValue(profileId, out var cached))
            {
                return cached;
            }
        }

        var resources = Directory.Exists(GetShardRoot(profileId))
            ? await LoadShardedResourcesAsync(profileId, cancellationToken)
            : await LoadLegacyResourcesAsync(profileId, cancellationToken);
        var index = resources
            .GroupBy(resource => resource.NormalizedPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        lock (cacheLock)
        {
            pathCache[profileId] = index;
        }

        return index;
    }

    private async Task<IReadOnlyList<ResourceSummaryDto>> LoadLegacyResourcesAsync(string profileId, CancellationToken cancellationToken)
    {
        var path = GetLegacyIndexPath(profileId);
        if (!File.Exists(path))
        {
            return Array.Empty<ResourceSummaryDto>();
        }

        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<ResourceIndexDocument>(stream, JsonOptions, cancellationToken);
        return document?.Resources ?? Array.Empty<ResourceSummaryDto>();
    }

    private async Task<IReadOnlyList<ResourceSummaryDto>> LoadShardedResourcesAsync(string profileId, CancellationToken cancellationToken)
    {
        var resources = new List<ResourceSummaryDto>();
        foreach (var shardPath in EnumerateShardPaths(profileId))
        {
            await foreach (var resource in ReadShardAsync(shardPath, cancellationToken))
            {
                resources.Add(resource);
            }
        }

        return resources;
    }

    private async Task SaveShardsAsync(
        string profileId,
        IReadOnlyList<ResourceSummaryDto> resources,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        var shardRoot = GetShardRoot(profileId);
        var tempRoot = $"{shardRoot}.{Guid.NewGuid():N}.tmp";
        Directory.CreateDirectory(tempRoot);

        try
        {
            await WriteManifestAsync(profileId, resources.Count, warnings, tempRoot, cancellationToken);
            var orderedResources = resources
                .OrderBy(resource => resource.NormalizedPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            await WriteJsonLinesAsync(Path.Combine(tempRoot, "resources.sorted.jsonl"), orderedResources, cancellationToken);

            var buckets = orderedResources.GroupBy(resource => GetShardName(resource.NormalizedPath));

            foreach (var bucket in buckets)
            {
                var shardPath = Path.Combine(tempRoot, $"{bucket.Key}.jsonl");
                await WriteJsonLinesAsync(shardPath, bucket, cancellationToken);
            }

            if (Directory.Exists(shardRoot))
            {
                Directory.Delete(shardRoot, recursive: true);
            }

            Directory.Move(tempRoot, shardRoot);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static async Task WriteJsonLinesAsync(
        string path,
        IEnumerable<ResourceSummaryDto> resources,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await using var writer = new StreamWriter(stream);
        foreach (var resource in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(JsonSerializer.Serialize(resource, JsonLineOptions));
        }
    }

    private static async Task WriteManifestAsync(
        string profileId,
        int totalResources,
        IReadOnlyList<string> warnings,
        string root,
        CancellationToken cancellationToken)
    {
        var manifest = new ResourceShardManifest(profileId, DateTimeOffset.UtcNow, totalResources, ShardCount, warnings);
        await using var stream = File.Create(Path.Combine(root, "manifest.json"));
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
    }

    private async IAsyncEnumerable<ResourceSummaryDto> ReadShardAsync(
        string shardPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!File.Exists(shardPath))
        {
            yield break;
        }

        using var stream = File.OpenRead(shardPath);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var resource = JsonSerializer.Deserialize<ResourceSummaryDto>(line, JsonLineOptions);
            if (resource is not null)
            {
                yield return resource;
            }
        }
    }

    private IEnumerable<string> EnumerateShardPaths(string profileId)
    {
        var shardRoot = GetShardRoot(profileId);
        if (!Directory.Exists(shardRoot))
        {
            yield break;
        }

        for (var i = 0; i < ShardCount; i++)
        {
            yield return Path.Combine(shardRoot, $"{i:x2}.jsonl");
        }
    }

    private static IEnumerable<ResourceSummaryDto> ApplyFilters(IEnumerable<ResourceSummaryDto> resources, ResourceSearchRequest request)
    {
        return resources.Where(resource => Matches(resource, request));
    }

    private static bool Matches(ResourceSummaryDto resource, ResourceSearchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Query)
            && !resource.NormalizedPath.Contains(request.Query, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (request.Kind is not null && resource.Kind != request.Kind.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.Extension))
        {
            var extension = request.Extension.StartsWith('.') ? request.Extension : $".{request.Extension}";
            if (!string.Equals(resource.Extension, extension, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetShardName(string normalizedPath)
    {
        var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(normalizedPath);
        var shard = (hash & int.MaxValue) % ShardCount;
        return shard.ToString("x2");
    }

    private string GetIndexRoot(string profileId)
    {
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, profileId);
        return Path.Combine(layout.CacheRoot, "index");
    }

    private string GetLegacyIndexPath(string profileId)
    {
        return Path.Combine(GetIndexRoot(profileId), "resources.json");
    }

    private string GetShardRoot(string profileId)
    {
        return Path.Combine(GetIndexRoot(profileId), "resources-v2", "shards");
    }

    private string GetShardPath(string profileId, string normalizedPath)
    {
        return Path.Combine(GetShardRoot(profileId), $"{GetShardName(normalizedPath)}.jsonl");
    }

    private string GetSortedIndexPath(string profileId)
    {
        return Path.Combine(GetShardRoot(profileId), "resources.sorted.jsonl");
    }

    private sealed record ResourceIndexDocument(
        string ProfileId,
        DateTimeOffset IndexedAt,
        IReadOnlyList<ResourceSummaryDto> Resources,
        IReadOnlyList<string> Warnings);

    private sealed record ResourceShardManifest(
        string ProfileId,
        DateTimeOffset IndexedAt,
        int TotalResources,
        int ShardCount,
        IReadOnlyList<string> Warnings);
}
