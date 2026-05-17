using System.Text;
using System.Text.RegularExpressions;
using PoeStudio.Contracts;
using PoeStudio.Core.Native;

namespace PoeStudio.Core.Preview;

public sealed class ResourcePreviewService
{
    private const int MaxMediaPreviewBytes = 8 * 1024 * 1024;
    private const int MaxTextPreviewBytes = 64 * 1024 * 1024;

    private readonly NativeBundleResourceContentResolver? nativeContentResolver;

    public ResourcePreviewService()
    {
    }

    public ResourcePreviewService(NativeBundleResourceContentResolver nativeContentResolver)
    {
        this.nativeContentResolver = nativeContentResolver;
    }

    public async Task<ResourcePreviewResponse> BuildPreviewAsync(
        ResourceSummaryDto resource,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resource.PhysicalPath) || !File.Exists(resource.PhysicalPath))
        {
            return Unavailable(resource, "resource_file_missing", "资源文件不存在，可能尚未提取或索引已过期。");
        }

        var safeLimit = Math.Clamp(limit, 1, MaxTextPreviewBytes);
        if (IsTextLike(resource))
        {
            return await BuildTextPreviewAsync(resource, safeLimit, cancellationToken);
        }

        if (resource.Extension.Equals(".dds", StringComparison.OrdinalIgnoreCase))
        {
            return await BuildDdsPreviewAsync(resource, Math.Min(safeLimit, 4096), cancellationToken);
        }

        if (TryGetMediaType(resource, out var mediaKind, out var mediaType))
        {
            return await BuildMediaPreviewAsync(resource, mediaKind, mediaType, cancellationToken);
        }

        return await BuildHexPreviewAsync(resource, Math.Min(safeLimit, 4096), cancellationToken);
    }

    public async Task<ResourcePreviewResponse> BuildPreviewAsync(
        ResourceSummaryDto resource,
        ClientProfileDto? profile,
        int limit,
        CancellationToken cancellationToken)
    {
        return await BuildPreviewAsync(resource, profile, limit, oodlePath: null, cancellationToken);
    }

    public async Task<ResourcePreviewResponse> BuildPreviewAsync(
        ResourceSummaryDto resource,
        ClientProfileDto? profile,
        int limit,
        string? oodlePath,
        CancellationToken cancellationToken)
    {
        if (!NativeBundleResourceContentResolver.IsNativeResource(resource))
        {
            return await BuildPreviewAsync(resource, limit, cancellationToken);
        }

        if (profile is null || nativeContentResolver is null)
        {
            return Unavailable(resource, "native_preview_unavailable", "native 资源预览需要客户端配置和 Oodle。");
        }

        var content = await nativeContentResolver.ReadAsync(profile, resource, oodlePath, cancellationToken);
        if (!content.Ok)
        {
            return Unavailable(resource, content.ErrorCode ?? "native_preview_unavailable", content.Message ?? "native 资源预览不可用。");
        }

        var safeLimit = Math.Clamp(limit, 1, MaxTextPreviewBytes);
        if (IsTextLike(resource))
        {
            return BuildTextPreview(resource, content.Data, safeLimit);
        }

        if (resource.Extension.Equals(".dds", StringComparison.OrdinalIgnoreCase))
        {
            return BuildDdsPreview(resource, content.Data, Math.Min(safeLimit, 4096));
        }

        if (TryGetMediaType(resource, out var mediaKind, out var mediaType))
        {
            return BuildMediaPreview(resource, content.Data, mediaKind, mediaType);
        }

        return BuildHexPreview(resource, content.Data, Math.Min(safeLimit, 4096));
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
        var text = DecodeText(textBytes);

        return TextResponse(resource, text, truncated, InspectText(resource, text));
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

        return HexResponse(resource, hex, stream.Length > limit);
    }

    private static async Task<ResourcePreviewResponse> BuildDdsPreviewAsync(
        ResourceSummaryDto resource,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(resource.PhysicalPath!);
        var buffer = new byte[Math.Min(limit, (int)Math.Min(stream.Length, limit))];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        var data = buffer.AsSpan(0, read).ToArray();
        return BuildDdsPreview(resource, data, limit, stream.Length > limit);
    }

    private static async Task<ResourcePreviewResponse> BuildMediaPreviewAsync(
        ResourceSummaryDto resource,
        PreviewKind mediaKind,
        string mediaType,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(resource.PhysicalPath!);
        if (stream.Length > MaxMediaPreviewBytes)
        {
            return Unavailable(resource, "media_preview_too_large", "资源过大，请先导出后查看。");
        }

        var content = new byte[stream.Length];
        var offset = 0;
        while (offset < content.Length)
        {
            var read = await stream.ReadAsync(content.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return MediaResponse(resource, mediaKind, mediaType, content);
    }

    private static ResourcePreviewResponse BuildTextPreview(ResourceSummaryDto resource, byte[] data, int limit)
    {
        var truncated = data.Length > limit;
        var count = Math.Min(data.Length, limit);
        var text = DecodeText(data.AsSpan(0, count));
        return TextResponse(resource, text, truncated, InspectText(resource, text));
    }

    private static string DecodeText(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 2)
        {
            if (data[0] == 0xFF && data[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(data[2..]);
            }

            if (data[0] == 0xFE && data[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(data[2..]);
            }
        }

        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(data[3..]);
        }

        var sampleLength = Math.Min(data.Length, 512);
        if (sampleLength >= 4)
        {
            var oddNulls = 0;
            var evenNulls = 0;
            for (var index = 0; index < sampleLength; index++)
            {
                if (data[index] != 0)
                {
                    continue;
                }

                if ((index & 1) == 0)
                {
                    evenNulls++;
                }
                else
                {
                    oddNulls++;
                }
            }

            if (oddNulls > sampleLength / 4 && evenNulls < oddNulls / 3)
            {
                return Encoding.Unicode.GetString(data);
            }

            if (evenNulls > sampleLength / 4 && oddNulls < evenNulls / 3)
            {
                return Encoding.BigEndianUnicode.GetString(data);
            }
        }

        return Encoding.UTF8.GetString(data);
    }

    private static ResourcePreviewResponse BuildHexPreview(ResourceSummaryDto resource, byte[] data, int limit)
    {
        var count = Math.Min(data.Length, limit);
        var hex = string.Join(" ", data.Take(count).Select(item => item.ToString("X2")));
        return HexResponse(resource, hex, data.Length > limit);
    }

    private static ResourcePreviewResponse BuildDdsPreview(ResourceSummaryDto resource, byte[] data, int limit, bool? truncated = null)
    {
        var count = Math.Min(data.Length, limit);
        var hex = string.Join(" ", data.Take(count).Select(item => item.ToString("X2")));
        return HexResponse(resource, hex, truncated ?? data.Length > limit, InspectDds(data));
    }

    private static ResourcePreviewResponse BuildMediaPreview(
        ResourceSummaryDto resource,
        byte[] data,
        PreviewKind mediaKind,
        string mediaType)
    {
        if (data.Length > MaxMediaPreviewBytes)
        {
            return Unavailable(resource, "media_preview_too_large", "资源过大，请先导出后查看。");
        }

        return MediaResponse(resource, mediaKind, mediaType, data);
    }

    private static ResourcePreviewResponse TextResponse(
        ResourceSummaryDto resource,
        string text,
        bool truncated,
        ResourceInspectionDto? inspection = null)
    {
        return new ResourcePreviewResponse(
            resource.ProfileId,
            resource.VirtualPath,
            PreviewKind.Text,
            DetectLanguage(resource.Extension),
            text,
            Hex: null,
            MediaType: null,
            Base64Content: null,
            Truncated: truncated,
            ErrorCode: null,
            Message: null,
            Inspection: inspection,
            FromOverlay: resource.SourceLayer == ResourceSourceLayer.Overlay);
    }

    private static ResourcePreviewResponse HexResponse(
        ResourceSummaryDto resource,
        string hex,
        bool truncated,
        ResourceInspectionDto? inspection = null)
    {
        return new ResourcePreviewResponse(
            resource.ProfileId,
            resource.VirtualPath,
            PreviewKind.Hex,
            Language: null,
            Text: null,
            Hex: hex,
            MediaType: null,
            Base64Content: null,
            Truncated: truncated,
            ErrorCode: null,
            Message: null,
            Inspection: inspection,
            FromOverlay: resource.SourceLayer == ResourceSourceLayer.Overlay);
    }

    private static ResourcePreviewResponse MediaResponse(
        ResourceSummaryDto resource,
        PreviewKind mediaKind,
        string mediaType,
        byte[] content)
    {
        return new ResourcePreviewResponse(
            resource.ProfileId,
            resource.VirtualPath,
            mediaKind,
            Language: null,
            Text: null,
            Hex: null,
            MediaType: mediaType,
            Base64Content: Convert.ToBase64String(content),
            Truncated: false,
            ErrorCode: null,
            Message: null,
            Inspection: null,
            FromOverlay: resource.SourceLayer == ResourceSourceLayer.Overlay);
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
            MediaType: null,
            Base64Content: null,
            Truncated: false,
            ErrorCode: errorCode,
            Message: message,
            Inspection: null,
            FromOverlay: resource.SourceLayer == ResourceSourceLayer.Overlay);
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
            || resource.Extension.Equals(".atlas", StringComparison.OrdinalIgnoreCase)
            || resource.Extension.Equals(".hlsl", StringComparison.OrdinalIgnoreCase)
            || resource.Extension.Equals(".csd", StringComparison.OrdinalIgnoreCase);
    }

    private static string? DetectLanguage(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".json" => "json",
            ".xml" => "xml",
            ".ui" => "xml",
            ".atlas" => "text",
            ".hlsl" => "hlsl",
            ".csd" => "text",
            ".filter" => "text",
            ".txt" => "text",
            _ => "text"
        };
    }

    private static bool TryGetMediaType(ResourceSummaryDto resource, out PreviewKind kind, out string mediaType)
    {
        (kind, mediaType) = resource.Extension.ToLowerInvariant() switch
        {
            ".png" => (PreviewKind.Image, "image/png"),
            ".jpg" => (PreviewKind.Image, "image/jpeg"),
            ".jpeg" => (PreviewKind.Image, "image/jpeg"),
            ".bmp" => (PreviewKind.Image, "image/bmp"),
            ".ogg" => (PreviewKind.Audio, "audio/ogg"),
            ".wav" => (PreviewKind.Audio, "audio/wav"),
            ".ttf" => (PreviewKind.Font, "font/ttf"),
            _ => (PreviewKind.Unavailable, string.Empty)
        };
        return kind is PreviewKind.Image or PreviewKind.Audio or PreviewKind.Font;
    }

    private static ResourceInspectionDto? InspectText(ResourceSummaryDto resource, string text)
    {
        if (resource.Extension.Equals(".csd", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (resource.Extension.Equals(".atlas", StringComparison.OrdinalIgnoreCase))
        {
            return InspectAtlas(text);
        }

        if (resource.Extension.Equals(".ui", StringComparison.OrdinalIgnoreCase))
        {
            return InspectUi(text);
        }

        return null;
    }

    private static ResourceInspectionDto InspectAtlas(string text)
    {
        var lines = SplitNonEmptyLines(text);
        var warnings = new List<string>();
        var size = lines.Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith("size:", StringComparison.OrdinalIgnoreCase))?
            .Split(':', 2)[1]
            .Trim()
            .Replace(", ", "x", StringComparison.Ordinal)
            .Replace(",", "x", StringComparison.Ordinal);
        var regions = lines.Count(line => line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.Contains(':', StringComparison.Ordinal));
        if (regions > 0)
        {
            regions = Math.Max(0, regions - 1);
        }

        if (string.IsNullOrWhiteSpace(size))
        {
            warnings.Add("未找到 atlas size 字段。");
            size = "unknown";
        }

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["regions"] = regions.ToString(),
            ["size"] = size
        };
        return new ResourceInspectionDto("atlas", $"{regions} 个图块 · {size}", properties, warnings);
    }

    private static ResourceInspectionDto InspectUi(string text)
    {
        var lower = text.ToLowerInvariant();
        var elementCount = Regex.Matches(text, @"<\s*[^/!?][^>]*>").Count;
        var textRefs = Regex.Matches(text, @"\b(text|title|caption|label)\s*=", RegexOptions.IgnoreCase).Count;
        var fontRefs = Regex.Matches(text, @"\b(font|fontface|font-family)\s*=", RegexOptions.IgnoreCase).Count;
        var textureRefs = Regex.Matches(text, @"\b(texture|sprite|atlas|image)\s*=", RegexOptions.IgnoreCase).Count;
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["elements"] = elementCount.ToString(),
            ["text_fields"] = textRefs.ToString(),
            ["font_refs"] = fontRefs.ToString(),
            ["texture_refs"] = textureRefs.ToString()
        };
        var warnings = new List<string>();
        if (fontRefs == 0)
        {
            warnings.Add("未发现 font 字段，字体替换可能需要检查关联样式。");
        }

        if (textureRefs == 0 && lower.Contains(".atlas", StringComparison.Ordinal))
        {
            warnings.Add("检测到 atlas 文本但未识别贴图字段，请手工确认。");
        }

        return new ResourceInspectionDto(
            "ui",
            $"{elementCount} 个标签 · {textRefs} 文本 · {fontRefs} 字体 · {textureRefs} 贴图",
            properties,
            warnings);
    }

    private static ResourceInspectionDto InspectDds(byte[] data)
    {
        var warnings = new List<string>();
        if (data.Length < 128 || data[0] != 'D' || data[1] != 'D' || data[2] != 'S' || data[3] != ' ')
        {
            return new ResourceInspectionDto(
                "dds",
                "DDS 头无效",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ["DDS 魔数或头长度不正确。"]);
        }

        var height = ReadUInt(data, 12);
        var width = ReadUInt(data, 16);
        var mipMaps = ReadUInt(data, 28);
        var fourCc = Encoding.ASCII.GetString(data.AsSpan(84, 4)).TrimEnd('\0', ' ');
        if (string.IsNullOrWhiteSpace(fourCc))
        {
            fourCc = "RGBA";
            warnings.Add("未找到 FourCC，可能是未压缩 DDS。");
        }

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["width"] = width.ToString(),
            ["height"] = height.ToString(),
            ["mipMaps"] = mipMaps.ToString(),
            ["format"] = fourCc
        };
        var mipText = mipMaps == 1 ? "1 mip" : $"{mipMaps} mips";
        return new ResourceInspectionDto("dds", $"{width}x{height} · {fourCc} · {mipText}", properties, warnings);
    }

    private static IReadOnlyList<string> SplitNonEmptyLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    private static uint ReadUInt(byte[] data, int offset)
    {
        return (uint)(data[offset]
            | (data[offset + 1] << 8)
            | (data[offset + 2] << 16)
            | (data[offset + 3] << 24));
    }
}
