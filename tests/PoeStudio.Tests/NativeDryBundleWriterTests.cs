using PoeStudio.Contracts;
using PoeStudio.Core.Patching;

namespace PoeStudio.Tests;

public sealed class NativeDryBundleWriterTests
{
    [Fact]
    public async Task WriteAsync_writes_plan_manifest_and_concatenated_payload()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-native-writer-tests", Guid.NewGuid().ToString("N"));
        var overlayPath = Path.Combine(root, "overlay.txt");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(overlayPath, "hello");
        var entry = new OverlayEntryDto(
            ProfileId: "profile",
            VirtualPath: "text/sample.txt",
            NormalizedPath: "text/sample.txt",
            OverlayPath: overlayPath,
            OverlaySize: 5,
            OverlayHash: "hash",
            BaseHash: null,
            BaseSize: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);
        var plan = new NativePatchPlanResponse(
            "profile",
            "PoeStudio.NativePatch.bundle.bin",
            Ready: true,
            TotalItems: 1,
            [
                new NativePatchPlanItemDto(
                    "text/sample.txt",
                    "PoeStudio.NativePatch.bundle.bin",
                    Offset: 0,
                    Size: 5,
                    OverlayHash: "hash",
                    RequiresIndexUpdate: true,
                    Blocker: null)
            ],
            Blockers: [],
            Warnings: []);

        var result = await new NativeDryBundleWriter().WriteAsync(root, plan, [entry], CancellationToken.None);

        Assert.True(File.Exists(result.BundlePath));
        Assert.True(File.Exists(result.ManifestPath));
        var bytes = await File.ReadAllBytesAsync(result.BundlePath);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        var magic = "POESTUDIO-NATIVE-DRY-BUNDLE\0";
        Assert.Equal(magic, System.Text.Encoding.ASCII.GetString(bytes, 0, magic.Length));
        Assert.Contains("text/sample.txt", text);
        Assert.EndsWith("hello", text);
    }
}
