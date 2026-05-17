using PoeStudio.Contracts;
using PoeStudio.Core.Resources;

namespace PoeStudio.Core.Native;

public sealed record NativeBundleContentResult(
    bool Ok,
    byte[] Data,
    string? ErrorCode,
    string? Message);

public sealed class NativeBundleResourceContentResolver
{
    private const string NativeScheme = "native-bundles2://";
    private const string GgpkBundles2Scheme = "ggpk-bundles2://";
    private readonly IOodleCodec oodleCodec;

    public NativeBundleResourceContentResolver(IOodleCodec oodleCodec)
    {
        this.oodleCodec = oodleCodec;
    }

    public async Task<NativeBundleContentResult> ReadAsync(
        ClientProfileDto profile,
        ResourceSummaryDto resource,
        CancellationToken cancellationToken)
    {
        return await ReadAsync(profile, resource, oodlePath: null, cancellationToken);
    }

    public async Task<NativeBundleContentResult> ReadAsync(
        ClientProfileDto profile,
        ResourceSummaryDto resource,
        string? oodlePath,
        CancellationToken cancellationToken)
    {
        if (IsGgpkBundles2Resource(resource))
        {
            return await ReadGgpkBundles2Async(resource, oodlePath, cancellationToken);
        }

        if (!TryParse(resource.PhysicalPath, out var bundlePath, out var offset, out var size))
        {
            return Fail("invalid_native_resource_path", "native 资源定位信息不合法。");
        }

        if (string.IsNullOrWhiteSpace(profile.Bundles2Path))
        {
            return Fail("missing_bundles2_path", "客户端配置缺少 Bundles2 目录。");
        }

        string physicalBundlePath;
        try
        {
            physicalBundlePath = ResourcePath.ToSafePhysicalPath(profile.Bundles2Path, bundlePath);
        }
        catch (ArgumentException ex)
        {
            return Fail("invalid_native_bundle_path", ex.Message);
        }

        if (!File.Exists(physicalBundlePath))
        {
            return Fail("native_bundle_missing", "native bundle 文件不存在。");
        }

        var bundleData = await File.ReadAllBytesAsync(physicalBundlePath, cancellationToken);
        using var requestCodec = TryCreateRequestCodec(oodlePath, out var oodleWarning);
        var decompressed = new NativeBundleDecompressor(requestCodec ?? oodleCodec).Decompress(bundleData);
        if (!decompressed.Ok)
        {
            if (decompressed.Status == NativeBundleDecompressStatus.OodleMissing)
            {
                return Fail("native_oodle_missing", oodleWarning ?? decompressed.Warnings.FirstOrDefault() ?? "Oodle 不可用，无法预览 native bundle 资源。");
            }

            return Fail("native_bundle_decompress_failed", decompressed.Warnings.FirstOrDefault() ?? "native bundle 解压失败。");
        }

        if (offset < 0 || size < 0 || offset + size > decompressed.Data.Length)
        {
            return Fail("native_slice_out_of_range", "native 资源切片超出 bundle 解压数据范围。");
        }

        return new NativeBundleContentResult(
            Ok: true,
            decompressed.Data.AsSpan(offset, size).ToArray(),
            ErrorCode: null,
            Message: null);
    }

    public static bool IsNativeResource(ResourceSummaryDto resource)
    {
        return resource.PhysicalPath?.StartsWith(NativeScheme, StringComparison.OrdinalIgnoreCase) == true
            || IsGgpkBundles2Resource(resource);
    }

    private static bool IsGgpkBundles2Resource(ResourceSummaryDto resource)
    {
        return resource.PhysicalPath?.StartsWith(GgpkBundles2Scheme, StringComparison.OrdinalIgnoreCase) == true;
    }

