using System.IO.Compression;
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
    private readonly IReadOnlyDictionary<PatchPackageWriterKind, IPatchPackageWriter> packageWriters;

    public PatchBuildService(string workspaceRoot, IPatchOverlayReader overlayStore)
        : this(workspaceRoot, overlayStore, [new MvpPatchPackageWriter(), CreateNativeUnavailableWriter(), CreateLibGgpkUnavailableWriter()])
    {
    }

    public PatchBuildService(string workspaceRoot, IPatchOverlayReader overlayStore, IPatchPackageWriter packageWriter)
        : this(workspaceRoot, overlayStore, [packageWriter])
    {
    }

    public PatchBuildService(string workspaceRoot, IPatchOverlayReader overlayStore, IEnumerable<IPatchPackageWriter> packageWriters)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
        this.overlayStore = overlayStore;
        this.packageWriters = packageWriters.ToDictionary(writer => writer.Kind);
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
        var overlayEntries = await overlayStore.GetEntriesAsync(request.ProfileId, cancellationToken);
        var dryRun = await DryRunAsync(new PatchDryRunRequest(request.ProfileId), profile, cancellationToken);
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, request.ProfileId);
        layout.EnsureDirectories();

        var buildId = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var outputDirectory = Path.Combine(layout.BuildsRoot, buildId);
        var rootPrefix = GetTemplateRoot(request.Template);
        var bundlesDirectory = string.IsNullOrEmpty(rootPrefix)
            ? Path.Combine(outputDirectory, "Bundles2")
            : Path.Combine(outputDirectory, rootPrefix, "Bundles2");

        if (!packageWriters.TryGetValue(request.WriterKind, out var writer))
        {
            throw new PatchBuildException("patch_writer_unavailable", $"补丁写入器不可用：{request.WriterKind}");
        }

        var writeResult = await writer.WriteAsync(
            new PatchPackageWriterContext(profile, request, bundlesDirectory, overlayEntries, dryRun.Changes),
            cancellationToken);

        var builtAt = DateTimeOffset.UtcNow;
        var manifestPath = Path.Combine(outputDirectory, "patch_manifest.json");
        var warnings = dryRun.Warnings.Concat(writeResult.Warnings).Distinct(StringComparer.Ordinal).ToArray();
        var manifest = new PatchManifestDto(
            request.ProfileId,
            writeResult.BuildMode,
            request.Template,
            request.BundleName,
            builtAt,
            dryRun.Changes,
            warnings);
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
            writeResult.BuildMode,
            request.Template,
            outputDirectory,
            writeResult.IndexPath,
            writeResult.BundlePath,
            manifestPath,
            rollbackManifestPath,
            zipPath,
            dryRun.TotalChanges,
            warnings);
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
            "构建前会执行 dry-run 汇总，真实写包能力由当前 PatchPackageWriter 决定。"
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

    private static IPatchPackageWriter CreateNativeUnavailableWriter()
    {
        return new UnavailablePatchPackageWriter(
            PatchPackageWriterKind.NativeBundles2,
            "native_writer_unavailable",
            "Native Bundles2 写入器尚未接入，当前不能生成可安装的真实 Bundles2 补丁。");
    }

    private static IPatchPackageWriter CreateLibGgpkUnavailableWriter()
    {
        return new UnavailablePatchPackageWriter(
            PatchPackageWriterKind.LibGgpk3Adapter,
            "libggpk3_writer_unavailable",
            "LibGGPK3 Adapter 尚未接入；正式闭源发布前还需要处理 AGPL 授权边界。");
    }

    private static IReadOnlyDictionary<TKey, int> CountBy<TKey>(
        IReadOnlyList<PatchChangeDto> changes,
        Func<PatchChangeDto, TKey> keySelector)
        where TKey : notnull
    {
        return changes.GroupBy(keySelector).ToDictionary(group => group.Key, group => group.Count());
    }
}
