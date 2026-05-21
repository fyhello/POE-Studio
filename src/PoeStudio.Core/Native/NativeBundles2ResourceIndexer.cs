using PoeStudio.Contracts;
using PoeStudio.Core.Resources;

namespace PoeStudio.Core.Native;

public sealed record NativeResourceIndexResult(
    IReadOnlyList<ResourceSummaryDto> Resources,
    int TotalFiles,
    int FailedPaths,
    IReadOnlyList<string> Warnings);

public sealed class NativeBundles2ResourceIndexer
{
    public NativeResourceIndexResult Index(
        ClientProfileDto profile,
        NativeIndexParseResult parsed,
        NativePathResolveResult paths)
    {
        var indexedAt = DateTimeOffset.UtcNow;
        var resources = new List<ResourceSummaryDto>(paths.ResolvedCount);
        var warnings = new List<string>(parsed.Warnings.Concat(paths.Warnings));

        foreach (var file in parsed.Files)
        {
            if (!paths.Paths.TryGetValue(file.PathHash, out var virtualPath))
            {
                continue;
            }

            string normalized;
            try
            {
                normalized = ResourcePath.Normalize(virtualPath);
            }
            catch (ArgumentException ex)
            {
                warnings.Add($"跳过无法识别的 native 资源路径：{virtualPath} ({ex.Message})");
                continue;
            }

            var bundle = parsed.Bundles[file.BundleIndex];
            var extension = Path.GetExtension(normalized).ToLowerInvariant();
            resources.Add(new ResourceSummaryDto(
                Id: CreateId(profile.Id, normalized),
                ProfileId: profile.Id,
                VirtualPath: normalized,
                NormalizedPath: normalized,
                Extension: extension,
                Kind: ResourceClassifier.Classify(normalized),
                Size: file.Size,
                PhysicalPath: CreateNativePhysicalPath(bundle.Path, file.Offset, file.Size),
                SourceLayer: ResourceSourceLayer.Base,
                IndexedAt: indexedAt));
        }

        if (paths.FailedCount > 0)
        {
            warnings.Add($"{paths.FailedCount} 个 native 文件路径未解析。");
        }

        return new NativeResourceIndexResult(
            resources.OrderBy(resource => resource.NormalizedPath, StringComparer.OrdinalIgnoreCase).ToArray(),
            parsed.FileCount,
            paths.FailedCount,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string CreateNativePhysicalPath(string bundlePath, int offset, int size)
    {
        return $"native-bundles2://{bundlePath}.bundle.bin#offset={offset}&size={size}";
    }

    private static string CreateId(string profileId, string normalizedPath)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{profileId}:native:{normalizedPath}"))).ToLowerInvariant();
    }
}
