using System.IO.Compression;
using System.Security.Cryptography;
using PoeStudio.Contracts;

namespace PoeStudio.Core.Patching;

public sealed class PatchZipInstallPreviewService
{
    private readonly PatchImportAnalyzer analyzer;

    public PatchZipInstallPreviewService(PatchImportAnalyzer analyzer)
    {
        this.analyzer = analyzer;
    }

    public async Task<PatchZipInstallPreviewResponse> PreviewAsync(
        PatchZipInstallPreviewRequest request,
        ClientProfileDto profile,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.Bundles2Path))
        {
            throw new PatchBuildException("missing_bundles2_path", "客户端配置缺少 Bundles2 路径。");
        }

        var analysis = await analyzer.AnalyzeZipAsync(
            new PatchZipAnalyzeRequest(request.ZipPath, request.BundleName, request.OodlePath),
            cancellationToken);
        if (!analysis.HasBundlesDirectory || string.IsNullOrWhiteSpace(analysis.BundlesRoot))
        {
            throw new PatchBuildException("patch_zip_not_installable", "补丁包中未找到可安装的 Bundles2 目录。");
        }

        using var zip = ZipFile.OpenRead(request.ZipPath);
        var files = new List<PatchZipInstallPreviewFileDto>();
        var warnings = new List<string>(analysis.Warnings);
        foreach (var entry in zip.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullName = NormalizeZipPath(entry.FullName);
            if (!fullName.StartsWith(analysis.BundlesRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = fullName[analysis.BundlesRoot.Length..];
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var targetPath = SafeCombine(profile.Bundles2Path, relative);
            var targetExists = File.Exists(targetPath);
            long? targetSize = targetExists ? new FileInfo(targetPath).Length : null;
            var sameSize = targetSize == entry.Length;
            bool? sameHash = null;
            if (targetExists && sameSize)
            {
                await using var source = entry.Open();
                await using var target = File.OpenRead(targetPath);
                sameHash = await HashAsync(source, cancellationToken) == await HashAsync(target, cancellationToken);
            }

            files.Add(new PatchZipInstallPreviewFileDto(
                relative,
                fullName,
                targetPath,
                entry.Length,
                targetExists,
                targetSize,
                sameSize,
                sameHash,
                PatchRiskClassifier.Classify(relative)));
        }

        var newFiles = files.Count(file => !file.TargetExists);
        var sameFiles = files.Count(file => file.SameHash == true);
        var replacedFiles = files.Count - newFiles - sameFiles;
        var highRiskFiles = files.Count(file => file.RiskLevel == PatchRiskLevel.High);
        if (replacedFiles > 0)
        {
            warnings.Add($"将覆盖 {replacedFiles} 个已有 Bundles2 文件。");
        }

        return new PatchZipInstallPreviewResponse(
            request.ProfileId,
            request.ZipPath,
            analysis.Ok,
            files.Count,
            newFiles,
            replacedFiles,
            sameFiles,
            highRiskFiles,
            files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            analysis,
            warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static async Task<string> HashAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
            throw new PatchBuildException("zip_path_escaped", "补丁包包含不安全路径，已停止预检。");
        }

        return fullPath;
    }
}
