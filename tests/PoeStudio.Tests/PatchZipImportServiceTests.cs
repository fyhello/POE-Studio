using System.IO.Compression;
using PoeStudio.Contracts;
using PoeStudio.Core.Patching;

namespace PoeStudio.Tests;

public sealed class PatchZipImportServiceTests
{
    [Fact]
    public async Task ImportAsync_extracts_external_zip_into_profile_build_history_shape()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-patch-import-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var zipPath = Path.Combine(root, "external.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(zip, "Bundles2/_.index.bin", [1, 2, 3]);
            await WriteEntryAsync(zip, "Bundles2/Tiny.V0.1.bundle.bin", [4, 5, 6]);
        }
        var service = new PatchZipImportService(root, new PatchImportAnalyzer());

        var result = await service.ImportAsync(
            new PatchZipImportRequest("profile-a", zipPath, "Tiny.V0.1.bundle.bin"),
            CancellationToken.None);

        Assert.Equal("profile-a", result.ProfileId);
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "Bundles2", "_.index.bin")));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "Bundles2", "Tiny.V0.1.bundle.bin")));
        Assert.True(File.Exists(result.ZipPath));
        Assert.True(File.Exists(result.ImportManifestPath));
        Assert.True(result.Analysis.HasPatchBundle);
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes);
    }
}
