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
