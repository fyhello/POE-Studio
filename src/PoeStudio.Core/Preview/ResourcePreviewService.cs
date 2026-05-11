using System.Text;
using PoeStudio.Contracts;

namespace PoeStudio.Core.Preview;

public sealed class ResourcePreviewService
{
    public async Task<ResourcePreviewResponse> BuildPreviewAsync(
        ResourceSummaryDto resource,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resource.PhysicalPath) || !File.Exists(resource.PhysicalPath))
        {
            return Unavailable(resource, "resource_file_missing", "资源文件不存在，可能尚未提取或索引已过期。");
        }

        var safeLimit = Math.Clamp(limit, 1, 1024 * 1024);
        if (IsTextLike(resource))
        {
            return await BuildTextPreviewAsync(resource, safeLimit, cancellationToken);
        }

        return await BuildHexPreviewAsync(resource, Math.Min(safeLimit, 4096), cancellationToken);
    }

    private static async Task<ResourcePreviewResponse> BuildTextPreviewAsync(
        ResourceSummaryDto resource,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(resource.PhysicalPath!);
        var buffer = new byte[Math.Min(limit + 1, (int)Math.Min(stream.Length, limit + 1L))];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        var truncated = stream.Length > limit;
        var textBytes = truncated ? buffer.AsSpan(0, Math.Min(limit, read)).ToArray() : buffer.AsSpan(0, read).ToArray();
        var text = Encoding.UTF8.GetString(textBytes);

        return new ResourcePreviewResponse(
            resource.ProfileId,
            resource.VirtualPath,
            PreviewKind.Text,
            DetectLanguage(resource.Extension),
            text,
            Hex: null,
            Truncated: truncated,
            ErrorCode: null,
            Message: null);
    }

    private static async Task<ResourcePreviewResponse> BuildHexPreviewAsync(
        ResourceSummaryDto resource,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(resource.PhysicalPath!);
        var buffer = new byte[Math.Min(limit, (int)Math.Min(stream.Length, limit))];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        var hex = string.Join(" ", buffer.Take(read).Select(item => item.ToString("X2")));

        return new ResourcePreviewResponse(
            resource.ProfileId,
            resource.VirtualPath,
            PreviewKind.Hex,
            Language: null,
            Text: null,
            Hex: hex,
            Truncated: stream.Length > limit,
            ErrorCode: null,
            Message: null);
    }

    private static ResourcePreviewResponse Unavailable(ResourceSummaryDto resource, string errorCode, string message)
    {
        return new ResourcePreviewResponse(
            resource.ProfileId,
            resource.VirtualPath,
            PreviewKind.Unavailable,
            Language: null,
            Text: null,
            Hex: null,
            Truncated: false,
            ErrorCode: errorCode,
            Message: message);
    }

    private static bool IsTextLike(ResourceSummaryDto resource)
    {
        if (resource.Kind is ResourceKind.Text or ResourceKind.Ui)
        {
            return true;
        }

        return resource.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || resource.Extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
            || resource.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || resource.Extension.Equals(".filter", StringComparison.OrdinalIgnoreCase)
            || resource.Extension.Equals(".ui", StringComparison.OrdinalIgnoreCase)
            || resource.Extension.Equals(".hlsl", StringComparison.OrdinalIgnoreCase);
    }

    private static string? DetectLanguage(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".json" => "json",
            ".xml" => "xml",
            ".ui" => "xml",
            ".hlsl" => "hlsl",
            ".filter" => "text",
            ".txt" => "text",
            _ => "text"
        };
    }
}
