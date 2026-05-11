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
        return resource.PhysicalPath?.StartsWith(NativeScheme, StringComparison.OrdinalIgnoreCase) == true;
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
