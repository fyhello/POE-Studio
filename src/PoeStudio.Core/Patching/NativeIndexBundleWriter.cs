using PoeStudio.Core.Native;

namespace PoeStudio.Core.Patching;

public sealed record NativeIndexBundleWriteResult(
    string IndexPath,
    long UncompressedSize,
    long CompressedSize);

public sealed class NativeIndexBundleWriter
{
    public async Task<NativeIndexBundleWriteResult> WriteAsync(
        string decompressedIndexPath,
        string outputIndexPath,
        INativeBundleCodec codec,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(decompressedIndexPath))
        {
            throw new FileNotFoundException("重写后的解压 index 不存在。", decompressedIndexPath);
        }

        var payload = await File.ReadAllBytesAsync(decompressedIndexPath, cancellationToken);
        var bundle = new NativeBundleCompressor(codec).Compress(payload);
        Directory.CreateDirectory(Path.GetDirectoryName(outputIndexPath)!);
        await File.WriteAllBytesAsync(outputIndexPath, bundle, cancellationToken);
        return new NativeIndexBundleWriteResult(outputIndexPath, payload.LongLength, bundle.LongLength);
    }
}
