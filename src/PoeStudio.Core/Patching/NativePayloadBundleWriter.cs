using PoeStudio.Contracts;
using PoeStudio.Core.Native;

namespace PoeStudio.Core.Patching;

public sealed record NativePayloadBundleWriteResult(
    string BundlePath,
    long UncompressedSize,
    long CompressedSize);

public sealed class NativePayloadBundleWriter
{
    public async Task<NativePayloadBundleWriteResult> WriteAsync(
        string outputDirectory,
        NativePatchPlanResponse plan,
        IReadOnlyList<OverlayEntryDto> entries,
        INativeBundleCodec codec,
        byte[]? prefixPayload,
        CancellationToken cancellationToken)
    {
        if (!plan.Ready)
        {
            throw new InvalidOperationException("Native 写包计划未就绪。");
        }

        Directory.CreateDirectory(outputDirectory);
        var bundlePath = Path.Combine(outputDirectory, plan.BundleName);
        var byPath = entries.ToDictionary(item => item.NormalizedPath, StringComparer.OrdinalIgnoreCase);
        await using var payload = new MemoryStream();
        if (prefixPayload is { Length: > 0 })
        {
            await payload.WriteAsync(prefixPayload, cancellationToken);
        }

        foreach (var item in plan.Items.OrderBy(item => item.Offset))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (payload.Position != item.Offset)
            {
                throw new InvalidDataException($"Native bundle offset 不连续：expected={payload.Position}, actual={item.Offset}。");
            }

            if (!byPath.TryGetValue(item.VirtualPath, out var entry) || !File.Exists(entry.OverlayPath))
            {
                throw new InvalidOperationException($"找不到 overlay entry：{item.VirtualPath}");
            }

            var content = await File.ReadAllBytesAsync(entry.OverlayPath, cancellationToken);
            if (content.LongLength != item.Size)
            {
                throw new InvalidDataException($"overlay 大小与计划不一致：{item.VirtualPath}");
            }

            await payload.WriteAsync(content, cancellationToken);
        }

        var compressed = new NativeBundleCompressor(codec).Compress(payload.ToArray(), headerUnknown: 1);
        await File.WriteAllBytesAsync(bundlePath, compressed, cancellationToken);
        return new NativePayloadBundleWriteResult(bundlePath, payload.Length, compressed.LongLength);
    }

    public Task<NativePayloadBundleWriteResult> WriteAsync(
        string outputDirectory,
        NativePatchPlanResponse plan,
        IReadOnlyList<OverlayEntryDto> entries,
        INativeBundleCodec codec,
        CancellationToken cancellationToken)
    {
        return WriteAsync(outputDirectory, plan, entries, codec, prefixPayload: null, cancellationToken);
    }
}
