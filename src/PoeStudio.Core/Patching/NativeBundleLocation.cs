namespace PoeStudio.Core.Patching;

public sealed record NativeBundleLocation(string BundleName, long Offset, long Size);

public static class NativeBundleLocationParser
{
    private const string NativeScheme = "native-bundles2://";
    private const string GgpkBundles2Scheme = "ggpk-bundles2://";

    public static bool TryParse(string? physicalPath, out NativeBundleLocation location)
    {
        location = default!;
        if (string.IsNullOrWhiteSpace(physicalPath))
        {
            return false;
        }

        if (physicalPath.StartsWith(NativeScheme, StringComparison.OrdinalIgnoreCase))
        {
            return TryParseNative(physicalPath[NativeScheme.Length..], out location);
        }

        if (physicalPath.StartsWith(GgpkBundles2Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return TryParseGgpkBundles2(physicalPath[GgpkBundles2Scheme.Length..], out location);
        }

        return false;
    }

    private static bool TryParseNative(string payload, out NativeBundleLocation location)
    {
        location = default!;
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

    private static bool TryParseGgpkBundles2(string payload, out NativeBundleLocation location)
    {
        location = default!;
        var hashIndex = payload.IndexOf('#');
        if (hashIndex <= 0 || hashIndex == payload.Length - 1)
        {
            return false;
        }

        var query = payload[(hashIndex + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

        if (!query.TryGetValue("bundlePath", out var bundlePathText)
            || !query.TryGetValue("offset", out var offsetText)
            || !query.TryGetValue("size", out var sizeText)
            || !long.TryParse(offsetText, out var offset)
            || !long.TryParse(sizeText, out var size)
            || offset < 0
            || size < 0)
        {
            return false;
        }

        var bundlePath = Uri.UnescapeDataString(bundlePathText).Replace('\\', '/');
        var bundleName = bundlePath.StartsWith("bundles2/", StringComparison.OrdinalIgnoreCase)
            ? bundlePath["bundles2/".Length..]
            : bundlePath;
        location = new NativeBundleLocation(bundleName, offset, size);
        return true;
    }
}
