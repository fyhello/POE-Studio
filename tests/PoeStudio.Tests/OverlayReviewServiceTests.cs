using PoeStudio.Contracts;
using PoeStudio.Core.Overlay;
using PoeStudio.Core.Patching;
using PoeStudio.Storage.Overlay;

namespace PoeStudio.Tests;

public sealed class OverlayReviewServiceTests
{
    [Fact]
    public async Task ReviewAsync_returns_text_previews_and_risk_summary()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-overlay-review-tests", Guid.NewGuid().ToString("N"));
        var profileId = "profile-a";
        var basePath = Path.Combine(root, "base", "text", "sample.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
        await File.WriteAllTextAsync(basePath, "Hello exile");
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profileId, "text/sample.txt", "Hello 流放者", basePath, true), CancellationToken.None);
        var service = new OverlayReviewService(overlay, new StaticLookup(new ResourceSummaryDto(
            "resource",
            profileId,
            "text/sample.txt",
            "text/sample.txt",
            ".txt",
            ResourceKind.Text,
            11,
            basePath,
            ResourceSourceLayer.Base,
            DateTimeOffset.UtcNow)));

        var result = await service.ReviewAsync(new OverlayReviewRequest(profileId), CancellationToken.None);

        Assert.Equal(1, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("Hello exile", item.BasePreview);
        Assert.Equal("Hello 流放者", item.OverlayPreview);
        Assert.True(item.TextChanged);
        Assert.Equal(1, item.ChangedLines);
        Assert.Equal("Hello exile", Assert.Single(item.BaseChangedLines));
        Assert.Equal("Hello 流放者", Assert.Single(item.OverlayChangedLines));
        Assert.Contains("- Hello exile", item.TextDiff);
        Assert.Contains("+ Hello 流放者", item.TextDiff);
        Assert.Equal(PatchRiskLevel.Low, item.RiskLevel);
    }

    [Fact]
    public async Task ReviewAsync_can_filter_by_risk_level()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-overlay-review-tests", Guid.NewGuid().ToString("N"));
        var profileId = "profile-a";
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profileId, "text/sample.txt", "safe"), CancellationToken.None);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profileId, "metadata/effects/fire.mat", "risky"), CancellationToken.None);
        var service = new OverlayReviewService(overlay, new StaticLookup());

        var result = await service.ReviewAsync(new OverlayReviewRequest(profileId, RiskLevel: PatchRiskLevel.High), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("metadata/effects/fire.mat", item.VirtualPath);
        Assert.Equal(PatchRiskLevel.High, item.RiskLevel);
    }

    [Fact]
    public async Task ReviewAsync_can_filter_by_resource_kind_and_returns_kind_counts()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-overlay-review-tests", Guid.NewGuid().ToString("N"));
        var profileId = "profile-a";
        var overlay = new OverlayStore(root);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profileId, "text/sample.txt", "safe"), CancellationToken.None);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(profileId, "ui/panel.ui", "ui"), CancellationToken.None);
        var service = new OverlayReviewService(overlay, new StaticLookup());

        var result = await service.ReviewAsync(new OverlayReviewRequest(profileId, Kind: ResourceKind.Ui), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("ui/panel.ui", item.VirtualPath);
        Assert.Equal(1, result.KindCounts[ResourceKind.Ui]);
    }

    private sealed class StaticLookup(params ResourceSummaryDto[] resources) : IPatchResourceLookup
    {
        public Task<ResourceSummaryDto?> GetByPathAsync(string profileId, string virtualPath, CancellationToken cancellationToken)
        {
            return Task.FromResult(resources.FirstOrDefault(resource =>
                string.Equals(resource.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(resource.NormalizedPath, virtualPath, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
