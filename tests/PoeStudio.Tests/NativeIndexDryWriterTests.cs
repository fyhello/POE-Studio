using PoeStudio.Contracts;
using PoeStudio.Core.Patching;

namespace PoeStudio.Tests;

public sealed class NativeIndexDryWriterTests
{
    [Fact]
    public async Task WriteAsync_persists_index_rewrite_records_that_can_be_read_back()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-native-index-dry-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "native_index_dry.bin");
        var plan = new NativeIndexRewritePlanResponse(
            ProfileId: "profile-a",
            Ready: true,
            TotalItems: 1,
            Items:
            [
                new NativeIndexRewriteItemDto(
                    "metadata/stat_descriptions.txt",
                    "PoeStudio.NativePatch.bundle.bin",
                    Offset: 128,
                    Size: 4096,
                    OverlayHash: "sha256-demo")
            ],
            Blockers: [],
            Warnings: []);

        await new NativeIndexDryWriter().WriteAsync(path, plan, CancellationToken.None);

        var result = await NativeIndexDryWriter.ReadAsync(path, CancellationToken.None);
        Assert.Equal("POESTUDIO-NATIVE-DRY-INDEX", result.Magic);
        Assert.Equal(1, result.Version);
        Assert.Equal("profile-a", result.ProfileId);
        var item = Assert.Single(result.Items);
        Assert.Equal("metadata/stat_descriptions.txt", item.VirtualPath);
        Assert.Equal("PoeStudio.NativePatch.bundle.bin", item.BundleName);
        Assert.Equal(128, item.Offset);
        Assert.Equal(4096, item.Size);
        Assert.Equal("sha256-demo", item.OverlayHash);
    }
}
