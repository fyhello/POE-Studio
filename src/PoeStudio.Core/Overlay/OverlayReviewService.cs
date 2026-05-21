using PoeStudio.Contracts;
using PoeStudio.Core.Patching;
using PoeStudio.Core.Resources;

namespace PoeStudio.Core.Overlay;

public interface IOverlayReviewReader
{
    Task<OverlayListResponse> ListAsync(string profileId, CancellationToken cancellationToken);
}

public sealed class OverlayReviewService
{
    private readonly IOverlayReviewReader overlayReader;
    private readonly IPatchResourceLookup resourceLookup;

    public OverlayReviewService(IOverlayReviewReader overlayReader, IPatchResourceLookup resourceLookup)
    {
        this.overlayReader = overlayReader;
        this.resourceLookup = resourceLookup;
    }

    public async Task<OverlayReviewResponse> ReviewAsync(OverlayReviewRequest request, CancellationToken cancellationToken)
    {
        var list = await overlayReader.ListAsync(request.ProfileId, cancellationToken);
        var take = Math.Clamp(request.Take, 1, 500);
        var previewChars = Math.Clamp(request.PreviewChars, 0, 2000);
        var items = new List<OverlayReviewItemDto>();
        var warnings = new List<string>();

        foreach (var entry in list.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var itemWarnings = new List<string>();
            var kind = ResourceClassifier.Classify(entry.VirtualPath);
            var risk = PatchRiskClassifier.Classify(entry.VirtualPath);
            if (request.RiskLevel is not null && risk != request.RiskLevel.Value)
            {
                continue;
            }

            if (request.Kind is not null && kind != request.Kind.Value)
            {
                continue;
            }

            string? basePreview = null;
            string? overlayPreview = null;
            string? baseHash = entry.BaseHash;
            long? baseSize = entry.BaseSize;
            var resource = await resourceLookup.GetByPathAsync(request.ProfileId, entry.VirtualPath, cancellationToken);
            if (resource is null)
            {
                itemWarnings.Add("资源索引中未找到原始资源。");
            }
            else
            {
                baseSize ??= resource.Size;
                if (baseHash is null && !string.IsNullOrWhiteSpace(resource.PhysicalPath) && File.Exists(resource.PhysicalPath))
                {
                    baseHash = await HashFileAsync(resource.PhysicalPath, cancellationToken);
                }

                if (CanPreviewText(kind) && !string.IsNullOrWhiteSpace(resource.PhysicalPath) && File.Exists(resource.PhysicalPath))
                {
                    basePreview = await ReadPreviewAsync(resource.PhysicalPath, previewChars, cancellationToken);
                }
            }

            if (CanPreviewText(kind) && File.Exists(entry.OverlayPath))
            {
                overlayPreview = await ReadPreviewAsync(entry.OverlayPath, previewChars, cancellationToken);
            }
            else if (!CanPreviewText(kind))
            {
                itemWarnings.Add("非文本资源仅显示大小与 hash。");
            }

            var textReview = BuildTextReview(basePreview, overlayPreview, previewChars);
            if (itemWarnings.Count > 0)
            {
                warnings.Add($"{entry.VirtualPath}: {string.Join("；", itemWarnings)}");
            }

            items.Add(new OverlayReviewItemDto(
                entry.VirtualPath,
                kind,
                risk,
                entry.OverlaySize,
                baseSize,
                entry.OverlayHash,
                baseHash,
                !string.Equals(baseHash, entry.OverlayHash, StringComparison.OrdinalIgnoreCase),
                basePreview,
                overlayPreview,
                textReview.ChangedLines,
                textReview.BaseChangedLines,
                textReview.OverlayChangedLines,
                textReview.TextDiff,
                itemWarnings));
            if (items.Count >= take)
            {
                break;
            }
        }

        return new OverlayReviewResponse(
            request.ProfileId,
            list.Total,
            items.Count,
            items.GroupBy(item => item.RiskLevel).ToDictionary(group => group.Key, group => group.Count()),
            items.GroupBy(item => item.Kind).ToDictionary(group => group.Key, group => group.Count()),
            items,
            warnings);
    }

    private static bool CanPreviewText(ResourceKind kind)
    {
        return kind is ResourceKind.Text or ResourceKind.Ui or ResourceKind.Table;
    }

    private static async Task<string> ReadPreviewAsync(string path, int maxChars, CancellationToken cancellationToken)
    {
        if (maxChars == 0)
        {
            return string.Empty;
        }

        var text = await File.ReadAllTextAsync(path, cancellationToken);
        text = text.Replace("\0", string.Empty, StringComparison.Ordinal);
        return text.Length <= maxChars ? text : text[..maxChars];
    }

    private static TextReview BuildTextReview(string? basePreview, string? overlayPreview, int maxChars)
    {
        if (basePreview is null || overlayPreview is null)
        {
            return new TextReview(0, [], [], null);
        }

        var baseLines = SplitLines(basePreview);
        var overlayLines = SplitLines(overlayPreview);
        var count = Math.Max(baseLines.Length, overlayLines.Length);
        var baseChanged = new List<string>();
        var overlayChanged = new List<string>();
        for (var index = 0; index < count; index++)
        {
            var before = index < baseLines.Length ? baseLines[index] : string.Empty;
            var after = index < overlayLines.Length ? overlayLines[index] : string.Empty;
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                baseChanged.Add(before);
                overlayChanged.Add(after);
            }

            if (baseChanged.Count >= 20)
            {
                break;
            }
        }

        var diff = baseChanged.Count == 0
            ? null
            : string.Join(Environment.NewLine, baseChanged.Zip(overlayChanged, (before, after) => $"- {before}{Environment.NewLine}+ {after}"));
        if (diff is not null && maxChars > 0 && diff.Length > maxChars)
        {
            diff = diff[..maxChars];
        }

        return new TextReview(baseChanged.Count, baseChanged, overlayChanged, diff);
    }

    private static string[] SplitLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record TextReview(
        int ChangedLines,
        IReadOnlyList<string> BaseChangedLines,
        IReadOnlyList<string> OverlayChangedLines,
        string? TextDiff);
}
