using PoeStudio.Contracts;
using PoeStudio.Core.Native;
using PoeStudio.Core.Patching;
using PoeStudio.Storage.Resources;

namespace PoeStudio.Mcp;

public sealed class PoeResourceContentReader
{
    public const int DefaultMaxBytes = 65536;
    public const int AbsoluteMaxBytes = 1048576;

    private readonly ResourceIndexStore resourceIndexStore;
    private readonly string[] allowedRoots;
    private readonly ClientProfileDto? profile;
    private readonly NativeBundleResourceContentResolver? nativeContentResolver;

    public PoeResourceContentReader(ResourceIndexStore resourceIndexStore, IEnumerable<string> allowedRoots)
        : this(resourceIndexStore, allowedRoots, profile: null, nativeContentResolver: null)
    {
    }

    public PoeResourceContentReader(
        ResourceIndexStore resourceIndexStore,
        IEnumerable<string> allowedRoots,
        ClientProfileDto? profile,
        NativeBundleResourceContentResolver? nativeContentResolver)
    {
        this.resourceIndexStore = resourceIndexStore;
        this.allowedRoots = allowedRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        this.profile = profile;
        this.nativeContentResolver = nativeContentResolver;
    }

    public async Task<PoeResourceContentReadResult> ReadAsync(
        string profileId,
        string resourcePath,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        return await ReadAsync(profileId, resourcePath, maxBytes, oodlePath: null, cancellationToken);
    }

    public async Task<PoeResourceContentReadResult> ReadAsync(
        string profileId,
        string resourcePath,
        int maxBytes,
        string? oodlePath,
        CancellationToken cancellationToken)
    {
        return await ReadAsync(profileId, resourcePath, maxBytes, oodlePath, AbsoluteMaxBytes, cancellationToken);
    }

    public async Task<PoeResourceContentReadResult> ReadAsync(
        string profileId,
        string resourcePath,
        int maxBytes,
        string? oodlePath,
        int maxAllowedBytes,
        CancellationToken cancellationToken)
    {
        if (maxAllowedBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAllowedBytes));
        }

        if (maxBytes <= 0 || maxBytes > maxAllowedBytes)
        {
            return PoeResourceContentReadResult.Error(
                "invalid_max_bytes",
                $"maxBytes must be between 1 and {maxAllowedBytes}.");
        }

        ResourceSummaryDto? resource;
        try
        {
            resource = await resourceIndexStore.GetByPathAsync(profileId, resourcePath, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return PoeResourceContentReadResult.Error("invalid_resource_path", exception.Message);
        }

        if (resource is null)
        {
            return PoeResourceContentReadResult.Error("resource_not_found", $"Resource '{resourcePath}' was not found in the index.");
        }

        if (NativeBundleResourceContentResolver.IsNativeResource(resource))
        {
            if (!IsNativePhysicalPathAllowed(resource.PhysicalPath, out var nativePathError))
            {
                return nativePathError;
            }

            if (profile is null || nativeContentResolver is null)
            {
                return PoeResourceContentReadResult.Error(
                    "native_resource_reader_unavailable",
                    "Native Bundles2 or GGPK resources require a profile and native content resolver.");
            }

            var nativeRead = await nativeContentResolver.ReadAsync(profile, resource, oodlePath, cancellationToken);
            if (!nativeRead.Ok)
            {
                return PoeResourceContentReadResult.Error(
                    nativeRead.ErrorCode ?? "native_resource_read_failed",
                    nativeRead.Message ?? "Native resource read failed.");
            }

            var count = Math.Min(maxBytes, nativeRead.Data.Length);
            return PoeResourceContentReadResult.Success(
                resource,
                resource.PhysicalPath!,
                nativeRead.Data.AsSpan(0, count).ToArray(),
                nativeRead.Data.Length > count);
        }

        if (IsUnsupported(resource.PhysicalPath))
        {
            return PoeResourceContentReadResult.Error(
                "unsupported_resource_path",
                "Only physical files, native-bundles2://, and ggpk-bundles2:// resources are supported by MCP read tools.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(resource.PhysicalPath!);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return PoeResourceContentReadResult.Error("invalid_physical_path", exception.Message);
        }

        if (!IsUnderAllowedRoot(fullPath))
        {
            return PoeResourceContentReadResult.Error(
                "physical_path_outside_allowed_roots",
                $"Physical resource path is outside the allowed profile roots: {fullPath}");
        }

        if (!File.Exists(fullPath))
        {
            return PoeResourceContentReadResult.Error("physical_resource_not_found", $"Physical resource file does not exist: {fullPath}");
        }

        var bytes = await ReadPrefixAsync(fullPath, maxBytes, cancellationToken);
        return PoeResourceContentReadResult.Success(resource, fullPath, bytes.Bytes, bytes.Truncated);
    }

    private static bool IsUnsupported(string? physicalPath)
    {
        return string.IsNullOrWhiteSpace(physicalPath)
            || physicalPath.Contains("://", StringComparison.Ordinal);
    }

    private bool IsNativePhysicalPathAllowed(string? physicalPath, out PoeResourceContentReadResult error)
    {
        error = PoeResourceContentReadResult.Error("unused", "unused");
        const string GgpkBundles2Scheme = "ggpk-bundles2://";
        if (string.IsNullOrWhiteSpace(physicalPath)
            || !physicalPath.StartsWith(GgpkBundles2Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rest = physicalPath[GgpkBundles2Scheme.Length..];
        var hashIndex = rest.IndexOf('#');
        if (hashIndex <= 0)
        {
            error = PoeResourceContentReadResult.Error(
                "invalid_ggpk_bundles2_resource_path",
                "GGPK 内嵌 native 资源定位信息不合法。");
            return false;
        }

        string ggpkPath;
        try
        {
            ggpkPath = Path.GetFullPath(rest[..hashIndex]);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = PoeResourceContentReadResult.Error("invalid_physical_path", exception.Message);
            return false;
        }

        if (IsUnderAllowedRoot(ggpkPath))
        {
            return true;
        }

        error = PoeResourceContentReadResult.Error(
            "physical_path_outside_allowed_roots",
            $"GGPK resource path is outside the allowed profile roots: {ggpkPath}");
        return false;
    }

    private bool IsUnderAllowedRoot(string fullPath)
    {
        return allowedRoots.Any(root => IsSameOrChildPath(root, fullPath));
    }

    private static bool IsSameOrChildPath(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(path);

        return string.Equals(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(
                normalizedRoot + Path.AltDirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(byte[] Bytes, bool Truncated)> ReadPrefixAsync(
        string path,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var buffer = new byte[maxBytes];
        var total = 0;
        while (total < maxBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, maxBytes - total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return (buffer[..total], stream.Length > total);
    }
}

public sealed record PoeResourceContentReadResult(
    bool IsError,
    string? ErrorCode,
    string? ErrorMessage,
    ResourceSummaryDto? Resource,
    string? PhysicalPath,
    byte[] Bytes,
    bool Truncated)
{
    public static PoeResourceContentReadResult Success(
        ResourceSummaryDto resource,
        string physicalPath,
        byte[] bytes,
        bool truncated)
    {
        return new PoeResourceContentReadResult(false, null, null, resource, physicalPath, bytes, truncated);
    }

    public static PoeResourceContentReadResult Error(string code, string message)
    {
        return new PoeResourceContentReadResult(true, code, message, null, null, [], false);
    }
}