    private async Task<NativeBundleContentResult> ReadGgpkBundles2Async(
        ResourceSummaryDto resource,
        string? oodlePath,
        CancellationToken cancellationToken)
    {
        if (!TryParseGgpkBundles2(resource.PhysicalPath, out var ggpkPath, out var bundleOffset, out var bundleSize, out var offset, out var size))
        {
            return Fail("invalid_ggpk_bundles2_resource_path", "GGPK 内嵌 native 资源定位信息不合法。");
        }

        if (!File.Exists(ggpkPath))
        {
            return Fail("ggpk_file_missing", "Content.ggpk 文件不存在。");
        }

        var bundleData = new byte[bundleSize];
        await using (var stream = File.Open(ggpkPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            stream.Position = bundleOffset;
            var readOffset = 0;
            while (readOffset < bundleData.Length)
            {
                var read = await stream.ReadAsync(bundleData.AsMemory(readOffset), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                readOffset += read;
            }

            if (readOffset != bundleData.Length)
            {
                return Fail("ggpk_bundle_read_incomplete", "GGPK 内嵌 bundle 读取不完整。");
            }
        }

        using var requestCodec = TryCreateRequestCodec(oodlePath, out var oodleWarning);
        var decompressed = new NativeBundleDecompressor(requestCodec ?? oodleCodec).Decompress(bundleData);
        if (!decompressed.Ok)
        {
            if (decompressed.Status == NativeBundleDecompressStatus.OodleMissing)
            {
                return Fail("native_oodle_missing", oodleWarning ?? decompressed.Warnings.FirstOrDefault() ?? "Oodle 不可用，无法预览 GGPK 内嵌 native 资源。");
            }

            return Fail("native_bundle_decompress_failed", decompressed.Warnings.FirstOrDefault() ?? "GGPK 内嵌 native bundle 解压失败。");
        }

        if (offset < 0 || size < 0 || offset + size > decompressed.Data.Length)
        {
            return Fail("native_slice_out_of_range", "GGPK 内嵌 native 资源切片超出 bundle 解压数据范围。");
        }

        return new NativeBundleContentResult(
            Ok: true,
            decompressed.Data.AsSpan(offset, size).ToArray(),
            ErrorCode: null,
            Message: null);
    }

    private static bool TryParse(string? physicalPath, out string bundlePath, out int offset, out int size)
    {
        bundlePath = string.Empty;
        offset = 0;
        size = 0;

        if (string.IsNullOrWhiteSpace(physicalPath) || !physicalPath.StartsWith(NativeScheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = physicalPath[NativeScheme.Length..];
        var hashIndex = rest.IndexOf('#');
        if (hashIndex <= 0 || hashIndex == rest.Length - 1)
        {
            return false;
        }

        bundlePath = rest[..hashIndex];
        var query = rest[(hashIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in query)
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            if (pair[0].Equals("offset", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(pair[1], out offset);
            }
            else if (pair[0].Equals("size", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(pair[1], out size);
            }
        }

        return offset >= 0 && size >= 0 && !string.IsNullOrWhiteSpace(bundlePath);
    }

    private static bool TryParseGgpkBundles2(
        string? physicalPath,
        out string ggpkPath,
        out long bundleOffset,
        out int bundleSize,
        out int offset,
        out int size)
    {
        ggpkPath = string.Empty;
        bundleOffset = 0;
        bundleSize = 0;
        offset = 0;
        size = 0;

        if (string.IsNullOrWhiteSpace(physicalPath) || !physicalPath.StartsWith(GgpkBundles2Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = physicalPath[GgpkBundles2Scheme.Length..];
        var hashIndex = rest.IndexOf('#');
        if (hashIndex <= 0 || hashIndex == rest.Length - 1)
        {
            return false;
        }

        ggpkPath = rest[..hashIndex];
        var query = rest[(hashIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in query)
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            if (pair[0].Equals("bundleOffset", StringComparison.OrdinalIgnoreCase))
            {
                long.TryParse(pair[1], out bundleOffset);
            }
            else if (pair[0].Equals("bundleSize", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(pair[1], out bundleSize);
            }
            else if (pair[0].Equals("offset", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(pair[1], out offset);
            }
            else if (pair[0].Equals("size", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(pair[1], out size);
            }
        }

        return bundleOffset >= 0 && bundleSize >= 0 && offset >= 0 && size >= 0 && !string.IsNullOrWhiteSpace(ggpkPath);
    }

    private static IDisposableOodleCodec? TryCreateRequestCodec(string? oodlePath, out string? warning)
    {
        warning = null;
        if (string.IsNullOrWhiteSpace(oodlePath))
        {
            return null;
        }

        if (!File.Exists(oodlePath))
        {
            warning = $"指定的 oo2core.dll 不存在：{oodlePath}";
            return null;
        }

        try
        {
            return new IDisposableOodleCodec(new NativeOodleCodec(oodlePath));
        }
        catch (Exception ex) when (ex is FileNotFoundException or EntryPointNotFoundException or BadImageFormatException or DllNotFoundException)
        {
            warning = $"无法加载 oo2core.dll：{ex.Message}";
            return null;
        }
    }

    private static NativeBundleContentResult Fail(string errorCode, string message)
    {
        return new NativeBundleContentResult(false, [], errorCode, message);
    }

    private sealed class IDisposableOodleCodec(IOodleCodec inner) : IOodleCodec, IDisposable
    {
        public bool IsAvailable => inner.IsAvailable;

        public int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, int compressor)
        {
            return inner.Decompress(compressed, output, compressor);
        }

        public void Dispose()
        {
            if (inner is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
