using System.IO.Compression;
using PoeStudio.Contracts;
using PoeStudio.Core.Patching;
using PoeStudio.Storage.Overlay;

namespace PoeStudio.Tests;

public sealed class PatchBuildServiceTests
{
    [Fact]
    public async Task DryRunAsync_returns_empty_summary_without_overlays()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var service = new PatchBuildService(root, new OverlayStore(root));
        var profile = Profile(root);

        var result = await service.DryRunAsync(new PatchDryRunRequest(profile.Id), profile, CancellationToken.None);

        Assert.Equal(0, result.TotalChanges);
        Assert.Empty(result.Changes);
        Assert.Empty(result.RiskCounts);
    }

    [Fact]
    public async Task DryRunAsync_summarizes_overlay_changes_by_kind_and_risk()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var overlay = new OverlayStore(root);
        var service = new PatchBuildService(root, overlay);
        var profile = Profile(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "metadata/effects/fire.mat", "changed"), CancellationToken.None);

        var result = await service.DryRunAsync(new PatchDryRunRequest(profile.Id), profile, CancellationToken.None);

        Assert.Equal(1, result.TotalChanges);
        Assert.Equal(PatchRiskLevel.High, Assert.Single(result.Changes).RiskLevel);
        Assert.Equal(1, result.RiskCounts[PatchRiskLevel.High]);
        Assert.Equal(1, result.KindCounts[ResourceKind.Material]);
    }

    [Fact]
    public async Task BuildAsync_writes_double_files_manifest_rollback_and_zip()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var clientRoot = Path.Combine(root, "client");
        var bundles = Path.Combine(clientRoot, "Bundles2");
        Directory.CreateDirectory(bundles);
        var index = Path.Combine(bundles, "_.index.bin");
        await File.WriteAllBytesAsync(index, [1, 2, 3]);
        var profile = Profile(root) with
        {
            RootPath = clientRoot,
            Bundles2Path = bundles,
            IndexPath = index
        };
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/sample.txt", "overlay"), CancellationToken.None);
        var service = new PatchBuildService(root, overlay);

        var result = await service.BuildAsync(new PatchBuildRequest(profile.Id, PatchZipTemplate.Epic), profile, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "PathOfExile2", "Bundles2", "_.index.bin")));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "PathOfExile2", "Bundles2", "Tiny.V0.1.bundle.bin")));
        Assert.True(File.Exists(result.ManifestPath));
        Assert.True(File.Exists(result.RollbackManifestPath));
        Assert.True(File.Exists(result.ZipPath));
        using var zip = ZipFile.OpenRead(result.ZipPath);
        Assert.Contains(zip.Entries, entry => entry.FullName == "PathOfExile2/Bundles2/_.index.bin");
        Assert.Contains(zip.Entries, entry => entry.FullName == "PathOfExile2/Bundles2/Tiny.V0.1.bundle.bin");
    }

    [Fact]
    public async Task BuildAsync_uses_injected_package_writer()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var profile = Profile(root);
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/sample.txt", "overlay"), CancellationToken.None);
        var writer = new CapturingPatchPackageWriter();
        var service = new PatchBuildService(root, overlay, writer);

        var result = await service.BuildAsync(new PatchBuildRequest(profile.Id), profile, CancellationToken.None);

        Assert.Equal(PatchBuildMode.OverlayBundleMvp, result.BuildMode);
        Assert.Equal(profile.Id, writer.Context!.Profile.Id);
        Assert.Single(writer.Context.OverlayEntries);
        Assert.Single(writer.Context.Changes);
        Assert.True(File.Exists(result.IndexPath));
        Assert.True(File.Exists(result.BundlePath));
    }

    [Fact]
    public async Task BuildAsync_native_bundles2_mode_fails_until_writer_is_available()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var profile = Profile(root);
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/sample.txt", "overlay"), CancellationToken.None);
        var service = new PatchBuildService(root, overlay);

        var ex = await Assert.ThrowsAsync<PatchBuildException>(() => service.BuildAsync(
            new PatchBuildRequest(profile.Id, WriterKind: PatchPackageWriterKind.NativeBundles2),
            profile,
            CancellationToken.None));

        Assert.Equal("native_writer_unavailable", ex.ErrorCode);
        Assert.Contains("Native Bundles2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckReadinessAsync_reports_native_writer_and_oodle_blockers()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var profile = Profile(root);
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/sample.txt", "overlay"), CancellationToken.None);
        var service = new PatchBuildService(root, overlay);

        var result = await service.CheckReadinessAsync(new PatchReadinessRequest(profile.Id), profile, CancellationToken.None);

        Assert.False(result.Ready);
        Assert.Equal(1, result.TotalChanges);
        Assert.Contains(result.Blockers, item => item.Contains("Native Bundles2 写入器", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Blockers, item => item.Contains("Oodle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanNativePatchAsync_marks_items_requiring_index_updates()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var profile = Profile(root);
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/sample.txt", "overlay"), CancellationToken.None);
        var service = new PatchBuildService(root, overlay);

        var result = await service.PlanNativePatchAsync(new NativePatchPlanRequest(profile.Id), CancellationToken.None);

        Assert.True(result.Ready);
        var item = Assert.Single(result.Items);
        Assert.Equal("text/sample.txt", item.VirtualPath);
        Assert.True(item.RequiresIndexUpdate);
        Assert.Equal(0, item.Offset);
        Assert.Equal("PoeStudio.NativePatch.bundle.bin", item.BundleName);
    }

    [Fact]
    public async Task BuildNativeDryBundleAsync_writes_bundle_and_plan_manifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var profile = Profile(root);
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/sample.txt", "overlay"), CancellationToken.None);
        var service = new PatchBuildService(root, overlay);

        var result = await service.BuildNativeDryBundleAsync(new NativeDryBundleBuildRequest(profile.Id), CancellationToken.None);

        Assert.True(File.Exists(result.BundlePath));
        Assert.True(File.Exists(result.ManifestPath));
        Assert.True(File.Exists(result.IndexPlanPath));
        Assert.True(result.Size > 0);
        Assert.Single(result.Plan.Items);
        Assert.Single(result.IndexPlan.Items);
    }

    [Fact]
    public async Task PlanNativeIndexRewriteAsync_projects_index_records_from_native_plan()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var profile = Profile(root);
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/sample.txt", "overlay"), CancellationToken.None);
        var service = new PatchBuildService(root, overlay);

        var result = await service.PlanNativeIndexRewriteAsync(new NativeIndexRewritePlanRequest(profile.Id), CancellationToken.None);

        Assert.True(result.Ready);
        var item = Assert.Single(result.Items);
        Assert.Equal("text/sample.txt", item.VirtualPath);
        Assert.Equal("PoeStudio.NativePatch.bundle.bin", item.BundleName);
        Assert.Equal(0, item.Offset);
        Assert.True(item.Size > 0);
    }

    [Fact]
    public async Task InstallAsync_previews_and_applies_patch_files_under_client_bundles()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var clientRoot = Path.Combine(root, "client");
        var bundles = Path.Combine(clientRoot, "Bundles2");
        Directory.CreateDirectory(bundles);
        var profile = Profile(root) with
        {
            RootPath = clientRoot,
            Bundles2Path = bundles,
            IndexPath = Path.Combine(bundles, "_.index.bin")
        };
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/sample.txt", "overlay"), CancellationToken.None);
        var service = new PatchBuildService(root, overlay);
        var build = await service.BuildAsync(new PatchBuildRequest(profile.Id), profile, CancellationToken.None);
        var buildId = new DirectoryInfo(build.OutputDirectory).Name;

        var preview = await service.InstallAsync(new PatchInstallRequest(profile.Id, buildId, Apply: false), profile, CancellationToken.None);

        Assert.False(preview.Applied);
        Assert.Equal(2, preview.FileCount);
        Assert.False(File.Exists(Path.Combine(bundles, "Tiny.V0.1.bundle.bin")));
        var applied = await service.InstallAsync(new PatchInstallRequest(profile.Id, buildId, Apply: true), profile, CancellationToken.None);

        Assert.True(applied.Applied);
        Assert.True(File.Exists(Path.Combine(bundles, "_.index.bin")));
        Assert.True(File.Exists(Path.Combine(bundles, "Tiny.V0.1.bundle.bin")));
        Assert.True(File.Exists(applied.InstallManifestPath));
    }

    [Fact]
    public async Task UninstallAsync_previews_and_removes_installed_patch_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var clientRoot = Path.Combine(root, "client");
        var bundles = Path.Combine(clientRoot, "Bundles2");
        Directory.CreateDirectory(bundles);
        var profile = Profile(root) with
        {
            RootPath = clientRoot,
            Bundles2Path = bundles,
            IndexPath = Path.Combine(bundles, "_.index.bin")
        };
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/sample.txt", "overlay"), CancellationToken.None);
        var service = new PatchBuildService(root, overlay);
        var build = await service.BuildAsync(new PatchBuildRequest(profile.Id), profile, CancellationToken.None);
        var buildId = new DirectoryInfo(build.OutputDirectory).Name;
        await service.InstallAsync(new PatchInstallRequest(profile.Id, buildId, Apply: true), profile, CancellationToken.None);

        var preview = await service.UninstallAsync(new PatchUninstallRequest(profile.Id, buildId, Apply: false), profile, CancellationToken.None);
        var removed = await service.UninstallAsync(new PatchUninstallRequest(profile.Id, buildId, Apply: true), profile, CancellationToken.None);

        Assert.False(preview.Applied);
        Assert.Equal(2, preview.Removed);
        Assert.True(removed.Applied);
        Assert.Equal(2, removed.Removed);
        Assert.False(File.Exists(Path.Combine(bundles, "Tiny.V0.1.bundle.bin")));
    }

    [Fact]
    public async Task UninstallAsync_restores_files_that_existed_before_install()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var clientRoot = Path.Combine(root, "client");
        var bundles = Path.Combine(clientRoot, "Bundles2");
        Directory.CreateDirectory(bundles);
        var existingBundle = Path.Combine(bundles, "Tiny.V0.1.bundle.bin");
        await File.WriteAllBytesAsync(existingBundle, [9, 9, 9]);
        var profile = Profile(root) with
        {
            RootPath = clientRoot,
            Bundles2Path = bundles,
            IndexPath = Path.Combine(bundles, "_.index.bin")
        };
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/sample.txt", "overlay"), CancellationToken.None);
        var service = new PatchBuildService(root, overlay);
        var build = await service.BuildAsync(new PatchBuildRequest(profile.Id), profile, CancellationToken.None);
        var buildId = new DirectoryInfo(build.OutputDirectory).Name;

        var installed = await service.InstallAsync(new PatchInstallRequest(profile.Id, buildId, Apply: true), profile, CancellationToken.None);
        await service.UninstallAsync(new PatchUninstallRequest(profile.Id, buildId, Apply: true), profile, CancellationToken.None);

        Assert.True(installed.Files.Single(file => file.RelativePath == "Tiny.V0.1.bundle.bin").TargetExists);
        Assert.Equal([9, 9, 9], await File.ReadAllBytesAsync(existingBundle));
    }

    private static ClientProfileDto Profile(string root)
    {
        var id = Guid.NewGuid().ToString("N");
        return new ClientProfileDto(
            Id: id,
            DisplayName: "test",
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Bundles2,
            RootPath: root,
            ContentGgpkPath: null,
            Bundles2Path: Path.Combine(root, "Bundles2"),
            IndexPath: Path.Combine(root, "Bundles2", "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);
    }

    private sealed class CapturingPatchPackageWriter : IPatchPackageWriter
    {
        public PatchPackageWriterKind Kind => PatchPackageWriterKind.Mvp;

        public PatchPackageWriterContext? Context { get; private set; }

        public async Task<PatchPackageWriteResult> WriteAsync(PatchPackageWriterContext context, CancellationToken cancellationToken)
        {
            Context = context;
            Directory.CreateDirectory(context.BundlesDirectory);
            var indexPath = Path.Combine(context.BundlesDirectory, "_.index.bin");
            var bundlePath = Path.Combine(context.BundlesDirectory, context.Request.BundleName);
            await File.WriteAllBytesAsync(indexPath, [1], cancellationToken);
            await File.WriteAllBytesAsync(bundlePath, [2], cancellationToken);
            return new PatchPackageWriteResult(indexPath, bundlePath, PatchBuildMode.OverlayBundleMvp, ["captured"]);
        }
    }
}
