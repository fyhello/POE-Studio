using PoeStudio.Contracts;
using PoeStudio.Core.Preview;

namespace PoeStudio.Tests;

public sealed class ResourcePreviewServiceTests
{
    [Fact]
    public async Task BuildPreviewAsync_returns_text_preview_for_json()
    {
        var file = Path.Combine(Path.GetTempPath(), "poe-studio-preview-tests", Guid.NewGuid().ToString("N"), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "{\"name\":\"Gem\"}");
        var service = new ResourcePreviewService();

        var result = await service.BuildPreviewAsync(Resource("config/config.json", ResourceKind.Text, file), 100, CancellationToken.None);

        Assert.Equal(PreviewKind.Text, result.Kind);
        Assert.Equal("json", result.Language);
        Assert.Contains("\"Gem\"", result.Text);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task BuildPreviewAsync_returns_hex_preview_for_binary()
    {
        var file = Path.Combine(Path.GetTempPath(), "poe-studio-preview-tests", Guid.NewGuid().ToString("N"), "blob.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, [0, 1, 2, 255]);
        var service = new ResourcePreviewService();

        var result = await service.BuildPreviewAsync(Resource("data/blob.bin", ResourceKind.Binary, file), 2, CancellationToken.None);

        Assert.Equal(PreviewKind.Hex, result.Kind);
        Assert.Equal("00 01", result.Hex);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task BuildPreviewAsync_reports_missing_file()
    {
        var service = new ResourcePreviewService();

        var result = await service.BuildPreviewAsync(Resource("missing.txt", ResourceKind.Text, null), 100, CancellationToken.None);

        Assert.Equal(PreviewKind.Unavailable, result.Kind);
        Assert.Equal("resource_file_missing", result.ErrorCode);
    }

    private static ResourceSummaryDto Resource(string path, ResourceKind kind, string? physicalPath)
    {
        return new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: path,
            NormalizedPath: path,
            Extension: Path.GetExtension(path),
            Kind: kind,
            Size: physicalPath is not null && File.Exists(physicalPath) ? new FileInfo(physicalPath).Length : 0,
            PhysicalPath: physicalPath,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
    }
}
