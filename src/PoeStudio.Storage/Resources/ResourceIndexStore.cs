using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using PoeStudio.Contracts;
using PoeStudio.Core.Patching;
using PoeStudio.Core.Workspace;

namespace PoeStudio.Storage.Resources;

public sealed class ResourceIndexStore : IPatchBundleResourceLookup, IPatchPathHashLookup
{
    private const int ShardCount = 128;
    private const int MaxSearchTake = 5000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions JsonLineOptions = new(JsonSerializerDefaults.Web);

    private readonly Func<string> workspaceRoot;
    private readonly Dictionary<string, Dictionary<string, ResourceSummaryDto>> pathCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object cacheLock = new();

    public ResourceIndexStore(string workspaceRoot)
        : this(() => workspaceRoot)
    {
    }

    public ResourceIndexStore(Func<string> workspaceRoot)
    {
        this.workspaceRoot = () => Path.GetFullPath(workspaceRoot());
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
        var take = Math.Clamp(request.Take, 1, MaxSearchTake);
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

    public async Task<string?> FindPathByHashAsync(string profileId, ulong pathHash, CancellationToken cancellationToken)
    {
        var request = new ResourceSearchRequest(profileId, Take: 500);
        var skip = 0;
        while (true)
        {
            var page = await SearchAsync(request with { Skip = skip, Take = 500 }, cancellationToken);
            foreach (var resource in page.Items)
            {
                var hash = PoeStudio.Core.Native.NativeIndexPathResolver.MurmurHash64A(System.Text.Encoding.UTF8.GetBytes(resource.NormalizedPath));
                if (hash == pathHash)
                {
                    return resource.NormalizedPath;
                }
            }

            skip += page.Items.Count;
            if (page.Items.Count == 0 || skip >= page.Total)
            {
                return null;
            }
        }
    }

    public async Task<ResourceSummaryDto?> FindByBundleNameAsync(string profileId, string bundleName, CancellationToken cancellationToken)
    {
        var normalizedBundle = NormalizeBundleName(bundleName);
        if (Directory.Exists(GetShardRoot(profileId)))
        {
            foreach (var shardPath in EnumerateShardPaths(profileId))
            {
                await foreach (var resource in ReadShardAsync(shardPath, cancellationToken))
                {
                    if (ResourceMatchesBundle(resource, normalizedBundle))
                    {
                        return resource;
                    }
                }
            }

            return null;
        }

        foreach (var resource in await LoadLegacyResourcesAsync(profileId, cancellationToken))
        {
            if (ResourceMatchesBundle(resource, normalizedBundle))
            {
                return resource;
            }
        }

        return null;
    }

    private async Task<(int Total, IReadOnlyList<ResourceSummaryDto> Items)> SearchResourcesAsync(
        ResourceSearchRequest request,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(GetShardRoot(request.ProfileId)))
        {
            if (CanUseExtensionIndex(request) && File.Exists(GetExtensionIndexPath(request.ProfileId, request.Extension!)))
            {
                return await SearchExtensionStreamAsync(request, skip, take, cancellationToken);
            }

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

    private async Task<(int Total, IReadOnlyList<ResourceSummaryDto> Items)> SearchExtensionStreamAsync(
        ResourceSearchRequest request,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var total = 0;
        var page = new List<ResourceSummaryDto>(take);
        await foreach (var resource in ReadShardAsync(GetExtensionIndexPath(request.ProfileId, request.Extension!), cancellationToken))
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

            var extensionRoot = Path.Combine(tempRoot, "extensions");
            Directory.CreateDirectory(extensionRoot);
            foreach (var bucket in orderedResources
                         .Where(resource => !string.IsNullOrWhiteSpace(resource.Extension))
                         .GroupBy(resource => NormalizeExtensionKey(resource.Extension)))
            {
                var extensionPath = Path.Combine(extensionRoot, $"{bucket.Key}.jsonl");
                await WriteJsonLinesAsync(extensionPath, bucket, cancellationToken);
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
        if (request.TranslationOnly && !IsTranslationResource(resource))
        {
            return false;
        }

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

    private static bool IsTranslationResource(ResourceSummaryDto resource)
    {
        var path = resource.NormalizedPath.Replace('\\', '/');
        if (path.Equals("data/balance/languages.dat", StringComparison.OrdinalIgnoreCase)
            || path.Equals("art/uiimages1.txt", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.StartsWith("data/balance/traditional chinese/", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(resource.Extension, ".datc64", StringComparison.OrdinalIgnoreCase)
                || string.Equals(resource.Extension, ".dat", StringComparison.OrdinalIgnoreCase);
        }

        if (path.StartsWith("data/statdescriptions/", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(resource.Extension, ".csd", StringComparison.OrdinalIgnoreCase);
        }

        if (path.StartsWith("metadata/ui/", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(resource.Extension, ".ui", StringComparison.OrdinalIgnoreCase)
                || string.Equals(resource.Extension, ".xml", StringComparison.OrdinalIgnoreCase)
                || string.Equals(resource.Extension, ".txt", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool CanUseExtensionIndex(ResourceSearchRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.Extension)
            && request.Kind is null
            && !request.TranslationOnly;
    }

    private static bool ResourceMatchesBundle(ResourceSummaryDto resource, string normalizedBundle)
    {
        return string.Equals(NormalizeBundleName(resource.NormalizedPath), normalizedBundle, StringComparison.OrdinalIgnoreCase)
            || PhysicalPathContainsBundle(resource.PhysicalPath, normalizedBundle);
    }

    private static bool PhysicalPathContainsBundle(string? physicalPath, string normalizedBundle)
    {
        if (string.IsNullOrWhiteSpace(physicalPath))
        {
            return false;
        }

        if (physicalPath.StartsWith("native-bundles2://", StringComparison.OrdinalIgnoreCase))
        {
            var rest = physicalPath["native-bundles2://".Length..];
            var hashIndex = rest.IndexOf('#');
            var bundleName = hashIndex > 0 ? rest[..hashIndex] : rest;
            return string.Equals(NormalizeBundleName(Uri.UnescapeDataString(bundleName)), normalizedBundle, StringComparison.OrdinalIgnoreCase);
        }

        if (physicalPath.StartsWith("ggpk://", StringComparison.OrdinalIgnoreCase))
        {
            var hashIndex = physicalPath.IndexOf('#');
            var path = hashIndex > 0 ? physicalPath["ggpk://".Length..hashIndex] : physicalPath["ggpk://".Length..];
            return string.Equals(NormalizeBundleName(path), normalizedBundle, StringComparison.OrdinalIgnoreCase);
        }

        if (!physicalPath.StartsWith("ggpk-bundles2://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var queryStart = physicalPath.IndexOf('#');
        if (queryStart <= 0 || queryStart == physicalPath.Length - 1)
        {
            return false;
        }

        foreach (var part in physicalPath[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0].Equals("bundlePath", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(NormalizeBundleName(Uri.UnescapeDataString(pair[1])), normalizedBundle, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static string NormalizeBundleName(string bundleName)
    {
        var normalized = bundleName.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("bundles2/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["bundles2/".Length..];
        }

        return normalized;
    }

    private static string GetShardName(string normalizedPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath.ToLowerInvariant()));
        var shard = hash[0] % ShardCount;
        return shard.ToString("x2");
    }

    private static string NormalizeExtensionKey(string extension)
    {
        var normalized = extension.Trim().ToLowerInvariant();
        if (!normalized.StartsWith('.'))
        {
            normalized = $".{normalized}";
        }

        var builder = new StringBuilder(normalized.Length + 1);
        foreach (var ch in normalized)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        return builder.ToString();
    }

    private string GetIndexRoot(string profileId)
    {
        var layout = WorkspaceLayout.ForProfile(workspaceRoot(), profileId);
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

    private string GetExtensionIndexPath(string profileId, string extension)
    {
        return Path.Combine(GetShardRoot(profileId), "extensions", $"{NormalizeExtensionKey(extension)}.jsonl");
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
