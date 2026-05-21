using System.Text.RegularExpressions;
using PoeStudio.Contracts;
using PoeStudio.Core.Resources;

namespace PoeStudio.Core.Tables;

public sealed partial class TableSchemaInferer
{
    public TableSchemaInferResult Infer(string virtualPath, string fmtVirtualPath, string text)
    {
        var warnings = new List<string>();
        var recordSize = 0;
        var headerSize = 0;
        var fields = new List<TableSchemaFieldDto>();
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryReadNumberSetting(line, "recordSize", out var parsedRecordSize)
                || TryReadNumberSetting(line, "record_size", out parsedRecordSize))
            {
                recordSize = parsedRecordSize;
                continue;
            }

            if (TryReadNumberSetting(line, "headerSize", out var parsedHeaderSize)
                || TryReadNumberSetting(line, "header_size", out parsedHeaderSize))
            {
                headerSize = parsedHeaderSize;
                continue;
            }

            var field = ParseField(line);
            if (field is not null)
            {
                fields.Add(field);
            }
        }

        if (recordSize <= 0 && fields.Count > 0)
        {
            recordSize = fields.Max(field => field.Offset + FieldSize(field));
            warnings.Add("fmt 未提供 recordSize，已按字段末尾推断。");
        }

        if (recordSize <= 0 || fields.Count == 0)
        {
            warnings.Add("fmt 未包含可识别字段。");
            return new TableSchemaInferResult(virtualPath, fmtVirtualPath, Inferred: false, null, warnings);
        }

        return new TableSchemaInferResult(
            virtualPath,
            fmtVirtualPath,
            Inferred: true,
            new TableSchemaDto(recordSize, Math.Max(0, headerSize), fields.OrderBy(field => field.Offset).ToArray()),
            warnings);
    }

    private static string StripComment(string line)
    {
        var hash = line.IndexOf('#');
        var slashes = line.IndexOf("//", StringComparison.Ordinal);
        var cut = new[] { hash, slashes }.Where(index => index >= 0).DefaultIfEmpty(line.Length).Min();
        return line[..cut];
    }

    private static bool TryReadNumberSetting(string line, string name, out int value)
    {
        var match = SettingRegex().Match(line);
        if (match.Success && string.Equals(match.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(match.Groups["value"].Value, out value);
        }

        value = 0;
        return false;
    }

    private static TableSchemaFieldDto? ParseField(string line)
    {
        var jsonLike = JsonLikeFieldRegex().Match(line);
        if (jsonLike.Success)
        {
            return BuildField(
                jsonLike.Groups["name"].Value,
                jsonLike.Groups["offset"].Value,
                jsonLike.Groups["type"].Value,
                jsonLike.Groups["length"].Success ? jsonLike.Groups["length"].Value : null);
        }

        var tokens = TokenRegex().Matches(line)
            .Select(match => match.Value.Trim('"', '\''))
            .ToArray();
        if (tokens.Length < 3)
        {
            return null;
        }

        if (int.TryParse(tokens[1], out _))
        {
            return BuildField(tokens[0], tokens[1], tokens[2], tokens.Length >= 4 ? tokens[3] : null);
        }

        if (int.TryParse(tokens[2], out _))
        {
            return BuildField(tokens[0], tokens[2], tokens[1], tokens.Length >= 4 ? tokens[3] : null);
        }

        return null;
    }

    private static TableSchemaFieldDto? BuildField(string name, string offsetText, string type, string? lengthText)
    {
        if (!int.TryParse(offsetText, out var offset))
        {
            return null;
        }

        int? length = int.TryParse(lengthText, out var parsedLength) ? parsedLength : null;
        var normalizedType = NormalizeType(type, length);
        if (normalizedType is null)
        {
            return null;
        }

        if ((normalizedType == "ascii" || normalizedType == "utf8z" || normalizedType == "utf16z") && length is null)
        {
            return null;
        }

        return new TableSchemaFieldDto(ResourcePath.Normalize(name).Replace("/", "_", StringComparison.Ordinal), offset, normalizedType, length);
    }

    private static string? NormalizeType(string type, int? length)
    {
        var value = type.Trim().ToLowerInvariant();
        return value switch
        {
            "byte" or "uint8" or "u8" => "u8",
            "ushort" or "uint16" or "u16" => "u16",
            "short" or "int16" or "i16" => "i16",
            "uint" or "uint32" or "u32" => "u32",
            "int" or "int32" or "i32" => "i32",
            "ulong" or "uint64" or "u64" => "u64",
            "single" or "float" or "f32" => "float",
            "string" or "ascii" when length is > 0 => "ascii",
            "utf8" or "utf8z" when length is > 0 => "utf8z",
            "utf16" or "utf16le" or "utf16z" when length is > 0 => "utf16z",
            _ => null
        };
    }

    private static int FieldSize(TableSchemaFieldDto field)
    {
        return field.Type.ToLowerInvariant() switch
        {
            "u8" => 1,
            "u16" or "i16" => 2,
            "u32" or "i32" or "float" => 4,
            "u64" => 8,
            "ascii" or "utf8z" or "utf16z" => field.Length.GetValueOrDefault(),
            _ => 0
        };
    }

    [GeneratedRegex(@"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*[:=]\s*(?<value>\d+)$")]
    private static partial Regex SettingRegex();

    [GeneratedRegex(@"""name""\s*:\s*""(?<name>[^""]+)""[\s\S]*?""offset""\s*:\s*(?<offset>\d+)[\s\S]*?""type""\s*:\s*""(?<type>[^""]+)""(?:[\s\S]*?""length""\s*:\s*(?<length>\d+))?", RegexOptions.IgnoreCase)]
    private static partial Regex JsonLikeFieldRegex();

    [GeneratedRegex(@"""[^""]+""|'[^']+'|[^\s,;:]+")]
    private static partial Regex TokenRegex();
}

public sealed record TableSchemaInferResult(
    string VirtualPath,
    string FormatPath,
    bool Inferred,
    TableSchemaDto? Schema,
    IReadOnlyList<string> Warnings);
