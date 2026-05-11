using PoeStudio.Contracts;

namespace PoeStudio.Core.Resources;

public sealed class FileSystemResourceIndexer
{
    private static readonly HashSet<string> ContainerExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ggpk", ".bin"
    };

    public Task<FileSystemResourceIndexResult> IndexAsync(ClientProfileDto profile, CancellationToken cancellationToken)
    {
        var sourceRoot = GetSourceRoot(profile);
        var warnings = new List<string>();

        if (sourceRoot is null || !Directory.Exists(sourceRoot))
        {
            warnings.Add("资源目录不存在，无法建立展开资源索引。");
            return Task.FromResult(new FileSystemResourceIndexResult(Array.Empty<ResourceSummaryDto>(), warnings));
        }

        var indexedAt = DateTimeOffset.UtcNow;
        var resources = new List<ResourceSummaryDto>();

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(file);
            if (ContainerExtensions.Contains(extension))
            {
                continue;
            }

            string normalized;
            try
            {
                var relative = Path.GetRelativePath(sourceRoot, file);
                normalized = ResourcePath.Normalize(relative);
            }
            catch (ArgumentException ex)
            {
                warnings.Add($"跳过无法识别的资源路径：{file} ({ex.Message})");
                continue;
            }

            var info = new FileInfo(file);
            resources.Add(new ResourceSummaryDto(
                Id: CreateId(profile.Id, normalized),
                ProfileId: profile.Id,
                VirtualPath: normalized,
                NormalizedPath: normalized,
                Extension: extension.ToLowerInvariant(),
                Kind: ResourceClassifier.Classify(normalized),
                Size: info.Length,
                PhysicalPath: info.FullName,
                SourceLayer: ResourceSourceLayer.Base,
                IndexedAt: indexedAt));
        }

        return Task.FromResult(new FileSystemResourceIndexResult(
            resources.OrderBy(resource => resource.NormalizedPath, StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings));
    }

    private static string? GetSourceRoot(ClientProfileDto profile)
    {
        if (profile.EntryKind == ClientEntryKind.Bundles2 && !string.IsNullOrWhiteSpace(profile.Bundles2Path))
        {
            return profile.Bundles2Path;
        }

        return Directory.Exists(profile.RootPath) ? profile.RootPath : null;
    }

    private static string CreateId(string profileId, string normalizedPath)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{profileId}:{normalizedPath}"))).ToLowerInvariant();
    }
}

public sealed record FileSystemResourceIndexResult(
    IReadOnlyList<ResourceSummaryDto> Resources,
    IReadOnlyList<string> Warnings);
