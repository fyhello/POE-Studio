using System.IO.Compression;
using PoeStudio.Contracts;
using PoeStudio.Core.Native;
using PoeStudio.Core.Resources;

namespace PoeStudio.Core.Patching;

public sealed class PatchImportAnalyzer
{
    public async Task<PatchZipAnalyzeResponse> AnalyzeZipAsync(
        PatchZipAnalyzeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ZipPath))
        {
            throw new PatchBuildException("missing_zip_path", "补丁包路径不能为空。");
        }

        if (!File.Exists(request.ZipPath))
        {
            throw new PatchBuildException("zip_not_found", "补丁包不存在。");
        }

        var warnings = new List<string>();
        var tempRoot = Path.Combine(Path.GetTempPath(), "poe-studio-patch-analyze", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);
            using var archive = ZipFile.OpenRead(request.ZipPath);
            var entries = archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var bundlesRoot = FindBundlesRoot(entries);
            var indexEntry = bundlesRoot is null ? null : FindEntry(entries, $"{bundlesRoot}_.index.bin");
            var patchBundleName = ResolvePatchBundleName(entries, bundlesRoot, request.BundleName);
            var patchBundleEntry = bundlesRoot is null || patchBundleName is null
                ? null
                : FindEntry(entries, $"{bundlesRoot}{patchBundleName}");

            if (bundlesRoot is null)
            {
                warnings.Add("补丁包中未找到 Bundles2 目录。");
            }

            if (indexEntry is null)
            {
                warnings.Add("补丁包中未找到 Bundles2/_.index.bin。");
            }

            if (patchBundleEntry is null)
            {
                warnings.Add("补丁包中未找到 patch bundle。");
            }

            foreach (var entry in entries)
            {
                var normalized = NormalizeZipPath(entry.FullName);
                if (normalized.Contains("../", StringComparison.Ordinal) || Path.IsPathRooted(normalized))
                {
                    warnings.Add($"跳过不安全路径：{entry.FullName}");
                    continue;
                }

                var target = SafeCombine(tempRoot, normalized);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }

            var extractedBundles = bundlesRoot is null
                ? null
                : Path.Combine(tempRoot, bundlesRoot.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar));
            PatchVerifyResponse? verification = null;
            if (extractedBundles is not null && Directory.Exists(extractedBundles) && patchBundleName is not null)
            {
                var codec = CreateCodec(request.OodlePath);
                try
                {
                    var verify = await new PatchPackageVerifier(codec).VerifyNativeAsync(
                        extractedBundles,
                        patchBundleName,
                        cancellationToken);
                    verification = new PatchVerifyResponse(
                        ProfileId: "external",
                        BuildId: Path.GetFileNameWithoutExtension(request.ZipPath),
                        Ok: verify.Ok,
                        BundlesDirectory: verify.BundlesDirectory,
                        IndexPath: verify.IndexPath,
                        BundlePath: verify.BundlePath,
                        PatchedFileRecords: verify.PatchedFileRecords,
                        Warnings: verify.Warnings);
                    warnings.AddRange(verify.Warnings);
                }
                finally
                {
                    if (codec is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }

            var dtoEntries = entries.Select(entry =>
            {
                var relative = bundlesRoot is not null && NormalizeZipPath(entry.FullName).StartsWith(bundlesRoot, StringComparison.OrdinalIgnoreCase)
                    ? NormalizeZipPath(entry.FullName)[bundlesRoot.Length..]
                    : NormalizeZipPath(entry.FullName);
                var extension = Path.GetExtension(relative).ToLowerInvariant();
                return new PatchZipEntryDto(
                    NormalizeZipPath(entry.FullName),
                    relative,
                    entry.CompressedLength,
                    entry.Length,
                    extension,
                    ResourceClassifier.Classify(relative),
                    PatchRiskClassifier.Classify(relative));
            }).ToArray();

            var ok = bundlesRoot is not null
                && indexEntry is not null
                && patchBundleEntry is not null
                && (verification is null || verification.Ok);

            return new PatchZipAnalyzeResponse(
                request.ZipPath,
                ok,
                DetectTemplate(bundlesRoot),
                bundlesRoot is not null,
                indexEntry is not null,
                patchBundleEntry is not null,
                bundlesRoot,
                indexEntry?.FullName,
                patchBundleEntry?.FullName,
                dtoEntries.Length,
                dtoEntries.Sum(entry => entry.Size),
                dtoEntries,
                CountBy(dtoEntries, entry => entry.Kind),
                CountBy(dtoEntries, entry => entry.RiskLevel),
                verification,
                warnings.Distinct(StringComparer.Ordinal).ToArray());
        }
        catch (InvalidDataException ex)
        {
            throw new PatchBuildException("invalid_zip", $"补丁包不是有效 zip：{ex.Message}");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static ZipArchiveEntry? FindEntry(IReadOnlyList<ZipArchiveEntry> entries, string fullName)
    {
        return entries.FirstOrDefault(entry => string.Equals(NormalizeZipPath(entry.FullName), fullName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindBundlesRoot(IReadOnlyList<ZipArchiveEntry> entries)
    {
        return entries
            .Select(entry => NormalizeZipPath(entry.FullName))
            .Where(name => name.EndsWith("/_.index.bin", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Bundles2/_.index.bin", StringComparison.OrdinalIgnoreCase))
            .Select(name =>
            {
                var index = name.LastIndexOf("Bundles2/_.index.bin", StringComparison.OrdinalIgnoreCase);
                return index < 0 ? null : name[..(index + "Bundles2/".Length)];
            })
            .Where(root => root is not null)
            .OrderBy(root => root!.Length)
            .FirstOrDefault();
    }

    private static string? ResolvePatchBundleName(
        IReadOnlyList<ZipArchiveEntry> entries,
        string? bundlesRoot,
        string preferredBundleName)
    {
        if (bundlesRoot is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredBundleName)
            && FindEntry(entries, $"{bundlesRoot}{preferredBundleName}") is not null)
        {
            return preferredBundleName;
        }

        return entries
            .Select(entry => NormalizeZipPath(entry.FullName))
            .Where(name => name.StartsWith(bundlesRoot, StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".bundle.bin", StringComparison.OrdinalIgnoreCase))
            .Select(name => name[bundlesRoot.Length..])
            .Where(name => !name.Contains('/', StringComparison.Ordinal))
            .OrderByDescending(name => name.Equals("Tiny.V0.1.bundle.bin", StringComparison.OrdinalIgnoreCase))
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static PatchZipTemplate? DetectTemplate(string? bundlesRoot)
    {
        if (bundlesRoot is null)
        {
            return null;
        }

        return bundlesRoot.TrimEnd('/').Equals("PathOfExile2/Bundles2", StringComparison.OrdinalIgnoreCase)
            ? PatchZipTemplate.Epic
            : PatchZipTemplate.Official;
    }

    private static INativeBundleCodec CreateCodec(string? oodlePath)
    {
        if (string.Equals(oodlePath, "__copy__", StringComparison.Ordinal))
        {
            return new CopyNativeBundleCodec();
        }

        var codec = NativeOodleCompressCodec.TryCreate(oodlePath, out _);
        return codec is null ? new UnavailableNativeBundleCodec() : codec;
    }

    private static string NormalizeZipPath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static string SafeCombine(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new PatchBuildException("zip_path_escaped", "补丁包包含不安全路径。");
        }

        return fullPath;
    }

    private static IReadOnlyDictionary<TKey, int> CountBy<TKey>(
        IReadOnlyList<PatchZipEntryDto> entries,
        Func<PatchZipEntryDto, TKey> keySelector)
        where TKey : notnull
    {
        return entries.GroupBy(keySelector).ToDictionary(group => group.Key, group => group.Count());
    }

    private sealed class UnavailableNativeBundleCodec : INativeBundleCodec
    {
        public bool IsAvailable => false;

        public int CompressorId => 0;

        public byte[] Compress(ReadOnlySpan<byte> input)
        {
            throw new NotSupportedException("Native bundle codec is not available.");
        }

        public int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, int compressor)
        {
            throw new NotSupportedException("Native bundle codec is not available.");
        }
    }
}
