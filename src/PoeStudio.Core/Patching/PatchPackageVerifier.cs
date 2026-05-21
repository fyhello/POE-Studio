using PoeStudio.Core.Native;

namespace PoeStudio.Core.Patching;

public sealed record PatchPackageVerifyResult(
    bool Ok,
    string BundlesDirectory,
    string IndexPath,
    string BundlePath,
    int PatchedFileRecords,
    IReadOnlyList<string> Warnings);

public sealed class PatchPackageVerifier
{
    private readonly IOodleCodec codec;

    public PatchPackageVerifier(IOodleCodec codec)
    {
        this.codec = codec;
    }

    public async Task<PatchPackageVerifyResult> VerifyNativeAsync(
        string bundlesDirectory,
        string patchBundleName,
        CancellationToken cancellationToken)
    {
        var indexPath = Path.Combine(bundlesDirectory, "_.index.bin");
        var bundlePath = Path.Combine(bundlesDirectory, patchBundleName);
        var warnings = new List<string>();
        if (!File.Exists(indexPath))
        {
            warnings.Add("缺少 _.index.bin。");
        }

        if (!File.Exists(bundlePath))
        {
            warnings.Add($"缺少 patch bundle：{patchBundleName}");
        }

        if (warnings.Count > 0)
        {
            return Result(false, bundlesDirectory, indexPath, bundlePath, 0, warnings);
        }

        if (!codec.IsAvailable)
        {
            warnings.Add("Native bundle codec 不可用，无法验证压缩后的补丁包。");
            return Result(false, bundlesDirectory, indexPath, bundlePath, 0, warnings);
        }

        var decompressor = new NativeBundleDecompressor(codec);
        var patchBundle = decompressor.Decompress(await File.ReadAllBytesAsync(bundlePath, cancellationToken));
        if (!patchBundle.Ok)
        {
            warnings.AddRange(patchBundle.Warnings);
        }

        var indexBundle = decompressor.Decompress(await File.ReadAllBytesAsync(indexPath, cancellationToken));
        if (!indexBundle.Ok)
        {
            warnings.AddRange(indexBundle.Warnings);
            return Result(false, bundlesDirectory, indexPath, bundlePath, 0, warnings);
        }

        var tempIndexPath = Path.Combine(Path.GetTempPath(), "poe-studio-package-verify", $"{Guid.NewGuid():N}.index.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(tempIndexPath)!);
        try
        {
            await File.WriteAllBytesAsync(tempIndexPath, indexBundle.Data, cancellationToken);
            var parsed = await new NativeIndexRecordParser().ParseAsync(tempIndexPath, cancellationToken);
            if (!parsed.Ok)
            {
                warnings.AddRange(parsed.Warnings);
                return Result(false, bundlesDirectory, indexPath, bundlePath, 0, warnings);
            }

            var patchBundleRecord = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(patchBundleName));
            var patchBundleIndexes = parsed.Bundles
                .Where(bundle => string.Equals(bundle.Path, patchBundleRecord, StringComparison.OrdinalIgnoreCase))
                .Select(bundle => bundle.Index)
                .ToHashSet();
            var patchBundleLength = patchBundle.Data.LongLength;
            foreach (var bundle in parsed.Bundles.Where(bundle => patchBundleIndexes.Contains(bundle.Index)))
            {
                if (bundle.UncompressedSize != patchBundleLength)
                {
                    warnings.Add($"patch bundle record 解压长度不匹配：bundle={bundle.Path}, indexSize={bundle.UncompressedSize}, actualSize={patchBundleLength}。");
                }
            }

            var patchedRecords = 0;
            var invalidBounds = 0;
            foreach (var file in parsed.Files)
            {
                if (!patchBundleIndexes.Contains(file.BundleIndex))
                {
                    continue;
                }

                patchedRecords++;
                var end = (long)file.Offset + file.Size;
                if (end > patchBundleLength)
                {
                    invalidBounds++;
                    if (invalidBounds <= 10)
                    {
                        warnings.Add($"file record 0x{file.PathHash:x16} 超出 patch bundle 解压长度：offset={file.Offset}, size={file.Size}, end={end}, bundleLength={patchBundleLength}。");
                    }
                }
            }

            if (invalidBounds > 10)
            {
                warnings.Add($"另有 {invalidBounds - 10} 条 file record 超出 patch bundle 解压长度。");
            }

            if (patchedRecords == 0)
            {
                warnings.Add("index 中没有 file record 指向 patch bundle。");
            }

            return Result(warnings.Count == 0, bundlesDirectory, indexPath, bundlePath, patchedRecords, warnings);
        }
        finally
        {
            if (File.Exists(tempIndexPath))
            {
                File.Delete(tempIndexPath);
            }
        }
    }

    private static PatchPackageVerifyResult Result(
        bool ok,
        string bundlesDirectory,
        string indexPath,
        string bundlePath,
        int patchedRecords,
        IReadOnlyList<string> warnings)
    {
        return new PatchPackageVerifyResult(ok, bundlesDirectory, indexPath, bundlePath, patchedRecords, warnings);
    }
}
