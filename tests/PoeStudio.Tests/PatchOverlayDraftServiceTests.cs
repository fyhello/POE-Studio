using System.Text;
using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Native;
using PoeStudio.Core.Patching;
using PoeStudio.Core.Workspace;
using PoeStudio.Storage.Overlay;

namespace PoeStudio.Tests;

public sealed class PatchOverlayDraftServiceTests
{
    [Fact]
    public async Task ImportDraftAsync_extracts_patch_bundle_records_into_overlay_entries()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-overlay-draft-tests", Guid.NewGuid().ToString("N"));
        var profileId = "profile-a";
        var layout = WorkspaceLayout.ForProfile(root, profileId);
        var buildId = "20260512131313";
        var bundles = Path.Combine(layout.BuildsRoot, buildId, "Bundles2");
        Directory.CreateDirectory(bundles);
        var virtualPath = "text/sample.txt";
        var pathHash = NativeIndexPathResolver.MurmurHash64A(Encoding.UTF8.GetBytes(virtualPath));
        await File.WriteAllBytesAsync(Path.Combine(bundles, "_.index.bin"), new NativeBundleCompressor(new CopyNativeBundleCodec()).Compress(await BuildIndexPayloadAsync(root, pathHash)));
        await File.WriteAllBytesAsync(Path.Combine(bundles, "Tiny.V0.1.bundle.bin"), new NativeBundleCompressor(new CopyNativeBundleCodec()).Compress(Encoding.UTF8.GetBytes("patched text")));
        var overlay = new OverlayStore(root);
        var service = new PatchOverlayDraftService(root, overlay, new StaticPathHashLookup(pathHash, virtualPath));

        var result = await service.ImportDraftAsync(
            new PatchOverlayDraftRequest(profileId, buildId, "Tiny.V0.1.bundle.bin", "__copy__"),
            CancellationToken.None);
        var list = await overlay.ListAsync(profileId, CancellationToken.None);

        Assert.Equal(1, result.MatchedRecords);
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.KindCounts[ResourceKind.Text]);
        Assert.Equal(1, result.RiskCounts[PatchRiskLevel.Low]);
        Assert.EndsWith("overlay_draft_report.json", result.DraftReportPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(result.DraftReportPath));
        await using (var reportStream = File.OpenRead(result.DraftReportPath))
        {
            var report = await JsonSerializer.DeserializeAsync<PatchOverlayDraftReportDto>(
                reportStream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(report);
            Assert.Equal(profileId, report.ProfileId);
            Assert.Equal(buildId, report.BuildId);
            Assert.Equal(1, report.MatchedRecords);
            Assert.Equal(1, report.Imported);
            Assert.Equal(1, report.KindCounts[ResourceKind.Text]);
            Assert.Equal(1, report.RiskCounts[PatchRiskLevel.Low]);
            Assert.Equal(virtualPath, Assert.Single(report.Items).VirtualPath);
        }

        var item = Assert.Single(list.Items);
        Assert.Equal(virtualPath, item.VirtualPath);
        Assert.Equal("patched text", await File.ReadAllTextAsync(item.OverlayPath));
    }

    private sealed class StaticPathHashLookup(ulong pathHash, string virtualPath) : IPatchPathHashLookup
    {
        public Task<string?> FindPathByHashAsync(string profileId, ulong hash, CancellationToken cancellationToken)
        {
            return Task.FromResult(hash == pathHash ? virtualPath : null);
        }
    }

    private static async Task<byte[]> BuildIndexPayloadAsync(string root, ulong hash)
    {
        var path = Path.Combine(root, "index.payload.bin");
        await using (var stream = File.Create(path))
        await using (var writer = new BinaryWriter(stream))
        {
            writer.Write(2);
            WriteBundle(writer, "Base", 4096);
            WriteBundle(writer, "Tiny.V0.1", 12);
            writer.Write(1);
            writer.Write(hash);
            writer.Write(1);
            writer.Write(0);
            writer.Write(12);
            writer.Write(0);
        }

        return await File.ReadAllBytesAsync(path);
    }

    private static void WriteBundle(BinaryWriter writer, string path, int uncompressedSize)
    {
        var bytes = Encoding.UTF8.GetBytes(path);
        writer.Write(bytes.Length);
        writer.Write(bytes);
        writer.Write(uncompressedSize);
    }
}
