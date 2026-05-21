using System.Security.Cryptography;
using System.Text;
using PoeStudio.Contracts;
using PoeStudio.Core.Oodle;

namespace PoeStudio.Core.ClientDetection;

public static class ClientDetector
{
    public static ClientDetectionResult Detect(string rootPath, string? oodleSearchPath = null)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            warnings.Add("客户端根路径不能为空。");
            return BuildResult(
                detected: false,
                platform: ClientPlatform.Unknown,
                entryKind: ClientEntryKind.Unknown,
                rootPath: string.Empty,
                contentGgpkPath: null,
                bundles2Path: null,
                indexPath: null,
                new OodleDetectionResult(OodleStatus.Missing, null),
                warnings);
        }

        var fullRoot = Path.GetFullPath(rootPath);
        var contentGgpk = Path.Combine(fullRoot, "Content.ggpk");
        var bundles2 = Path.Combine(fullRoot, "Bundles2");
        var index = Path.Combine(bundles2, "_.index.bin");
        var oodle = OodleDetector.Detect(fullRoot, oodleSearchPath);

        if (File.Exists(contentGgpk))
        {
            return BuildResult(
                detected: true,
                platform: DetectPlatform(fullRoot),
                entryKind: ClientEntryKind.Ggpk,
                rootPath: fullRoot,
                contentGgpkPath: contentGgpk,
                bundles2Path: Directory.Exists(bundles2) ? bundles2 : null,
                indexPath: File.Exists(index) ? index : null,
                oodle,
                warnings);
        }

        if (File.Exists(index))
        {
            return BuildResult(
                detected: true,
                platform: DetectPlatform(fullRoot),
                entryKind: ClientEntryKind.Bundles2,
                rootPath: fullRoot,
                contentGgpkPath: null,
                bundles2Path: bundles2,
                indexPath: index,
                oodle,
                warnings);
        }

        warnings.Add("未找到 Content.ggpk 或 Bundles2/_.index.bin。");
        return BuildResult(
            detected: false,
            platform: ClientPlatform.Unknown,
            entryKind: ClientEntryKind.Unknown,
            rootPath: fullRoot,
            contentGgpkPath: null,
            bundles2Path: Directory.Exists(bundles2) ? bundles2 : null,
            indexPath: null,
            oodle,
            warnings);
    }

    private static ClientDetectionResult BuildResult(
        bool detected,
        ClientPlatform platform,
        ClientEntryKind entryKind,
        string rootPath,
        string? contentGgpkPath,
        string? bundles2Path,
        string? indexPath,
        OodleDetectionResult oodle,
        IReadOnlyList<string> warnings)
    {
        var fingerprint = BuildFingerprint(rootPath, contentGgpkPath, indexPath);
        return new ClientDetectionResult(
            detected,
            platform,
            entryKind,
            rootPath,
            contentGgpkPath,
            bundles2Path,
            indexPath,
            oodle.Status,
            oodle.Path,
            fingerprint,
            warnings);
    }

    private static ClientPlatform DetectPlatform(string rootPath)
    {
        if (rootPath.Contains("WeGame", StringComparison.OrdinalIgnoreCase) ||
            rootPath.Contains("rail_apps", StringComparison.OrdinalIgnoreCase))
        {
            return ClientPlatform.WeGame;
        }

        return ClientPlatform.Official;
    }

    private static string BuildFingerprint(string rootPath, string? contentGgpkPath, string? indexPath)
    {
        var builder = new StringBuilder();
        builder.Append(rootPath);
        AppendFileSignature(builder, contentGgpkPath);
        AppendFileSignature(builder, indexPath);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..16].ToLowerInvariant();
    }

    private static void AppendFileSignature(StringBuilder builder, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            builder.Append("|missing");
            return;
        }

        var info = new FileInfo(path);
        builder.Append('|').Append(info.FullName).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks);
    }
}
