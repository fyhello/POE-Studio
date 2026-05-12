namespace PoeStudio.Core.Patching;

public sealed record NativeBundleLocation(string BundleName, long Offset, long Size);

public static class NativeBundleLocationParser
{
    private const string Scheme = "native-bundles2://";

    public static bool TryParse(string? physicalPath, out NativeBundleLocation location)
    {
        location = default!;
        if (string.IsNullOrWhiteSpace(physicalPath)
            || !physicalPath.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var payload = physicalPath[Scheme.Length..];
        var hashIndex = payload.IndexOf('#');
        if (hashIndex <= 0 || hashIndex == payload.Length - 1)
        {
            return false;
        }

        var bundleName = Uri.UnescapeDataString(payload[..hashIndex]);
        var query = payload[(hashIndex + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

        if (!query.TryGetValue("offset", out var offsetText)
            || !query.TryGetValue("size", out var sizeText)
            || !long.TryParse(offsetText, out var offset)
            || !long.TryParse(sizeText, out var size)
            || offset < 0
            || size < 0)
        {
            return false;
        }

        location = new NativeBundleLocation(bundleName, offset, size);
        return true;
    }
}
