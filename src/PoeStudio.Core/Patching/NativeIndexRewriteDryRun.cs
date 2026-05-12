using System.Globalization;
using System.Text;
using PoeStudio.Contracts;
using PoeStudio.Core.Native;

namespace PoeStudio.Core.Patching;

public sealed record NativeIndexRewriteDryRunResult(
    bool Ok,
    string SourcePath,
    string OutputPath,
    int UpdatedRecords,
    IReadOnlyList<string> Warnings);

public sealed class NativeIndexRewriteDryRun
{
    public async Task<NativeIndexRewriteDryRunResult> RewriteAsync(
        string sourceDecompressedIndexPath,
        string outputDecompressedIndexPath,
        NativeIndexRewritePlanResponse plan,
        CancellationToken cancellationToken)
    {
        if (!plan.Ready)
        {
            return Failed(sourceDecompressedIndexPath, outputDecompressedIndexPath, 0, ["Native index 重写计划未就绪。"]);
        }

        var parsed = await new NativeIndexRecordParser().ParseAsync(sourceDecompressedIndexPath, cancellationToken);
        if (!parsed.Ok)
        {
            return Failed(sourceDecompressedIndexPath, outputDecompressedIndexPath, 0, parsed.Warnings);
        }

        var warnings = new List<string>();
        var bundles = parsed.Bundles.ToList();
        var files = parsed.Files.ToList();
        var updated = 0;

        foreach (var item in plan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Blocker is not null)
            {
                warnings.Add($"{item.VirtualPath}: {item.Blocker}");
                continue;
            }

            if (!TryParsePathHash(item.PathHash, out var pathHash))
            {
                warnings.Add($"{item.VirtualPath}: 缺少可写入的 path hash。");
                continue;
            }

            var fileIndex = files.FindIndex(file => file.PathHash == pathHash);
            if (fileIndex < 0)
            {
                warnings.Add($"{item.VirtualPath}: index 中找不到 path hash。");
                continue;
            }

            var current = files[fileIndex];
            var currentBundleName = BundleRecordToFileName(parsed.Bundles[current.BundleIndex].Path);
            if (!string.Equals(currentBundleName, item.OriginalBundleName, StringComparison.OrdinalIgnoreCase)
                || current.Offset != item.OriginalOffset
                || current.Size != item.OriginalSize)
            {
                warnings.Add($"{item.VirtualPath}: 原始记录不匹配，已停止 dry rewrite。");
                continue;
            }

            var targetBundlePath = BundleFileNameToRecordPath(item.BundleName);
            var targetBundleIndex = bundles.FindIndex(bundle => string.Equals(bundle.Path, targetBundlePath, StringComparison.OrdinalIgnoreCase));
            if (targetBundleIndex < 0)
            {
                targetBundleIndex = bundles.Count;
                bundles.Add(new NativeBundleRecord(targetBundleIndex, targetBundlePath, checked((int)Math.Min(int.MaxValue, item.Offset + item.Size))));
            }

            files[fileIndex] = new NativeFileRecord(current.PathHash, targetBundleIndex, checked((int)item.Offset), checked((int)item.Size));
            updated++;
        }

        if (warnings.Count > 0)
        {
            if (File.Exists(outputDecompressedIndexPath))
            {
                File.Delete(outputDecompressedIndexPath);
            }

            return Failed(sourceDecompressedIndexPath, outputDecompressedIndexPath, updated, warnings);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputDecompressedIndexPath)!);
        await using var stream = File.Create(outputDecompressedIndexPath);
        await using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(bundles.Count);
        foreach (var bundle in bundles.OrderBy(bundle => bundle.Index))
        {
            WriteBundle(writer, bundle.Path, bundle.UncompressedSize);
        }

        writer.Write(files.Count);
        foreach (var file in files)
        {
            writer.Write(file.PathHash);
            writer.Write(file.BundleIndex);
            writer.Write(file.Offset);
            writer.Write(file.Size);
        }

        writer.Write(parsed.DirectoryCount);
        foreach (var directory in parsed.Directories)
        {
            writer.Write(directory.PathHash);
            writer.Write(directory.Offset);
            writer.Write(directory.Size);
            writer.Write(directory.RecursiveSize);
        }

        return new NativeIndexRewriteDryRunResult(true, sourceDecompressedIndexPath, outputDecompressedIndexPath, updated, []);
    }

    private static NativeIndexRewriteDryRunResult Failed(
        string sourcePath,
        string outputPath,
        int updatedRecords,
        IReadOnlyList<string> warnings)
    {
        return new NativeIndexRewriteDryRunResult(false, sourcePath, outputPath, updatedRecords, warnings);
    }

    private static void WriteBundle(BinaryWriter writer, string path, int uncompressedSize)
    {
        var bytes = Encoding.UTF8.GetBytes(path);
        writer.Write(bytes.Length);
        writer.Write(bytes);
        writer.Write(uncompressedSize);
    }

    private static bool TryParsePathHash(string? value, out ulong pathHash)
    {
        pathHash = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        return ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out pathHash)
            || ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out pathHash);
    }

    private static string BundleRecordToFileName(string bundlePath)
    {
        return bundlePath.EndsWith(".bundle.bin", StringComparison.OrdinalIgnoreCase)
            ? bundlePath
            : $"{bundlePath}.bundle.bin";
    }

    private static string BundleFileNameToRecordPath(string bundleName)
    {
        return bundleName.EndsWith(".bundle.bin", StringComparison.OrdinalIgnoreCase)
            ? bundleName[..^".bundle.bin".Length]
            : bundleName;
    }
}
