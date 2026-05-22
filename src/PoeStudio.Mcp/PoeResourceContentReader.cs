using PoeStudio.Contracts;
using PoeStudio.Core.Patching;
using PoeStudio.Storage.Resources;

namespace PoeStudio.Mcp;

public sealed class PoeResourceContentReader
{
    public const int DefaultMaxBytes = 65536;
    public const int AbsoluteMaxBytes = 1048576;

    private readonly ResourceIndexStore resourceIndexStore;
    private readonly string[] allowedRoots;

    public PoeResourceContentReader(ResourceIndexStore resourceIndexStore, IEnumerable<string> allowedRoots)
    {
        this.resourceIndexStore = resourceIndexStore;
        this.allowedRoots = allowedRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<PoeResourceContentReadResult> ReadAsync(
        string profileId,
        string resourcePath,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (maxBytes is <= 0 or > AbsoluteMaxBytes)
        {
            return PoeResourceContentReadResult.Error(
                "invalid_max_bytes",
                $"maxBytes must be between 1 and {AbsoluteMaxBytes}.");
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

        if (IsNativeOrUnsupported(resource.PhysicalPath))
        {
            return PoeResourceContentReadResult.Error(
                "native_resource_not_supported_in_stage1",
                "Native Bundles2 or non-physical resources are not supported by Stage 1 MCP read tools.");
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

    private static bool IsNativeOrUnsupported(string? physicalPath)
    {
        return string.IsNullOrWhiteSpace(physicalPath)
            || NativeBundleLocationParser.TryParse(physicalPath, out _)
            || physicalPath.Contains("://", StringComparison.Ordinal);
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
