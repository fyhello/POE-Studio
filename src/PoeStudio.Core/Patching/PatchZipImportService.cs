using System.IO.Compression;
using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Workspace;

namespace PoeStudio.Core.Patching;

public sealed class PatchZipImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string workspaceRoot;
    private readonly PatchImportAnalyzer analyzer;

    public PatchZipImportService(string workspaceRoot, PatchImportAnalyzer analyzer)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
        this.analyzer = analyzer;
    }

    public async Task<PatchZipImportResponse> ImportAsync(
        PatchZipImportRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProfileId))
        {
            throw new PatchBuildException("missing_profile_id", "客户端配置不能为空。");
        }

        var analysis = await analyzer.AnalyzeZipAsync(
            new PatchZipAnalyzeRequest(request.ZipPath, request.BundleName, request.OodlePath),
            cancellationToken);
        if (!analysis.HasBundlesDirectory || !analysis.HasIndex || !analysis.HasPatchBundle)
        {
            throw new PatchBuildException("patch_zip_not_importable", "补丁包缺少 Bundles2/_.index.bin 或 patch bundle，不能导入。");
        }

        var layout = WorkspaceLayout.ForProfile(workspaceRoot, request.ProfileId);
        layout.EnsureDirectories();
        var buildId = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var outputDirectory = Path.Combine(layout.BuildsRoot, buildId);
        while (Directory.Exists(outputDirectory))
        {
            await Task.Delay(1000, cancellationToken);
            buildId = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            outputDirectory = Path.Combine(layout.BuildsRoot, buildId);
        }

        Directory.CreateDirectory(outputDirectory);
        using var zip = ZipFile.OpenRead(request.ZipPath);
        foreach (var entry in zip.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizeZipPath(entry.FullName);
            if (relative.Contains("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                throw new PatchBuildException("zip_path_escaped", "补丁包包含不安全路径，已停止导入。");
            }

            var target = SafeCombine(outputDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }

        var importedZipPath = Path.Combine(layout.BuildsRoot, $"{buildId}-external-patch.zip");
        if (File.Exists(importedZipPath))
        {
            File.Delete(importedZipPath);
        }

        ZipFile.CreateFromDirectory(outputDirectory, importedZipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
        var importManifestPath = Path.Combine(outputDirectory, "import_manifest.json");
        await WriteJsonAsync(importManifestPath, new PatchZipImportManifestDto(
            request.ProfileId,
            buildId,
            request.ZipPath,
            importedZipPath,
            DateTimeOffset.UtcNow,
            analysis,
            analysis.Warnings), cancellationToken);

        return new PatchZipImportResponse(
            request.ProfileId,
            buildId,
            outputDirectory,
            importedZipPath,
            importManifestPath,
            analysis,
            analysis.Warnings);
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
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
            throw new PatchBuildException("zip_path_escaped", "补丁包包含不安全路径，已停止导入。");
        }

        return fullPath;
    }
}
