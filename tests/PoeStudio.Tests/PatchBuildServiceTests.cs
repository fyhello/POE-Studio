using System.IO.Compression;
using System.Text;
using PoeStudio.Contracts;
using PoeStudio.Core.Native;
using PoeStudio.Core.Patching;
using PoeStudio.Core.Workspace;
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
    public async Task BuildAsync_native_bundles2_mode_reports_missing_index_cache_when_writer_is_available()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var profile = Profile(root) with { OodleStatus = OodleStatus.Found };
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/sample.txt", "overlay"), CancellationToken.None);
        var resource = new ResourceSummaryDto(
            Id: "resource",
            ProfileId: profile.Id,
            VirtualPath: "text/sample.txt",
            NormalizedPath: "text/sample.txt",
            Extension: ".txt",
            Kind: ResourceKind.Text,
            Size: 8,
            PhysicalPath: "native-bundles2://Base.bundle.bin#offset=16&size=8",
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var service = new PatchBuildService(root, overlay, new StaticPatchResourceLookup(resource), [new NativeBundles2PackageWriter(root, new StaticPatchResourceLookup(resource), new CopyNativeBundleCodec())]);

        var ex = await Assert.ThrowsAsync<PatchBuildException>(() => service.BuildAsync(
            new PatchBuildRequest(profile.Id, WriterKind: PatchPackageWriterKind.NativeBundles2),
            profile,
            CancellationToken.None));

        Assert.Equal("native_index_cache_missing", ex.ErrorCode);
        Assert.Contains("真实索引", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_native_bundles2_mode_uses_request_oodle_path_for_codec()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var profile = Profile(root) with { OodleStatus = OodleStatus.Found };
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/sample.txt", "patched"), CancellationToken.None);
        var hash = NativeIndexPathResolver.MurmurHash64A(Encoding.UTF8.GetBytes("text/sample.txt"));
        var layout = WorkspaceLayout.ForProfile(root, profile.Id);
        var indexCachePath = Path.Combine(layout.RawCacheRoot, "native", "bundles2", "index.decompressed.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(indexCachePath)!);
        await WriteDecompressedIndexAsync(indexCachePath, hash);
        var resource = new ResourceSummaryDto(
            Id: "resource",
            ProfileId: profile.Id,
            VirtualPath: "text/sample.txt",
            NormalizedPath: "text/sample.txt",
            Extension: ".txt",
            Kind: ResourceKind.Text,
            Size: 8,
            PhysicalPath: "native-bundles2://Base.bundle.bin#offset=16&size=8",
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var lookup = new StaticPatchResourceLookup(resource);
        var service = new PatchBuildService(root, overlay, lookup);

        var result = await service.BuildAsync(
            new PatchBuildRequest(profile.Id, BundleName: "PoeStudio.NativePatch.bundle.bin", WriterKind: PatchPackageWriterKind.NativeBundles2, OodlePath: "__copy__"),
            profile,
            CancellationToken.None);

        Assert.Equal(PatchBuildMode.NativeBundles2, result.BuildMode);
        Assert.True(File.Exists(result.IndexPath));
        Assert.True(File.Exists(result.BundlePath));
    }

    [Fact]
    public async Task BuildAsync_native_bundles2_zip_contains_only_installable_bundles_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var profile = Profile(root) with { OodleStatus = OodleStatus.Found };
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/sample.txt", "patched"), CancellationToken.None);
        var hash = NativeIndexPathResolver.MurmurHash64A(Encoding.UTF8.GetBytes("text/sample.txt"));
        var layout = WorkspaceLayout.ForProfile(root, profile.Id);
        var indexCachePath = Path.Combine(layout.RawCacheRoot, "native", "bundles2", "index.decompressed.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(indexCachePath)!);
        await WriteDecompressedIndexAsync(indexCachePath, hash);
        var resource = new ResourceSummaryDto(
            Id: "resource",
            ProfileId: profile.Id,
            VirtualPath: "text/sample.txt",
            NormalizedPath: "text/sample.txt",
            Extension: ".txt",
            Kind: ResourceKind.Text,
            Size: 8,
            PhysicalPath: "native-bundles2://Base.bundle.bin#offset=16&size=8",
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var service = new PatchBuildService(root, overlay, new StaticPatchResourceLookup(resource));

        var result = await service.BuildAsync(
            new PatchBuildRequest(profile.Id, PatchZipTemplate.WeGame, "PoeStudio.NativePatch.bundle.bin", PatchPackageWriterKind.NativeBundles2, "__copy__"),
            profile,
            CancellationToken.None);

        using var zip = ZipFile.OpenRead(result.ZipPath);
        Assert.Contains(zip.Entries, entry => entry.FullName == "Bundles2/_.index.bin");
        Assert.Contains(zip.Entries, entry => entry.FullName == "Bundles2/PoeStudio.NativePatch.bundle.bin");
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("rewritten", StringComparison.OrdinalIgnoreCase));
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
    public async Task CheckReadinessAsync_reports_missing_native_index_cache()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var profile = Profile(root) with { OodleStatus = OodleStatus.Found };
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/sample.txt", "overlay"), CancellationToken.None);
        var resource = new ResourceSummaryDto(
            Id: "resource",
            ProfileId: profile.Id,
            VirtualPath: "text/sample.txt",
            NormalizedPath: "text/sample.txt",
            Extension: ".txt",
            Kind: ResourceKind.Text,
            Size: 8,
            PhysicalPath: "native-bundles2://Base.bundle.bin#offset=16&size=8",
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var service = new PatchBuildService(root, overlay, new StaticPatchResourceLookup(resource));

        var result = await service.CheckReadinessAsync(new PatchReadinessRequest(profile.Id), profile, CancellationToken.None);

        Assert.False(result.Ready);
        Assert.Contains(result.Blockers, item => item.Contains("真实索引", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CheckReadinessAsync_reports_missing_resource_index_match()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var profile = Profile(root) with { OodleStatus = OodleStatus.Found };
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/missing.txt", "overlay"), CancellationToken.None);
        var layout = WorkspaceLayout.ForProfile(root, profile.Id);
        var indexCachePath = Path.Combine(layout.RawCacheRoot, "native", "bundles2", "index.decompressed.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(indexCachePath)!);
        await WriteDecompressedIndexAsync(indexCachePath, 123UL);
        var service = new PatchBuildService(root, overlay, new StaticPatchResourceLookup());

        var result = await service.CheckReadinessAsync(new PatchReadinessRequest(profile.Id), profile, CancellationToken.None);

        Assert.False(result.Ready);
        Assert.Contains(result.Blockers, item => item.Contains("资源索引", StringComparison.OrdinalIgnoreCase));
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
        Assert.True(File.Exists(result.NativeIndexDryPath));
        Assert.Null(result.NativeIndexRewriteDryPath);
        Assert.True(result.Size > 0);
        Assert.Single(result.Plan.Items);
        Assert.Single(result.IndexPlan.Items);
    }

    [Fact]
    public async Task BuildNativeDryBundleAsync_writes_index_rewrite_dry_run_when_cache_exists()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var profile = Profile(root);
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/sample.txt", "overlay"), CancellationToken.None);
        var hash = NativeIndexPathResolver.MurmurHash64A(Encoding.UTF8.GetBytes("text/sample.txt"));
        var layout = WorkspaceLayout.ForProfile(root, profile.Id);
        var indexCachePath = Path.Combine(layout.CacheRoot, "raw", "native", "bundles2", "index.decompressed.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(indexCachePath)!);
        await WriteDecompressedIndexAsync(indexCachePath, hash);
        var resource = new ResourceSummaryDto(
            Id: "resource",
            ProfileId: profile.Id,
            VirtualPath: "text/sample.txt",
            NormalizedPath: "text/sample.txt",
            Extension: ".txt",
            Kind: ResourceKind.Text,
            Size: 8,
            PhysicalPath: "native-bundles2://Base.bundle.bin#offset=16&size=8",
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var service = new PatchBuildService(root, overlay, new StaticPatchResourceLookup(resource));

        var result = await service.BuildNativeDryBundleAsync(new NativeDryBundleBuildRequest(profile.Id), CancellationToken.None);

        Assert.True(File.Exists(result.NativeIndexRewriteDryPath));
        var parsed = await new NativeIndexRecordParser().ParseAsync(result.NativeIndexRewriteDryPath!, CancellationToken.None);
        Assert.True(parsed.Ok);
        Assert.Equal("PoeStudio.NativePatch", parsed.Bundles[1].Path);
        Assert.Equal(1, parsed.Files[0].BundleIndex);
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
    public async Task PlanNativeIndexRewriteAsync_attaches_native_resource_location_when_indexed()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var profile = Profile(root);
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/sample.txt", "overlay"), CancellationToken.None);
        var resource = new ResourceSummaryDto(
            Id: "resource",
            ProfileId: profile.Id,
            VirtualPath: "text/sample.txt",
            NormalizedPath: "text/sample.txt",
            Extension: ".txt",
            Kind: ResourceKind.Text,
            Size: 12,
            PhysicalPath: "native-bundles2://Metadata/Text.bundle.bin#offset=32&size=12",
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var service = new PatchBuildService(root, overlay, new StaticPatchResourceLookup(resource));

        var result = await service.PlanNativeIndexRewriteAsync(new NativeIndexRewritePlanRequest(profile.Id), CancellationToken.None);

        Assert.True(result.Ready);
        var item = Assert.Single(result.Items);
        Assert.Null(item.Blocker);
        Assert.Equal("Metadata/Text.bundle.bin", item.OriginalBundleName);
        Assert.Equal(32, item.OriginalOffset);
        Assert.Equal(12, item.OriginalSize);
        Assert.False(string.IsNullOrWhiteSpace(item.PathHash));
    }

    [Fact]
    public async Task PlanNativeIndexRewriteAsync_blocks_when_indexed_resource_is_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-build-tests", Guid.NewGuid().ToString("N"));
        var profile = Profile(root);
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profile.Id, "text/missing.txt", "overlay"), CancellationToken.None);
        var service = new PatchBuildService(root, overlay, new StaticPatchResourceLookup());

        var result = await service.PlanNativeIndexRewriteAsync(new NativeIndexRewritePlanRequest(profile.Id), CancellationToken.None);

        Assert.False(result.Ready);
        Assert.Contains(result.Blockers, blocker => blocker.Contains("资源索引中不存在", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("资源索引中不存在该路径。", Assert.Single(result.Items).Blocker);
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

    private static async Task WriteDecompressedIndexAsync(string path, ulong pathHash)
    {
        await using var stream = File.Create(path);
        await using var writer = new BinaryWriter(stream);
        writer.Write(1);
        var bundleBytes = Encoding.UTF8.GetBytes("Base");
        writer.Write(bundleBytes.Length);
        writer.Write(bundleBytes);
        writer.Write(4096);
        writer.Write(1);
        writer.Write(pathHash);
        writer.Write(0);
        writer.Write(16);
        writer.Write(8);
        writer.Write(0);
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

    private sealed class StaticPatchResourceLookup(params ResourceSummaryDto[] resources) : IPatchResourceLookup
    {
        public Task<ResourceSummaryDto?> GetByPathAsync(string profileId, string virtualPath, CancellationToken cancellationToken)
        {
            return Task.FromResult(resources.FirstOrDefault(resource =>
                string.Equals(resource.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(resource.NormalizedPath, virtualPath, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
