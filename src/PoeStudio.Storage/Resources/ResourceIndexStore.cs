using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Workspace;

namespace PoeStudio.Storage.Resources;

public sealed class ResourceIndexStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string workspaceRoot;
    private readonly Dictionary<string, IReadOnlyList<ResourceSummaryDto>> resourceCache = new(StringComparer.OrdinalIgnoreCase);
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
        var path = GetIndexPath(profileId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var document = new ResourceIndexDocument(profileId, DateTimeOffset.UtcNow, resources, warnings);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
            }

            File.Move(tempPath, path, overwrite: true);
            lock (cacheLock)
            {
                resourceCache[profileId] = resources;
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
        var resources = await LoadResourcesAsync(request.ProfileId, cancellationToken);
        var query = resources.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            query = query.Where(resource => resource.NormalizedPath.Contains(request.Query, StringComparison.OrdinalIgnoreCase));
        }

        if (request.Kind is not null)
        {
            query = query.Where(resource => resource.Kind == request.Kind.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Extension))
        {
            var extension = request.Extension.StartsWith('.') ? request.Extension : $".{request.Extension}";
            query = query.Where(resource => string.Equals(resource.Extension, extension, StringComparison.OrdinalIgnoreCase));
        }

        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, 500);
        var matched = query.OrderBy(resource => resource.NormalizedPath, StringComparer.OrdinalIgnoreCase).ToArray();
        var items = matched.Skip(skip).Take(take).ToArray();

        return new ResourceSearchResponse(request.ProfileId, matched.Length, skip, take, items);
    }

    public async Task<ResourceSummaryDto?> GetByPathAsync(string profileId, string virtualPath, CancellationToken cancellationToken)
    {
        var normalized = PoeStudio.Core.Resources.ResourcePath.Normalize(virtualPath);
        var resources = await LoadResourcesAsync(profileId, cancellationToken);
        return resources.FirstOrDefault(resource => string.Equals(resource.NormalizedPath, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<ResourceSummaryDto>> LoadResourcesAsync(string profileId, CancellationToken cancellationToken)
    {
        var path = GetIndexPath(profileId);
        if (!File.Exists(path))
        {
            return Array.Empty<ResourceSummaryDto>();
        }

        lock (cacheLock)
        {
            if (resourceCache.TryGetValue(profileId, out var cached))
            {
                return cached;
            }
        }

        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<ResourceIndexDocument>(stream, JsonOptions, cancellationToken);
        var resources = document?.Resources ?? Array.Empty<ResourceSummaryDto>();
        lock (cacheLock)
        {
            resourceCache[profileId] = resources;
        }

        return resources;
    }

    private string GetIndexPath(string profileId)
    {
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, profileId);
        return Path.Combine(layout.CacheRoot, "index", "resources.json");
    }

    private sealed record ResourceIndexDocument(
        string ProfileId,
        DateTimeOffset IndexedAt,
        IReadOnlyList<ResourceSummaryDto> Resources,
        IReadOnlyList<string> Warnings);
}
