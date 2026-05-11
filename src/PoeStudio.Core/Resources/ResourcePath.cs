namespace PoeStudio.Core.Resources;

public static class ResourcePath
{
    public static string Normalize(string virtualPath)
    {
        if (string.IsNullOrWhiteSpace(virtualPath))
        {
            throw new ArgumentException("Virtual path cannot be empty.", nameof(virtualPath));
        }

        var trimmed = virtualPath.Trim();
        if (Path.IsPathRooted(trimmed) || trimmed.StartsWith('\\') || trimmed.StartsWith('/'))
        {
            throw new ArgumentException("Virtual path must be a safe relative path.", nameof(virtualPath));
        }

        var normalized = trimmed.Replace('\\', '/');

        if (normalized.Contains(':', StringComparison.Ordinal)
            || normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException("Virtual path must be a safe relative path.", nameof(virtualPath));
        }

        return normalized.ToLowerInvariant();
    }

    public static string ToSafePhysicalPath(string root, string virtualPath)
    {
        var normalized = Normalize(virtualPath);
        var combined = Path.GetFullPath(Path.Combine(root, Path.Combine(normalized.Split('/'))));
        var fullRoot = Path.GetFullPath(root);

        if (!combined.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(combined, fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Virtual path escapes the target root.", nameof(virtualPath));
        }

        return combined;
    }
}
