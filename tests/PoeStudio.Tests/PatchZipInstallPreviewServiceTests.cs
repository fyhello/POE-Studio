using System.IO.Compression;
using PoeStudio.Contracts;
using PoeStudio.Core.Patching;

namespace PoeStudio.Tests;

public sealed class PatchZipInstallPreviewServiceTests
{
    [Fact]
    public async Task PreviewAsync_reports_new_replaced_and_same_bundles_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-patch-impact-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "client", "Bundles2");
        Directory.CreateDirectory(bundles);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "_.index.bin"), [1, 1, 1]);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "Same.bundle.bin"), [9, 9, 9]);
        var zipPath = Path.Combine(root, "external.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(zip, "Bundles2/_.index.bin", [2, 2, 2]);
            await WriteEntryAsync(zip, "Bundles2/Tiny.V0.1.bundle.bin", [4, 5, 6]);
            await WriteEntryAsync(zip, "Bundles2/Same.bundle.bin", [9, 9, 9]);
        }
        var profile = Profile(root, bundles);
        var service = new PatchZipInstallPreviewService(new PatchImportAnalyzer());

        var result = await service.PreviewAsync(
            new PatchZipInstallPreviewRequest(profile.Id, zipPath, "Tiny.V0.1.bundle.bin"),
            profile,
            CancellationToken.None);

        Assert.Equal(3, result.FileCount);
        Assert.Equal(1, result.NewFiles);
        Assert.Equal(1, result.ReplacedFiles);
        Assert.Equal(1, result.SameFiles);
        Assert.Equal(0, result.HighRiskFiles);
        Assert.Contains(result.Files, file => file.RelativePath == "_.index.bin" && file.TargetExists && file.SameHash == false);
        Assert.Contains(result.Files, file => file.RelativePath == "Tiny.V0.1.bundle.bin" && !file.TargetExists);
        Assert.Contains(result.Files, file => file.RelativePath == "Same.bundle.bin" && file.SameHash == true);
    }

    private static ClientProfileDto Profile(string root, string bundles)
    {
        return new ClientProfileDto(
            Guid.NewGuid().ToString("N"),
            "test",
            ClientPlatform.WeGame,
            ClientEntryKind.Bundles2,
            Path.Combine(root, "client"),
            null,
            bundles,
            Path.Combine(bundles, "_.index.bin"),
            OodleStatus.Missing,
            "fingerprint",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes);
    }
}
