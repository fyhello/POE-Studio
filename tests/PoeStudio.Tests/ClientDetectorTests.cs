using PoeStudio.Contracts;
using PoeStudio.Core.ClientDetection;

namespace PoeStudio.Tests;

public sealed class ClientDetectorTests
{
    [Fact]
    public void Detect_identifies_official_client_by_content_ggpk()
    {
        var root = CreateTempDirectory();
        File.WriteAllBytes(Path.Combine(root, "Content.ggpk"), [1, 2, 3]);

        var result = ClientDetector.Detect(root);

        Assert.True(result.Detected);
        Assert.Equal(ClientPlatform.Official, result.Platform);
        Assert.Equal(ClientEntryKind.Ggpk, result.EntryKind);
        Assert.EndsWith("Content.ggpk", result.ContentGgpkPath);
    }

    [Fact]
    public void Detect_identifies_bundles_client_by_index_file()
    {
        var root = CreateTempDirectory();
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(bundles);
        File.WriteAllBytes(Path.Combine(bundles, "_.index.bin"), [1, 2, 3]);

        var result = ClientDetector.Detect(root);

        Assert.True(result.Detected);
        Assert.Equal(ClientEntryKind.Bundles2, result.EntryKind);
        Assert.EndsWith("_.index.bin", result.IndexPath);
    }

    [Fact]
    public void Detect_returns_warning_when_root_is_not_client()
    {
        var root = CreateTempDirectory();

        var result = ClientDetector.Detect(root);

        Assert.False(result.Detected);
        Assert.Contains(result.Warnings, item => item.Contains("未找到"));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "poe-studio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
