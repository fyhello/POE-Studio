using System.Text;
using PoeStudio.Contracts;

namespace PoeStudio.Core.Tables;

public sealed class TableInspector
{
    public TableInspectResponse Inspect(ResourceSummaryDto resource, byte[] data, int limit)
    {
        var format = resource.Extension.TrimStart('.').ToLowerInvariant();
        var safeLimit = Math.Clamp(limit, 1, 1024 * 1024);
        var slice = data.AsSpan(0, Math.Min(data.Length, safeLimit)).ToArray();
        if (LooksBinary(resource, slice))
        {
            return new TableInspectResponse(
                resource.ProfileId,
                resource.VirtualPath,
                format,
                Structured: false,
                Delimiter: null,
                PreviewRowCount: 0,
                Rows: [],
                HexPreview: string.Join(" ", slice.Take(64).Select(item => item.ToString("X2"))),
                Warnings: ["该表格资源是二进制格式，当前仅提供安全十六进制预览；字段级编辑需要格式定义。"]);
        }

        var text = Encoding.UTF8.GetString(slice);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None)
            .Where(line => line.Length > 0)
            .Take(50)
            .ToArray();
        var delimiter = DetectDelimiter(lines);
        var rows = lines.Select((line, index) => new TablePreviewRowDto(
            index + 1,
            delimiter is null ? [line] : line.Split(delimiter.Value).ToArray(),
            line)).ToArray();

        return new TableInspectResponse(
            resource.ProfileId,
            resource.VirtualPath,
            format,
            Structured: delimiter is not null,
            Delimiter: delimiter is '\t' ? "\\t" : delimiter?.ToString(),
            PreviewRowCount: rows.Length,
            Rows: rows,
            HexPreview: null,
            Warnings: delimiter is null ? ["未识别到稳定分隔符，按原始文本预览。"] : []);
    }

    private static bool LooksBinary(ResourceSummaryDto resource, byte[] data)
    {
        if (resource.Extension.Equals(".dat", StringComparison.OrdinalIgnoreCase)
            || resource.Extension.Equals(".datc64", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (data.Length == 0)
        {
            return false;
        }

        var controlCount = data.Count(item => item < 9 || item is > 13 and < 32);
        return controlCount > data.Length / 20;
    }

    private static char? DetectDelimiter(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return null;
        }

        foreach (var candidate in new[] { '\t', ',', ';', '|' })
        {
            var counts = lines.Select(line => line.Count(ch => ch == candidate)).Where(count => count > 0).ToArray();
            if (counts.Length >= Math.Min(2, lines.Count) && counts.Distinct().Count() <= 2)
            {
                return candidate;
            }
        }

        return null;
    }
}
