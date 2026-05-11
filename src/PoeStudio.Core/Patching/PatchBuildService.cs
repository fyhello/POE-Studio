using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Resources;
using PoeStudio.Core.Workspace;

namespace PoeStudio.Core.Patching;

public interface IPatchOverlayReader
{
    Task<IReadOnlyList<OverlayEntryDto>> GetEntriesAsync(string profileId, CancellationToken cancellationToken);
}

public sealed class PatchBuildService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string workspaceRoot;
    private readonly IPatchOverlayReader overlayStore;

    public PatchBuildService(string workspaceRoot, IPatchOverlayReader overlayStore)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
        this.overlayStore = overlayStore;
    }

    public async Task<PatchDryRunResponse> DryRunAsync(
        PatchDryRunRequest request,
        ClientProfileDto profile,
        CancellationToken cancellationToken)
    {
        var changes = await BuildChangesAsync(request.ProfileId, cancellationToken);
        var warnings = BuildWarnings(profile, changes);
        return new PatchDryRunResponse(
            request.ProfileId,
            changes.Count,
            changes,
            CountBy(changes, change => change.Kind),
            CountBy(changes, change => change.RiskLevel),
            warnings);
    }

    public async Task<PatchBuildResponse> BuildAsync(
        PatchBuildRequest request,
        ClientProfileDto profile,
        CancellationToken cancellationToken)
    {
        var dryRun = await DryRunAsync(new PatchDryRunRequest(request.ProfileId), profile, cancellationToken);
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, request.ProfileId);
        layout.EnsureDirectories();

        var buildId = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var outputDirectory = Path.Combine(layout.BuildsRoot, buildId);
        var rootPrefix = GetTemplateRoot(request.Template);
        var bundlesDirectory = string.IsNullOrEmpty(rootPrefix)
            ? Path.Combine(outputDirectory, "Bundles2")
            : Path.Combine(outputDirectory, rootPrefix, "Bundles2");
        Directory.CreateDirectory(bundlesDirectory);

        var indexPath = Path.Combine(bundlesDirectory, "_.index.bin");
        if (!string.IsNullOrWhiteSpace(profile.IndexPath) && File.Exists(profile.IndexPath))
        {
            File.Copy(profile.IndexPath, indexPath, overwrite: true);
        }
        else
        {
            await File.WriteAllBytesAsync(indexPath, Array.Empty<byte>(), cancellationToken);
        }

        var bundlePath = Path.Combine(bundlesDirectory, request.BundleName);
        await WriteMvpBundleAsync(bundlePath, await overlayStore.GetEntriesAsync(request.ProfileId, cancellationToken), cancellationToken);

        var builtAt = DateTimeOffset.UtcNow;
        var manifestPath = Path.Combine(outputDirectory, "patch_manifest.json");
        var manifest = new PatchManifestDto(
            request.ProfileId,
            PatchBuildMode.OverlayBundleMvp,
            request.Template,
            request.BundleName,
            builtAt,
            dryRun.Changes,
            dryRun.Warnings);
        await WriteJsonAsync(manifestPath, manifest, cancellationToken);

        var rollbackManifestPath = Path.Combine(outputDirectory, "rollback_manifest.json");
        var rollback = new PatchRollbackManifestDto(
            request.ProfileId,
            builtAt,
            dryRun.Changes.Select(change => new PatchRollbackItemDto(change.VirtualPath, change.BaseHash, change.OverlayHash)).ToArray());
        await WriteJsonAsync(rollbackManifestPath, rollback, cancellationToken);

        var zipPath = Path.Combine(layout.BuildsRoot, $"{buildId}-{request.Template.ToString().ToLowerInvariant()}-patch.zip");
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(outputDirectory, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);

        return new PatchBuildResponse(
            request.ProfileId,
            PatchBuildMode.OverlayBundleMvp,
            request.Template,
            outputDirectory,
            indexPath,
            bundlePath,
            manifestPath,
            rollbackManifestPath,
            zipPath,
            dryRun.TotalChanges,
            dryRun.Warnings);
    }

    private async Task<IReadOnlyList<PatchChangeDto>> BuildChangesAsync(string profileId, CancellationToken cancellationToken)
    {
        var entries = await overlayStore.GetEntriesAsync(profileId, cancellationToken);
        return entries.Select(entry =>
        {
            var extension = Path.GetExtension(entry.VirtualPath).ToLowerInvariant();
            return new PatchChangeDto(
                entry.VirtualPath,
                extension,
                ResourceClassifier.Classify(entry.VirtualPath),
                PatchRiskClassifier.Classify(entry.VirtualPath),
                entry.OverlaySize,
                entry.OverlayHash,
                entry.BaseHash);
        }).OrderBy(change => change.VirtualPath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> BuildWarnings(ClientProfileDto profile, IReadOnlyList<PatchChangeDto> changes)
    {
        var warnings = new List<string>
        {
            "当前构建模式为 OverlayBundleMvp，用于工作流验证和审计；真实 Bundles2 index 重写将在 Native Kernel/LibGGPK3 Adapter 阶段接入。"
        };

        if (profile.OodleStatus != OodleStatus.Found)
        {
            warnings.Add("未检测到 Oodle，正式压缩和真实 bundle 写入会被阻止。");
        }

        if (changes.Any(change => change.RiskLevel == PatchRiskLevel.High))
        {
            warnings.Add("包含高风险资源类型，正式构建前需要用户确认。");
        }

        return warnings;
    }

    private static async Task WriteMvpBundleAsync(
        string bundlePath,
        IReadOnlyList<OverlayEntryDto> entries,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(bundlePath);
        await using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("POESTUDIO-MVP-BUNDLE\0"));
        writer.Write(entries.Count);

        foreach (var entry in entries.OrderBy(item => item.NormalizedPath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pathBytes = Encoding.UTF8.GetBytes(entry.NormalizedPath);
            var content = await File.ReadAllBytesAsync(entry.OverlayPath, cancellationToken);
            writer.Write(pathBytes.Length);
            writer.Write(pathBytes);
            writer.Write(content.Length);
            writer.Write(content);
        }
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
    }

    private static string GetTemplateRoot(PatchZipTemplate template)
    {
        return template == PatchZipTemplate.Epic ? "PathOfExile2" : string.Empty;
    }

    private static IReadOnlyDictionary<TKey, int> CountBy<TKey>(
        IReadOnlyList<PatchChangeDto> changes,
        Func<PatchChangeDto, TKey> keySelector)
        where TKey : notnull
    {
        return changes.GroupBy(keySelector).ToDictionary(group => group.Key, group => group.Count());
    }
}
