using System.Text;
using System.Text.Json;
using System.Reflection;
using PoeStudio.Contracts;

namespace PoeStudio.Core.Tables;

public sealed class TableInspector
{
    private const int DefaultMaxRows = 1024 * 1024;

    public TableInspectResponse Inspect(ResourceSummaryDto resource, byte[] data, int limit)
    {
        return Inspect(resource, data, limit, schema: null);
    }

    public TableInspectResponse Inspect(ResourceSummaryDto resource, byte[] data, int limit, TableSchemaDto? schema, int maxRows = DefaultMaxRows)
    {
        var format = resource.Extension.TrimStart('.').ToLowerInvariant();
        var safeLimit = Math.Clamp(limit, 1, 1024 * 1024);
        var safeMaxRows = Math.Clamp(maxRows, 1, 1024 * 1024);
        var isBinary = LooksBinary(resource, data);
        var slice = data.AsSpan(0, Math.Min(data.Length, safeLimit)).ToArray();
        if (schema is not null)
        {
            return InspectWithSchema(resource, slice, format, schema, safeMaxRows);
        }

        if (TryDecodeTextTable(slice, out var text, out var textEncoding) && (!isBinary || LooksLikeDelimitedText(text)))
        {
            return InspectText(resource, format, text, textEncoding, safeMaxRows);
        }

        if (isBinary)
        {
            var legacyDatPreview = TryInspectLegacyDatWithCatalog(resource, data, format, safeMaxRows);
            if (legacyDatPreview is not null)
            {
                return legacyDatPreview;
            }

            var schemaPreview = TryInspectDatc64WithCatalog(resource, data, format, safeMaxRows);
            if (schemaPreview is not null)
            {
                return schemaPreview;
            }

            var inferredDatc64Preview = TryInspectDatc64ByStringPointers(resource, data, format, safeMaxRows);
            if (inferredDatc64Preview is not null)
            {
                return inferredDatc64Preview;
            }

            var binary = InspectBinary(slice);
            var candidateRows = binary.Strings.Count > 0
                ? BuildStringCandidateRows(binary.Strings, safeMaxRows)
                : BuildNumericWordRows(slice, safeMaxRows);
            var columns = binary.Strings.Count > 0
                ? new[] { "#", "offset", "bytes", "encoding", "text" }
                : new[] { "word", "offset", "u32", "i32", "hex" };
            var warnings = new List<string>
            {
                "该表格资源是二进制格式，当前提供安全只读检查；字段级编辑需要格式定义。"
            };
            if (binary.Strings.Count == 0)
            {
                warnings.Add("未识别到表结构，也没有发现可信的可读文本候选；请导入/选择 .fmt 结构或后续结构库后再按行列编辑。");
            }

            return new TableInspectResponse(
                resource.ProfileId,
                resource.VirtualPath,
                format,
                Structured: false,
                Delimiter: null,
                PreviewRowCount: candidateRows.Count,
                Rows: candidateRows,
                HexPreview: string.Join(" ", slice.Take(64).Select(item => item.ToString("X2"))),
                Warnings: warnings,
                HeaderFields: binary.HeaderFields,
                Strings: binary.Strings,
                LayoutHints: binary.LayoutHints,
                Columns: columns);
        }

        return InspectText(resource, format, text, textEncoding, safeMaxRows);
    }

    private static TableInspectResponse InspectText(
        ResourceSummaryDto resource,
        string format,
        string text,
        string encodingName,
        int safeMaxRows)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None)
            .Where(line => line.Length > 0)
            .Take(safeMaxRows)
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
            Warnings: delimiter is null ? ["未识别到稳定分隔符，按原始文本预览。"] : [],
            LayoutHints: [$"文本编码：{encodingName}"],
            TextEncoding: encodingName);
    }

    private static TableInspectResponse InspectWithSchema(
        ResourceSummaryDto resource,
        byte[] data,
        string format,
        TableSchemaDto schema,
        int maxRows)
    {
        var warnings = ValidateSchema(schema).ToList();
        if (warnings.Count > 0)
        {
            var binary = InspectBinary(data);
            return new TableInspectResponse(
                resource.ProfileId,
                resource.VirtualPath,
                format,
                Structured: false,
                Delimiter: null,
                PreviewRowCount: 0,
                Rows: [],
                HexPreview: string.Join(" ", data.Take(64).Select(item => item.ToString("X2"))),
                Warnings: warnings,
                HeaderFields: binary.HeaderFields,
                Strings: binary.Strings,
                LayoutHints: binary.LayoutHints);
        }

        var available = Math.Max(0, data.Length - schema.HeaderSize);
        var rowCount = available / schema.RecordSize;
        var rows = new List<TablePreviewRowDto>();
        for (var rowIndex = 0; rowIndex < Math.Min(rowCount, maxRows); rowIndex++)
        {
            var rowOffset = schema.HeaderSize + rowIndex * schema.RecordSize;
            var cells = schema.Fields
                .Select(field => ReadField(data, rowOffset, field))
                .ToArray();
            rows.Add(new TablePreviewRowDto(rowIndex + 1, cells, string.Join('\t', cells)));
        }

        var header = InspectBinary(data).HeaderFields;
        var hints = new List<string>
        {
            $"schema recordSize={schema.RecordSize}, headerSize={schema.HeaderSize}",
            $"schema rows={rowCount}"
        };
        return new TableInspectResponse(
            resource.ProfileId,
            resource.VirtualPath,
            format,
            Structured: true,
            Delimiter: "schema",
            PreviewRowCount: rows.Count,
            Rows: rows,
            HexPreview: null,
            Warnings: available % schema.RecordSize == 0 ? [] : ["数据长度不能被 recordSize 整除，末尾字节已忽略。"],
            HeaderFields: header,
            Strings: null,
            LayoutHints: hints,
            Columns: schema.Fields.Select(field => field.Name).ToArray(),
            EditableColumnIndexes: schema.Fields
                .Select((field, index) => new { field, index })
                .Where(item => item.field.Type.Equals("ascii", StringComparison.OrdinalIgnoreCase)
                    || item.field.Type.Equals("utf8z", StringComparison.OrdinalIgnoreCase)
                    || item.field.Type.Equals("utf16z", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.index)
                .ToArray());
    }

    public string ApplyCellEdits(ResourceSummaryDto resource, byte[] data, IReadOnlyList<TableCellEditDto> edits)
    {
        return ApplyTextCellEdits(resource, data, edits).Text;
    }

    public byte[] ApplyCellEditsToBytes(ResourceSummaryDto resource, byte[] data, IReadOnlyList<TableCellEditDto> edits)
    {
        var edited = ApplyTextCellEdits(resource, data, edits);
        return EncodeTextTable(edited.Text, edited.EncodingName);
    }

    private static TextEditResult ApplyTextCellEdits(ResourceSummaryDto resource, byte[] data, IReadOnlyList<TableCellEditDto> edits)
    {
        var slice = data.AsSpan(0, data.Length).ToArray();
        if (!TryDecodeTextTable(slice, out var text, out var encodingName) || (LooksBinary(resource, slice) && !LooksLikeDelimitedText(text)))
        {
            throw new InvalidOperationException("无法识别文本表格编码，不能安全写回。");
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n', StringSplitOptions.None);
        var nonEmpty = lines.Where(line => line.Length > 0).ToArray();
        var delimiter = DetectDelimiter(nonEmpty);
        if (delimiter is null)
        {
            throw new InvalidOperationException("未识别到稳定分隔符，不能安全写回表格。");
        }

        foreach (var edit in edits)
        {
            if (edit.RowNumber < 1 || edit.RowNumber > lines.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(edits), $"行号超出范围：{edit.RowNumber}");
            }

            var cells = lines[edit.RowNumber - 1].Split(delimiter.Value).ToArray();
            if (edit.ColumnIndex < 0 || edit.ColumnIndex >= cells.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(edits), $"列号超出范围：{edit.ColumnIndex}");
            }

            cells[edit.ColumnIndex] = edit.Value;
            lines[edit.RowNumber - 1] = string.Join(delimiter.Value, cells);
        }

        return new TextEditResult(string.Join(newline, lines), encodingName);
    }

    public byte[] ApplyCellEdits(
        ResourceSummaryDto resource,
        byte[] data,
        IReadOnlyList<TableCellEditDto> edits,
        TableSchemaDto schema)
    {
        if (!LooksBinary(resource, data))
        {
            return Encoding.UTF8.GetBytes(ApplyCellEdits(resource, data, edits));
        }

        var warnings = ValidateSchema(schema).ToArray();
        if (warnings.Length > 0)
        {
            throw new InvalidOperationException(string.Join(" ", warnings));
        }

        var output = data.AsSpan(0, data.Length).ToArray();
        var available = Math.Max(0, output.Length - schema.HeaderSize);
        var rowCount = available / schema.RecordSize;
        foreach (var edit in edits)
        {
            if (edit.RowNumber < 1 || edit.RowNumber > rowCount)
            {
                throw new ArgumentOutOfRangeException(nameof(edits), $"行号超出范围：{edit.RowNumber}");
            }

            if (edit.ColumnIndex < 0 || edit.ColumnIndex >= schema.Fields.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(edits), $"列号超出范围：{edit.ColumnIndex}");
            }

            var field = schema.Fields[edit.ColumnIndex];
            var offset = schema.HeaderSize + (edit.RowNumber - 1) * schema.RecordSize + field.Offset;
            WriteField(output, offset, field, edit.Value);
        }

        return output;
    }

    private static bool LooksBinary(ResourceSummaryDto resource, byte[] data)
    {
        if (resource.Extension.Equals(".datc64", StringComparison.OrdinalIgnoreCase)
            || resource.Extension.Equals(".dat", StringComparison.OrdinalIgnoreCase))
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

    private static bool TryDecodeTextTable(byte[] data, out string text, out string encodingName)
    {
        if (data.Length >= 2 && data[0] == 0xff && data[1] == 0xfe)
        {
            text = Encoding.Unicode.GetString(data, 2, data.Length - 2);
            encodingName = "UTF-16LE";
            return !text.Contains('\uFFFD', StringComparison.Ordinal);
        }

        if (data.Length >= 3 && data[0] == 0xef && data[1] == 0xbb && data[2] == 0xbf)
        {
            return TryDecodeStrict(data.AsSpan(3).ToArray(), Encoding.UTF8, "UTF-8 BOM", out text, out encodingName);
        }

        if (LooksLikeUtf16LeText(data))
        {
            text = Encoding.Unicode.GetString(data);
            encodingName = "UTF-16LE";
            return !text.Contains('\uFFFD', StringComparison.Ordinal);
        }

        return TryDecodeStrict(data, Encoding.UTF8, "UTF-8", out text, out encodingName);
    }

    private static bool TryDecodeStrict(byte[] data, Encoding encoding, string name, out string text, out string encodingName)
    {
        text = string.Empty;
        encodingName = name;
        try
        {
            var decoder = Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            text = decoder.GetString(data);
            return !text.Contains('\uFFFD', StringComparison.Ordinal);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static byte[] EncodeTextTable(string text, string encodingName)
    {
        return encodingName.ToUpperInvariant() switch
        {
            "UTF-16LE" => Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray(),
            "UTF-8 BOM" => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray(),
            _ => Encoding.UTF8.GetBytes(text)
        };
    }

    private static bool LooksLikeUtf16LeText(byte[] data)
    {
        if (data.Length < 8 || data.Length % 2 != 0) return false;
        var pairs = Math.Min(data.Length / 2, 256);
        var zeroHigh = 0;
        var useful = 0;
        for (var index = 0; index < pairs; index++)
        {
            var low = data[index * 2];
            var high = data[index * 2 + 1];
            if (high == 0 && low is >= 9 and <= 126)
            {
                zeroHigh++;
                useful++;
            }
            else
            {
                var ch = (char)(low | (high << 8));
                if (IsUsefulTextChar(ch)) useful++;
            }
        }

        return useful >= pairs * 0.75 && zeroHigh >= Math.Min(4, pairs / 4);
    }

    private static bool LooksLikeDelimitedText(string text)
    {
        if (text.IndexOf('\0') >= 0) return false;
        var sample = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Take(8)
            .ToArray();
        return DetectDelimiter(sample) is not null;
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

    private static BinaryInspection InspectBinary(byte[] data)
    {
        var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var headerFieldCount = Math.Min(8, data.Length / 4);
        for (var index = 0; index < headerFieldCount; index++)
        {
            var offset = index * 4;
            header[$"u32_{index}"] = ReadUInt32(data, offset).ToString();
        }

        var strings = ExtractStringCandidates(data, maxCount: 80);
        var hints = BuildLayoutHints(data, strings);
        return new BinaryInspection(header, strings, hints);
    }

    private static TableInspectResponse? TryInspectDatc64WithCatalog(
        ResourceSummaryDto resource,
        byte[] data,
        string format,
        int maxRows)
    {
        if (!resource.Extension.Equals(".datc64", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tableName = Path.GetFileNameWithoutExtension(resource.NormalizedPath);
        var table = DatSchemaCatalog.TryGet(tableName);
        var minimumFixedRowLength = table is null ? 0 : CalculateCatalogRowLength(table);
        if (!TryReadDatc64Sections(data, minimumFixedRowLength, out var dat))
        {
            return null;
        }

        if (table is null)
        {
            return null;
        }

        var columns = BuildCatalogColumns(table, dat);
        var safeColumns = dat.RowCount == 0
            ? columns
            : columns.Where(column => column.Offset + column.ByteLength <= dat.RowLength).ToArray();
        if (safeColumns.Length == 0)
        {
            return null;
        }

        var warnings = new List<string>
        {
            "该表格已按 schema.min.json 解析为真实 datc64 行列；string 字段支持写入草稿层，其他字段保持只读。"
        };
        if (safeColumns.Length != columns.Length)
        {
            warnings.Add("部分 schema 列超出当前 rowLength，已隐藏越界列。");
        }

        var rowLimit = Math.Min(dat.RowCount, maxRows);
        var rows = new List<TablePreviewRowDto>(rowLimit);
        for (var rowIndex = 0; rowIndex < rowLimit; rowIndex++)
        {
            var cells = safeColumns
                .Select(column => ReadCatalogCell(dat, rowIndex, column))
                .ToArray();
            rows.Add(new TablePreviewRowDto(rowIndex + 1, cells, string.Join('\t', cells)));
        }

        var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["rowCount"] = dat.RowCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["rowLength"] = dat.RowLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["fixedBytes"] = dat.FixedLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["variableBytes"] = dat.VariableLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["schema"] = table.Name
        };
        var hints = new[]
        {
            $"schema 表：{table.Name}",
            $"行数：{dat.RowCount}",
            $"行宽：{dat.RowLength} bytes",
            $"变量区：{dat.VariableLength} bytes",
            $"列：{safeColumns.Length}/{columns.Length}"
        };

        return new TableInspectResponse(
            resource.ProfileId,
            resource.VirtualPath,
            format,
            Structured: true,
            Delimiter: "datc64-schema",
            PreviewRowCount: rows.Count,
            Rows: rows,
            HexPreview: null,
            Warnings: warnings,
            HeaderFields: header,
            Strings: [],
            LayoutHints: hints,
            Columns: safeColumns.Select(column => column.DisplayName).ToArray(),
            EditableColumnIndexes: safeColumns
                .Select((column, index) => new { column, index })
                .Where(item => !item.column.Array && item.column.Type.Equals("string", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.index)
                .ToArray());
    }

    private static TableInspectResponse? TryInspectDatc64ByStringPointers(
        ResourceSummaryDto resource,
        byte[] data,
        string format,
        int maxRows)
    {
        if (!resource.Extension.Equals(".datc64", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!TryReadDatc64Sections(data, out var dat))
        {
            return null;
        }

        if (dat.RowCount == 0)
        {
            var emptyHeader = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["rowCount"] = "0",
                ["rowLength"] = "0",
                ["fixedBytes"] = dat.FixedLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["variableBytes"] = dat.VariableLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["inferredStringColumns"] = string.Empty
            };
            return new TableInspectResponse(
                resource.ProfileId,
                resource.VirtualPath,
                format,
                Structured: true,
                Delimiter: "datc64-auto",
                PreviewRowCount: 0,
                Rows: [],
                HexPreview: null,
                Warnings:
                [
                    "未匹配到正式 schema，当前识别为无行 datc64 空表；没有可预览或可编辑的数据行。"
                ],
                HeaderFields: emptyHeader,
                Strings: [],
                LayoutHints:
                [
                    "未找到 schema.min.json 表结构，已按 datc64 空表展示。",
                    "行数：0",
                    $"变量区：{dat.VariableLength} bytes"
                ],
                Columns: ["empty"],
                EditableColumnIndexes: []);
        }

        if (dat.RowLength < 4)
        {
            return null;
        }

        var wordFieldCount = Math.Min(dat.RowLength / 4, 128);
        var trailingByteCount = Math.Max(0, dat.RowLength - wordFieldCount * 4);
        var fieldCount = wordFieldCount + (trailingByteCount > 0 ? 1 : 0);
        var stringColumns = new Dictionary<int, int>();
        var rowsToProbe = Math.Min(dat.RowCount, Math.Min(maxRows, 64));
        for (var fieldIndex = 0; fieldIndex < wordFieldCount; fieldIndex++)
        {
            var hits = 0;
            for (var rowIndex = 0; rowIndex < rowsToProbe; rowIndex++)
            {
                var fixedOffset = rowIndex * dat.RowLength + fieldIndex * 4;
                var pointer = ReadUInt32(dat.Fixed, fixedOffset);
                if (TryReadDatc64String(dat.Variable, pointer, requireStrongSignal: true, out _))
                {
                    hits++;
                }
            }

            if (hits >= Math.Max(1, rowsToProbe / 3))
            {
                stringColumns[fieldIndex] = hits;
            }
        }

        var columns = Enumerable.Range(0, fieldCount)
            .Select(index => stringColumns.ContainsKey(index)
                ? $"text_{index} @{index * 4}"
                : index < wordFieldCount
                    ? $"u32_{index} @{index * 4}"
                    : $"tail @{wordFieldCount * 4}")
            .ToArray();
        var rowLimit = Math.Min(dat.RowCount, maxRows);
        var rows = new List<TablePreviewRowDto>(rowLimit);
        for (var rowIndex = 0; rowIndex < rowLimit; rowIndex++)
        {
            var cells = new string[fieldCount];
            for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
            {
                if (fieldIndex >= wordFieldCount)
                {
                    var tailOffset = rowIndex * dat.RowLength + wordFieldCount * 4;
                    cells[fieldIndex] = string.Join(" ", dat.Fixed.Skip(tailOffset).Take(trailingByteCount).Select(item => item.ToString("X2")));
                    continue;
                }

                var fixedOffset = rowIndex * dat.RowLength + fieldIndex * 4;
                var value = ReadUInt32(dat.Fixed, fixedOffset);
                cells[fieldIndex] = stringColumns.ContainsKey(fieldIndex) && TryReadDatc64String(dat.Variable, value, requireStrongSignal: false, out var text)
                    ? text
                    : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            rows.Add(new TablePreviewRowDto(rowIndex + 1, cells, string.Join('\t', cells)));
        }

        var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["rowCount"] = dat.RowCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["rowLength"] = dat.RowLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["fixedBytes"] = dat.FixedLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["variableBytes"] = dat.VariableLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["inferredStringColumns"] = string.Join(",", stringColumns.Keys.Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        };
        var hints = new[]
        {
            stringColumns.Count == 0
                ? "未找到 schema.min.json 表结构，已按 datc64 固定区/变量区自动推断只读数值行列。"
                : "未找到 schema.min.json 表结构，已按 datc64 固定区/变量区自动推断行列；识别出的文本列支持写入草稿层。",
            $"行数：{dat.RowCount}",
            $"行宽：{dat.RowLength} bytes",
            $"变量区：{dat.VariableLength} bytes",
            stringColumns.Count == 0
                ? "推断文本列：无，按固定区数值列展示。"
                : $"推断文本列：{string.Join(" / ", stringColumns.Keys.Select(index => $"@{index * 4}"))}"
        };

        return new TableInspectResponse(
            resource.ProfileId,
            resource.VirtualPath,
            format,
            Structured: true,
            Delimiter: "datc64-auto",
            PreviewRowCount: rows.Count,
            Rows: rows,
            HexPreview: null,
            Warnings:
            [
                stringColumns.Count == 0
                    ? "未匹配到正式 schema，当前为自动推断的只读 datc64 数值行列；保存字段编辑仍需要精确定义结构。"
                    : "未匹配到正式 schema，当前为自动推断的 datc64 行列；已识别的文本列可编辑，数值列保持只读。"
            ],
            HeaderFields: header,
            Strings: [],
            LayoutHints: hints,
            Columns: columns,
            EditableColumnIndexes: stringColumns.Keys.Order().ToArray());
    }

    private static TableInspectResponse? TryInspectLegacyDatWithCatalog(
        ResourceSummaryDto resource,
        byte[] data,
        string format,
        int maxRows)
    {
        if (!resource.Extension.Equals(".dat", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!LegacyDatCatalog.TryGet(Path.GetFileNameWithoutExtension(resource.NormalizedPath), out var table)
            || !TryReadLegacyDatRows(data, table, out var rows, out var consumedBytes))
        {
            return null;
        }

        var rowLimit = Math.Min(rows.Count, maxRows);
        var previewRows = rows
            .Take(rowLimit)
            .Select((row, index) => new TablePreviewRowDto(index + 1, row.Values, string.Join('\t', row.Values)))
            .ToArray();
        var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["rowCount"] = rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["schema"] = table.Name,
            ["consumedBytes"] = consumedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["paddingBytes"] = Math.Max(0, data.Length - consumedBytes).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        var hints = new[]
        {
            $"schema 表：{table.Name}",
            $"行数：{rows.Count}",
            "格式：旧 DAT，可变长 valuestring 行内 UTF-16LE 字符串",
            $"列：{table.Columns.Count}"
        };

        return new TableInspectResponse(
            resource.ProfileId,
            resource.VirtualPath,
            format,
            Structured: true,
            Delimiter: "legacy-dat-schema",
            PreviewRowCount: previewRows.Length,
            Rows: previewRows,
            HexPreview: null,
            Warnings:
            [
                "该表格已按旧 DAT 定义解析为真实行列；valuestring 字段支持写入草稿层，其他字段保持只读。"
            ],
            HeaderFields: header,
            Strings: [],
            LayoutHints: hints,
            Columns: table.Columns.Select(column => column.DisplayName).ToArray(),
            EditableColumnIndexes: table.Columns
                .Select((column, index) => new { column, index })
                .Where(item => item.column.Type.Equals("valuestring", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.index)
                .ToArray());
    }

    public byte[] ApplyDatc64CatalogCellEdits(
        ResourceSummaryDto resource,
        byte[] data,
        IReadOnlyList<TableCellEditDto> edits)
    {
        var result = ApplyDatc64CatalogCellEditsWithReport(resource, data, edits);
        if (result.Skipped.Count > 0)
        {
            throw new InvalidOperationException($"有 {result.Skipped.Count} 个单元格未写入：{string.Join("；", result.Skipped.Take(5))}");
        }

        return result.Data;
    }

    public Datc64EditResult ApplyDatc64CatalogCellEditsWithReport(
        ResourceSummaryDto resource,
        byte[] data,
        IReadOnlyList<TableCellEditDto> edits)
    {
        if (!resource.Extension.Equals(".datc64", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("当前资源不是 .datc64 表格。");
        }

        var tableName = Path.GetFileNameWithoutExtension(resource.NormalizedPath);
        var table = DatSchemaCatalog.TryGet(tableName);
        var minimumFixedRowLength = table is null ? 0 : CalculateCatalogRowLength(table);
        if (!TryReadDatc64Sections(data, minimumFixedRowLength, out var dat))
        {
            if (table is not null && TryReadDatc64Sections(data, out var inferredDat))
            {
                return ApplyInferredDatc64TextCellEditsWithReport(data, edits, inferredDat);
            }

            throw new InvalidOperationException("无法识别 datc64 固定区/变量区边界，不能安全写回。");
        }

        if (table is null)
        {
            return ApplyInferredDatc64TextCellEditsWithReport(data, edits, dat);
        }

        var columns = BuildCatalogColumns(table, dat)
            .Where(column => column.Offset + column.ByteLength <= dat.RowLength)
            .ToArray();
        if (columns.Length == 0)
        {
            throw new InvalidOperationException("没有可写回的 datc64 schema 字段。");
        }

        var editMap = new Dictionary<(int RowIndex, int ColumnIndex), string>();
        foreach (var edit in edits)
        {
            if (edit.RowNumber < 1 || edit.RowNumber > dat.RowCount)
            {
                throw new ArgumentOutOfRangeException(nameof(edits), $"行号超出范围：{edit.RowNumber}");
            }

            if (edit.ColumnIndex < 0 || edit.ColumnIndex >= columns.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(edits), $"列号超出范围：{edit.ColumnIndex}");
            }

            var column = columns[edit.ColumnIndex];
            if (column.Array || !column.Type.Equals("string", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"列 {column.Name} 是 {column.Type} 类型，当前只支持 string 字段写回。");
            }

            editMap[(edit.RowNumber - 1, edit.ColumnIndex)] = edit.Value;
        }

        if (editMap.Count == 0)
        {
            return new Datc64EditResult(data.AsSpan(0, data.Length).ToArray(), 0, []);
        }

        var fixedData = dat.Fixed.AsSpan(0, dat.Fixed.Length).ToArray();
        using var variable = new MemoryStream();
        variable.Write(dat.Variable, 0, dat.Variable.Length);
        var applied = 0;
        var skipped = new List<string>();
        foreach (var ((rowIndex, columnIndex), value) in editMap.OrderBy(item => item.Key.RowIndex).ThenBy(item => item.Key.ColumnIndex))
        {
            var column = columns[columnIndex];
            var originalValue = ReadCatalogStringCell(dat, rowIndex, column);
            if (string.Equals(originalValue, value, StringComparison.Ordinal))
            {
                continue;
            }

            if (!CanSafelyRewriteDatc64Text(resource, column, originalValue, value))
            {
                skipped.Add(FormatDatc64Skip(rowIndex, columnIndex, column.Name, originalValue, value));
                continue;
            }

            if (string.IsNullOrEmpty(value))
            {
                WriteUInt32(fixedData, ResolveCatalogStringPointerOffset(dat, rowIndex, column), 0xfefefefe);
                applied++;
                continue;
            }

            if (variable.Position > uint.MaxValue)
            {
                throw new InvalidOperationException("datc64 变量区超过 4GB，不能安全写回。");
            }

            WriteUInt32(fixedData, ResolveCatalogStringPointerOffset(dat, rowIndex, column), checked((uint)variable.Position));
            var bytes = Encoding.Unicode.GetBytes(value);
            variable.Write(bytes, 0, bytes.Length);
            variable.WriteByte(0);
            variable.WriteByte(0);
            variable.WriteByte(0);
            variable.WriteByte(0);
            applied++;
        }

        var variableData = variable.ToArray();
        var output = new byte[4 + fixedData.Length + variableData.Length];
        WriteUInt32(output, 0, checked((uint)dat.RowCount));
        fixedData.CopyTo(output, 4);
        variableData.CopyTo(output, 4 + fixedData.Length);
        return new Datc64EditResult(output, applied, skipped);
    }

    public byte[] ApplyLegacyDatCatalogCellEdits(
        ResourceSummaryDto resource,
        byte[] data,
        IReadOnlyList<TableCellEditDto> edits)
    {
        if (!resource.Extension.Equals(".dat", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("当前资源不是 .dat 表格。");
        }

        if (!LegacyDatCatalog.TryGet(Path.GetFileNameWithoutExtension(resource.NormalizedPath), out var table)
            || !TryReadLegacyDatRows(data, table, out var rows, out var consumedBytes))
        {
            throw new InvalidOperationException("无法识别旧 DAT 表结构，不能安全写回。");
        }

        var editMap = new Dictionary<(int RowIndex, int ColumnIndex), string>();
        foreach (var edit in edits)
        {
            if (edit.RowNumber < 1 || edit.RowNumber > rows.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(edits), $"行号超出范围：{edit.RowNumber}");
            }

            if (edit.ColumnIndex < 0 || edit.ColumnIndex >= table.Columns.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(edits), $"列号超出范围：{edit.ColumnIndex}");
            }

            var column = table.Columns[edit.ColumnIndex];
            if (!column.Type.Equals("valuestring", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"列 {column.Name} 是 {column.Type} 类型，当前只支持 valuestring 字段写回。");
            }

            editMap[(edit.RowNumber - 1, edit.ColumnIndex)] = edit.Value;
        }

        if (editMap.Count == 0)
        {
            return data.AsSpan(0, data.Length).ToArray();
        }

        var padding = data.AsSpan(consumedBytes).ToArray();
        using var output = new MemoryStream();
        WriteUInt32(output, checked((uint)rows.Count));
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var source = rows[rowIndex].Values.ToArray();
            foreach (var ((editRowIndex, editColumnIndex), value) in editMap)
            {
                if (editRowIndex == rowIndex)
                {
                    source[editColumnIndex] = value;
                }
            }

            WriteLegacyDatRow(output, table, source);
        }

        output.Write(padding, 0, padding.Length);
        return output.ToArray();
    }

    private static byte[] ApplyInferredDatc64TextCellEdits(
        byte[] data,
        IReadOnlyList<TableCellEditDto> edits,
        Datc64Sections dat)
    {
        var result = ApplyInferredDatc64TextCellEditsWithReport(data, edits, dat);
        if (result.Skipped.Count > 0)
        {
            throw new InvalidOperationException($"有 {result.Skipped.Count} 个单元格未写入：{string.Join("；", result.Skipped.Take(5))}");
        }

        return result.Data;
    }

    private static Datc64EditResult ApplyInferredDatc64TextCellEditsWithReport(
        byte[] data,
        IReadOnlyList<TableCellEditDto> edits,
        Datc64Sections dat)
    {
        var editableColumns = InferDatc64StringColumns(dat, Math.Min(dat.RowCount, 64));
        if (editableColumns.Count == 0)
        {
            throw new InvalidOperationException("自动结构没有识别到可安全写回的文本列。");
        }

        var editMap = new Dictionary<(int RowIndex, int ColumnIndex), string>();
        foreach (var edit in edits)
        {
            if (edit.RowNumber < 1 || edit.RowNumber > dat.RowCount)
            {
                throw new ArgumentOutOfRangeException(nameof(edits), $"行号超出范围：{edit.RowNumber}");
            }

            if (edit.ColumnIndex < 0 || edit.ColumnIndex >= dat.RowLength / 4)
            {
                throw new ArgumentOutOfRangeException(nameof(edits), $"列号超出范围：{edit.ColumnIndex}");
            }

            if (!editableColumns.ContainsKey(edit.ColumnIndex))
            {
                throw new InvalidOperationException($"列 {edit.ColumnIndex} 不是自动识别的文本列，不能安全写回。");
            }

            editMap[(edit.RowNumber - 1, edit.ColumnIndex)] = edit.Value;
        }

        if (editMap.Count == 0)
        {
            return new Datc64EditResult(data.AsSpan(0, data.Length).ToArray(), 0, []);
        }

        var fixedData = dat.Fixed.AsSpan(0, dat.Fixed.Length).ToArray();
        using var variable = new MemoryStream();
        variable.Write(dat.Variable, 0, dat.Variable.Length);
        var applied = 0;
        var skipped = new List<string>();
        foreach (var ((rowIndex, columnIndex), value) in editMap.OrderBy(item => item.Key.RowIndex).ThenBy(item => item.Key.ColumnIndex))
        {
            var fixedOffset = rowIndex * dat.RowLength + columnIndex * 4;
            var originalPointer = ReadUInt32(dat.Fixed, fixedOffset);
            var originalValue = ReadDatc64String(dat.Variable, originalPointer);
            if (string.Equals(originalValue, value, StringComparison.Ordinal))
            {
                continue;
            }

            if (!CanSafelyRewriteDatc64Text(originalValue, value))
            {
                skipped.Add(FormatDatc64Skip(rowIndex, columnIndex, $"text_{columnIndex}", originalValue, value));
                continue;
            }

            if (string.IsNullOrEmpty(value))
            {
                WriteUInt32(fixedData, fixedOffset, 0xfefefefe);
                applied++;
                continue;
            }

            if (variable.Position > uint.MaxValue)
            {
                throw new InvalidOperationException("datc64 变量区超过 4GB，不能安全写回。");
            }

            WriteUInt32(fixedData, fixedOffset, checked((uint)variable.Position));
            var bytes = Encoding.Unicode.GetBytes(value);
            variable.Write(bytes, 0, bytes.Length);
            variable.WriteByte(0);
            variable.WriteByte(0);
            variable.WriteByte(0);
            variable.WriteByte(0);
            applied++;
        }

        var variableData = variable.ToArray();
        var output = new byte[4 + fixedData.Length + variableData.Length];
        WriteUInt32(output, 0, checked((uint)dat.RowCount));
        fixedData.CopyTo(output, 4);
        variableData.CopyTo(output, 4 + fixedData.Length);
        return new Datc64EditResult(output, applied, skipped);
    }

    private static Dictionary<int, int> InferDatc64StringColumns(Datc64Sections dat, int rowsToProbe)
    {
        var wordFieldCount = Math.Min(dat.RowLength / 4, 128);
        var stringColumns = new Dictionary<int, int>();
        for (var fieldIndex = 0; fieldIndex < wordFieldCount; fieldIndex++)
        {
            var hits = 0;
            for (var rowIndex = 0; rowIndex < rowsToProbe; rowIndex++)
            {
                var fixedOffset = rowIndex * dat.RowLength + fieldIndex * 4;
                var pointer = ReadUInt32(dat.Fixed, fixedOffset);
                if (TryReadDatc64String(dat.Variable, pointer, requireStrongSignal: true, out _))
                {
                    hits++;
                }
            }

            if (hits >= Math.Max(1, rowsToProbe / 3))
            {
                stringColumns[fieldIndex] = hits;
            }
        }

        return stringColumns;
    }

    private static CatalogColumn[] BuildCatalogColumns(DatSchemaCatalog.Table table, Datc64Sections dat)
    {
        var columns = new List<CatalogColumn>();
        var offset = 0;
        var unnamed = 1;
        foreach (var schemaColumn in table.Columns)
        {
            var size = CatalogColumnSize(schemaColumn);
            if (size <= 0)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(schemaColumn.Name)
                ? $"未知字段{unnamed++}"
                : schemaColumn.Name!;
            columns.Add(new CatalogColumn(
                name,
                $"{name} @{offset}",
                schemaColumn.Type,
                schemaColumn.Array,
                offset,
                size));
            offset += size;
        }

        return columns.ToArray();
    }

    private static int CatalogColumnSize(DatSchemaCatalog.Column column)
    {
        if (column.Array) return 16;
        return column.Type.ToLowerInvariant() switch
        {
            "bool" => 1,
            "u8" or "i8" => 1,
            "u16" or "i16" => 2,
            "u32" or "i32" or "enumrow" or "f32" => 4,
            "u64" or "i64" or "f64" => 8,
            "string" or "row" => 8,
            "foreignrow" => 16,
            _ when column.Type.StartsWith("opaque", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(column.Type[6..], out var opaqueSize) => opaqueSize,
            _ when column.Type.StartsWith("padding", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(column.Type[7..], out var paddingSize) => paddingSize,
            _ => 0
        };
    }

    private static int CalculateCatalogRowLength(DatSchemaCatalog.Table table)
    {
        return table.Columns.Sum(CatalogColumnSize);
    }

    private static string ReadCatalogCell(Datc64Sections dat, int rowIndex, CatalogColumn column)
    {
        var offset = rowIndex * dat.RowLength + column.Offset;
        if (offset < 0 || offset + column.ByteLength > dat.FixedLength)
        {
            return string.Empty;
        }

        if (column.Array)
        {
            var length = ReadUInt32(dat.Fixed, offset);
            var variableOffset = ReadUInt32(dat.Fixed, offset + 8 <= dat.FixedLength ? offset + 8 : offset + 4);
            return length == 0 ? "[]" : $"[{length}] @{variableOffset}";
        }

        return column.Type.ToLowerInvariant() switch
        {
            "bool" => dat.Fixed[offset] == 0 ? "false" : "true",
            "u8" => dat.Fixed[offset].ToString(System.Globalization.CultureInfo.InvariantCulture),
            "i8" => unchecked((sbyte)dat.Fixed[offset]).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "u16" => ReadUInt16(dat.Fixed, offset).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "i16" => unchecked((short)ReadUInt16(dat.Fixed, offset)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "u32" => ReadUInt32(dat.Fixed, offset).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "i32" or "enumrow" => unchecked((int)ReadUInt32(dat.Fixed, offset)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "u64" => ReadUInt64(dat.Fixed, offset).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "i64" => unchecked((long)ReadUInt64(dat.Fixed, offset)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "f32" => ReadSingle(dat.Fixed, offset).ToString("G9", System.Globalization.CultureInfo.InvariantCulture),
            "string" => ReadCatalogStringCell(dat, rowIndex, column),
            "row" => FormatRowReference(ReadUInt32(dat.Fixed, offset)),
            "foreignrow" => FormatRowReference(ReadUInt32(dat.Fixed, offset)),
            _ when column.Type.StartsWith("opaque", StringComparison.OrdinalIgnoreCase)
                || column.Type.StartsWith("padding", StringComparison.OrdinalIgnoreCase) => string.Join(" ", dat.Fixed.Skip(offset).Take(column.ByteLength).Select(item => item.ToString("X2"))),
            _ => string.Empty
        };
    }

    private static string FormatRowReference(uint value)
    {
        return value == 0xfefefefe ? "" : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ReadCatalogStringCell(Datc64Sections dat, int rowIndex, CatalogColumn column)
    {
        var offset = rowIndex * dat.RowLength + column.Offset;
        if (column.ByteLength >= 8 && offset + 8 <= dat.FixedLength)
        {
            var second = ReadUInt32(dat.Fixed, offset + 4);
            if (TryReadDatc64String(dat.Variable, second, requireStrongSignal: false, out var secondValue))
            {
                return secondValue;
            }
        }

        return ReadDatc64String(dat.Variable, ReadUInt32(dat.Fixed, offset));
    }

    private static int ResolveCatalogStringPointerOffset(Datc64Sections dat, int rowIndex, CatalogColumn column)
    {
        var offset = rowIndex * dat.RowLength + column.Offset;
        if (column.ByteLength >= 8 && offset + 8 <= dat.FixedLength)
        {
            var second = ReadUInt32(dat.Fixed, offset + 4);
            if (TryReadDatc64String(dat.Variable, second, requireStrongSignal: false, out _))
            {
                return offset + 4;
            }
        }

        return offset;
    }

    private static string ReadDatc64String(byte[] variable, uint pointer)
    {
        return TryReadDatc64String(variable, pointer, requireStrongSignal: false, out var value) ? value : string.Empty;
    }

    private static bool CanSafelyRewriteDatc64Text(string originalValue, string newValue)
    {
        if (string.IsNullOrWhiteSpace(originalValue) && !string.IsNullOrWhiteSpace(newValue))
        {
            return false;
        }

        return Datc64MarkupKeys(originalValue).SequenceEqual(Datc64MarkupKeys(newValue), StringComparer.Ordinal);
    }

    private static bool CanSafelyRewriteDatc64Text(CatalogColumn column, string originalValue, string newValue)
    {
        if (!CanSafelyRewriteDatc64Text(originalValue, newValue))
        {
            return false;
        }

        if (IsDatc64FilePathColumn(column, originalValue) && !IsFilePathLike(newValue))
        {
            return false;
        }

        if (IsDatc64InternalIdColumn(column, originalValue) && !LooksLikeInternalId(newValue))
        {
            return false;
        }

        return true;
    }

    private static bool CanSafelyRewriteDatc64Text(ResourceSummaryDto resource, CatalogColumn column, string originalValue, string newValue)
    {
        if (IsIncursionRoomPerLevelTable(resource))
        {
            return true;
        }

        if (!CanSafelyRewriteDatc64Markup(resource, column, originalValue, newValue))
        {
            return false;
        }

        if (IsDatc64FilePathColumn(column, originalValue) && !IsFilePathLike(newValue))
        {
            return false;
        }

        if (IsDatc64InternalIdColumn(column, originalValue) && !LooksLikeInternalId(newValue))
        {
            return false;
        }

        return true;
    }

    private static bool CanSafelyRewriteDatc64Markup(ResourceSummaryDto resource, CatalogColumn column, string originalValue, string newValue)
    {
        if (CanSafelyRewriteDatc64Text(originalValue, newValue))
        {
            return true;
        }

        if (!IsIncursionRoomPerLevelTable(resource) || !IsIncursionRoomDescriptionColumn(column))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(originalValue) && !string.IsNullOrWhiteSpace(newValue))
        {
            return false;
        }

        var originalKeys = Datc64MarkupKeys(originalValue);
        var newKeys = Datc64MarkupKeys(newValue);
        return originalKeys.Count > 0 && ContainsOrderedSubsequence(newKeys, originalKeys);
    }

    private static bool IsIncursionRoomPerLevelTable(ResourceSummaryDto resource)
    {
        var tableName = Path.GetFileNameWithoutExtension(resource.NormalizedPath);
        return tableName.Equals("incursion2roomperlevel", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIncursionRoomDescriptionColumn(CatalogColumn column)
    {
        return column.Name.Equals("Description", StringComparison.OrdinalIgnoreCase)
            || column.Name.Equals("Description2", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsOrderedSubsequence(IReadOnlyList<string> values, IReadOnlyList<string> subsequence)
    {
        var index = 0;
        foreach (var value in values)
        {
            if (string.Equals(value, subsequence[index], StringComparison.Ordinal))
            {
                index++;
                if (index == subsequence.Count)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string FormatDatc64Skip(int rowIndex, int columnIndex, string columnName, string originalValue, string newValue)
    {
        return $"第 {rowIndex + 1} 行第 {columnIndex + 1} 列 {columnName}：{Datc64SkipReason(originalValue, newValue)}";
    }

    private static string Datc64SkipReason(string originalValue, string newValue)
    {
        if (string.IsNullOrWhiteSpace(originalValue) && !string.IsNullOrWhiteSpace(newValue))
        {
            return "原文为空或不是可读文本";
        }

        var originalKeys = Datc64MarkupKeys(originalValue);
        var newKeys = Datc64MarkupKeys(newValue);
        if (!originalKeys.SequenceEqual(newKeys, StringComparer.Ordinal))
        {
            return $"标记键不一致（原：{string.Join(",", originalKeys)}；新：{string.Join(",", newKeys)}）";
        }

        return "安全规则拒绝写入";
    }

    private static bool IsDatc64FilePathColumn(CatalogColumn column, string value)
    {
        return column.Name.Contains("File", StringComparison.OrdinalIgnoreCase)
            || column.Name.Contains("Path", StringComparison.OrdinalIgnoreCase)
            || column.Name.Contains("Metadata", StringComparison.OrdinalIgnoreCase)
            || IsFilePathLike(value);
    }

    private static bool IsDatc64InternalIdColumn(CatalogColumn column, string value)
    {
        return (column.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)
                || column.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
                || column.Name.Contains("HASH", StringComparison.OrdinalIgnoreCase)
                || column.Name.Contains("Script", StringComparison.OrdinalIgnoreCase))
            && LooksLikeInternalId(value);
    }

    private static bool LooksLikeInternalId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(IsCjk))
        {
            return false;
        }

        return value.Any(ch => ch is '_' or ':' or '.')
            || value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.');
    }

    private static bool IsFilePathLike(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains('/', StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".ot", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".otc", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".epk", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".aoc", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".ao", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".bank", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCjk(char ch)
    {
        return (ch >= '\u3400' && ch <= '\u4DBF')
            || (ch >= '\u4E00' && ch <= '\u9FFF')
            || (ch >= '\uF900' && ch <= '\uFAFF');
    }

    private static IReadOnlyList<string> Datc64MarkupKeys(string value)
    {
        var keys = new List<string>();
        var index = 0;
        while (index < value.Length)
        {
            var open = value.IndexOf('[', index);
            if (open < 0)
            {
                break;
            }

            var close = value.IndexOf(']', open + 1);
            if (close < 0)
            {
                keys.Add("<unclosed>");
                break;
            }

            var pipe = value.IndexOf('|', open + 1, close - open - 1);
            keys.Add(pipe > open ? value[(open + 1)..pipe] : value[(open + 1)..close]);
            index = close + 1;
        }

        return keys;
    }

    private static bool TryReadDatc64String(byte[] variable, uint pointer, bool requireStrongSignal, out string value)
    {
        value = string.Empty;
        if (pointer == 0xfefefefe || pointer >= variable.Length || pointer % 2 != 0 || PointsIntoDatc64VariablePadding(variable, pointer))
        {
            return false;
        }

        var start = checked((int)pointer);
        var end = -1;
        for (var index = start; index + 3 < variable.Length; index += 2)
        {
            if (variable[index] == 0 && variable[index + 1] == 0 && variable[index + 2] == 0 && variable[index + 3] == 0)
            {
                end = index;
                break;
            }
        }

        if (end < start || (end - start) % 2 != 0)
        {
            return false;
        }

        value = Encoding.Unicode.GetString(variable, start, end - start);
        if (value.Length == 0 || ContainsUnsafePreviewText(value))
        {
            return false;
        }

        return !requireStrongSignal || IsUsefulDecodedString(value, requireStrongSignal: true);
    }

    private static bool PointsIntoDatc64VariablePadding(byte[] variable, uint pointer)
    {
        if (pointer >= 8 || variable.Length < 8)
        {
            return false;
        }

        for (var index = 0; index < 8; index++)
        {
            if (variable[index] != 0xbb)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsUnsafePreviewText(string value)
    {
        return value.Any(ch => (char.IsControl(ch) && !char.IsWhiteSpace(ch)) || ch == '\uFFFD');
    }

    private static bool TryReadDatc64Sections(byte[] data, out Datc64Sections sections)
    {
        return TryReadDatc64Sections(data, minimumFixedRowLength: 0, out sections);
    }

    private static bool TryReadDatc64Sections(byte[] data, int minimumFixedRowLength, out Datc64Sections sections)
    {
        sections = default;
        if (data.Length < 12)
        {
            return false;
        }

        var rowCountValue = ReadUInt32(data, 0);
        if (rowCountValue > 1_000_000)
        {
            return false;
        }

        var rowCount = (int)rowCountValue;
        var minimumBoundary64 = rowCount > 0 ? (long)minimumFixedRowLength * rowCount : 0;
        if (minimumBoundary64 > data.Length - 4L)
        {
            return false;
        }

        var minimumBoundary = (int)minimumBoundary64;
        var boundary = FindDatc64VariableBoundary(data, rowCount, minimumBoundary);
        if (boundary < 0)
        {
            return false;
        }

        var rowLength = rowCount > 0 ? boundary / rowCount : 0;
        if (rowCount > 0 && rowLength <= 0)
        {
            return false;
        }

        var fixedData = data.AsSpan(4, boundary).ToArray();
        var variableData = data.AsSpan(4 + boundary).ToArray();
        sections = new Datc64Sections(rowCount, rowLength, fixedData, variableData);
        return true;
    }

    private static int FindDatc64VariableBoundary(byte[] data, int rowCount, int minimumBoundary)
    {
        var startOffset = checked(4 + Math.Max(0, minimumBoundary));
        for (var offset = startOffset; offset + 7 < data.Length; offset++)
        {
            if (rowCount > 0 && (offset - 4) % rowCount != 0)
            {
                continue;
            }

            var found = true;
            for (var index = 0; index < 8; index++)
            {
                if (data[offset + index] != 0xbb)
                {
                    found = false;
                    break;
                }
            }

            if (found)
            {
                return offset - 4;
            }
        }

        return -1;
    }

    private static bool TryReadLegacyDatRows(
        byte[] data,
        LegacyDatCatalog.Table table,
        out IReadOnlyList<LegacyDatRow> rows,
        out int consumedBytes)
    {
        rows = [];
        consumedBytes = 0;
        if (data.Length < 4)
        {
            return false;
        }

        var rowCountValue = ReadUInt32(data, 0);
        if (rowCountValue > 1_000_000)
        {
            return false;
        }

        var rowCount = (int)rowCountValue;
        var offset = 4;
        var parsed = new List<LegacyDatRow>(Math.Min(rowCount, 4096));
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var values = new string[table.Columns.Count];
            for (var columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                var column = table.Columns[columnIndex];
                if (column.Type.Equals("i32", StringComparison.OrdinalIgnoreCase))
                {
                    if (offset + 4 > data.Length)
                    {
                        return false;
                    }

                    values[columnIndex] = unchecked((int)ReadUInt32(data, offset)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    offset += 4;
                }
                else if (column.Type.Equals("valuestring", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryReadLegacyDatValueString(data, ref offset, out var value))
                    {
                        return false;
                    }

                    values[columnIndex] = value;
                }
                else
                {
                    return false;
                }
            }

            parsed.Add(new LegacyDatRow(values));
        }

        consumedBytes = offset;
        rows = parsed;
        return true;
    }

    private static bool TryReadLegacyDatValueString(byte[] data, ref int offset, out string value)
    {
        value = string.Empty;
        var start = offset;
        for (var index = start; index + 1 < data.Length; index += 2)
        {
            if (data[index] == 0 && data[index + 1] == 0)
            {
                if ((index - start) % 2 != 0)
                {
                    return false;
                }

                value = Encoding.Unicode.GetString(data, start, index - start);
                if (ContainsUnsafePreviewText(value))
                {
                    return false;
                }

                offset = index + 2;
                return true;
            }
        }

        return false;
    }

    private static void WriteLegacyDatRow(Stream stream, LegacyDatCatalog.Table table, IReadOnlyList<string> values)
    {
        for (var columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var column = table.Columns[columnIndex];
            var value = columnIndex < values.Count ? values[columnIndex] : string.Empty;
            if (column.Type.Equals("i32", StringComparison.OrdinalIgnoreCase))
            {
                WriteInt32(stream, int.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                continue;
            }

            if (column.Type.Equals("valuestring", StringComparison.OrdinalIgnoreCase))
            {
                WriteUtf16ZeroTerminated(stream, value);
                continue;
            }

            throw new InvalidOperationException($"旧 DAT 字段类型不支持：{column.Type}");
        }
    }

    private static IReadOnlyList<TablePreviewRowDto> BuildStringCandidateRows(
        IReadOnlyList<TableStringCandidateDto> strings,
        int maxRows)
    {
        return strings
            .Take(maxRows)
            .Select((item, index) => new TablePreviewRowDto(
                index + 1,
                [
                    (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    item.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    item.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    item.Encoding,
                    item.Value
                ],
                item.Value))
            .ToArray();
    }

    private static IReadOnlyList<TablePreviewRowDto> BuildNumericWordRows(byte[] data, int maxRows)
    {
        var count = Math.Min(data.Length / 4, maxRows);
        var rows = new List<TablePreviewRowDto>(count);
        for (var index = 0; index < count; index++)
        {
            var offset = index * 4;
            var u32 = ReadUInt32(data, offset);
            var i32 = unchecked((int)u32);
            var hex = string.Join(" ", data.Skip(offset).Take(4).Select(item => item.ToString("X2")));
            var cells = new[]
            {
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
                u32.ToString(System.Globalization.CultureInfo.InvariantCulture),
                i32.ToString(System.Globalization.CultureInfo.InvariantCulture),
                hex
            };
            rows.Add(new TablePreviewRowDto(index + 1, cells, string.Join('\t', cells)));
        }

        return rows;
    }

    private static IEnumerable<string> ValidateSchema(TableSchemaDto schema)
    {
        if (schema.RecordSize <= 0)
        {
            yield return "schema recordSize 必须大于 0。";
        }

        if (schema.HeaderSize < 0)
        {
            yield return "schema headerSize 不能小于 0。";
        }

        if (schema.Fields.Count == 0)
        {
            yield return "schema 至少需要一个字段。";
        }

        foreach (var field in schema.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name))
            {
                yield return "schema 字段名不能为空。";
            }

            if (field.Offset < 0 || field.Offset >= schema.RecordSize)
            {
                yield return $"字段 {field.Name} offset 超出 recordSize。";
            }

            var size = FieldSize(field);
            if (size <= 0)
            {
                yield return $"字段 {field.Name} 类型或长度不支持。";
            }
            else if (field.Offset + size > schema.RecordSize)
            {
                yield return $"字段 {field.Name} 超出 recordSize。";
            }
        }

        var orderedFields = schema.Fields
            .Select(field => new { Field = field, Start = field.Offset, End = field.Offset + FieldSize(field) })
            .Where(item => item.End > item.Start)
            .OrderBy(item => item.Start)
            .ToArray();
        for (var index = 1; index < orderedFields.Length; index++)
        {
            var previous = orderedFields[index - 1];
            var current = orderedFields[index];
            if (previous.End > current.Start)
            {
                yield return $"字段 {previous.Field.Name} 与 {current.Field.Name} 重叠，可能导致错位显示。";
            }
        }
    }

    private static string ReadField(byte[] data, int rowOffset, TableSchemaFieldDto field)
    {
        var offset = rowOffset + field.Offset;
        if (offset < 0 || offset >= data.Length)
        {
            return string.Empty;
        }

        return field.Type.ToLowerInvariant() switch
        {
            "u8" => data[offset].ToString(),
            "u16" when offset + 2 <= data.Length => ReadUInt16(data, offset).ToString(),
            "i16" when offset + 2 <= data.Length => unchecked((short)ReadUInt16(data, offset)).ToString(),
            "u32" when offset + 4 <= data.Length => ReadUInt32(data, offset).ToString(),
            "i32" when offset + 4 <= data.Length => unchecked((int)ReadUInt32(data, offset)).ToString(),
            "u64" when offset + 8 <= data.Length => ReadUInt64(data, offset).ToString(),
            "float" when offset + 4 <= data.Length => ReadSingle(data, offset).ToString("G9", System.Globalization.CultureInfo.InvariantCulture),
            "ascii" => ReadAscii(data, offset, field.Length ?? 0),
            "utf8z" => ReadUtf8Z(data, offset, field.Length ?? 0),
            "utf16z" => ReadUtf16Z(data, offset, field.Length ?? 0),
            _ => string.Empty
        };
    }

    private static void WriteField(byte[] data, int offset, TableSchemaFieldDto field, string value)
    {
        if (offset < 0 || offset + FieldSize(field) > data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(field), $"字段 {field.Name} 超出文件范围。");
        }

        switch (field.Type.ToLowerInvariant())
        {
            case "u8":
                data[offset] = byte.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "u16":
                WriteUInt16(data, offset, ushort.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case "i16":
                WriteUInt16(data, offset, unchecked((ushort)short.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
                break;
            case "u32":
                WriteUInt32(data, offset, uint.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case "i32":
                WriteUInt32(data, offset, unchecked((uint)int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
                break;
            case "u64":
                WriteUInt64(data, offset, ulong.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case "float":
                WriteSingle(data, offset, float.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case "ascii":
                WriteAscii(data, offset, field.Length.GetValueOrDefault(), value);
                break;
            case "utf8z":
                WriteUtf8Z(data, offset, field.Length.GetValueOrDefault(), value);
                break;
            case "utf16z":
                WriteUtf16Z(data, offset, field.Length.GetValueOrDefault(), value);
                break;
            default:
                throw new InvalidOperationException($"字段 {field.Name} 类型不支持写入。");
        }
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

    private static string ReadAscii(byte[] data, int offset, int length)
    {
        if (length <= 0 || offset + length > data.Length)
        {
            return string.Empty;
        }

        return Encoding.ASCII.GetString(data, offset, length).TrimEnd('\0', ' ');
    }

    private static string ReadUtf8Z(byte[] data, int offset, int length)
    {
        if (length <= 0 || offset + length > data.Length)
        {
            return string.Empty;
        }

        var end = offset;
        var max = offset + length;
        while (end < max && data[end] != 0)
        {
            end++;
        }

        return Encoding.UTF8.GetString(data, offset, end - offset);
    }

    private static string ReadUtf16Z(byte[] data, int offset, int length)
    {
        if (length <= 0 || offset + length > data.Length)
        {
            return string.Empty;
        }

        var end = offset;
        var max = offset + length - 1;
        while (end < max && (data[end] != 0 || data[end + 1] != 0))
        {
            end += 2;
        }

        if ((end - offset) % 2 != 0)
        {
            end--;
        }

        return Encoding.Unicode.GetString(data, offset, Math.Max(0, end - offset));
    }

    private static IReadOnlyList<TableStringCandidateDto> ExtractStringCandidates(byte[] data, int maxCount)
    {
        return ExtractAsciiStrings(data, maxCount)
            .Concat(ExtractUtf8Strings(data, maxCount))
            .Concat(ExtractUtf16LeStrings(data, maxCount))
            .GroupBy(item => $"{item.Offset}:{item.Length}:{item.Value}", StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.Offset)
            .ThenBy(item => item.Encoding, StringComparer.Ordinal)
            .Take(maxCount)
            .ToArray();
    }

    private static IReadOnlyList<TableStringCandidateDto> ExtractAsciiStrings(byte[] data, int maxCount)
    {
        var result = new List<TableStringCandidateDto>();
        var start = -1;
        for (var index = 0; index <= data.Length; index++)
        {
            var isPrintable = index < data.Length && data[index] is >= 32 and <= 126;
            if (isPrintable)
            {
                if (start < 0)
                {
                    start = index;
                }
            }
            else if (start >= 0)
            {
                var length = index - start;
                if (length >= 4 && IsLikelyStandaloneAsciiString(data, start, length))
                {
                    result.Add(new TableStringCandidateDto(start, length, Encoding.ASCII.GetString(data, start, length)));
                    if (result.Count >= maxCount)
                    {
                        return result;
                    }
                }

                start = -1;
            }
        }

        return result;
    }

    private static bool IsLikelyStandaloneAsciiString(byte[] data, int offset, int length)
    {
        var value = Encoding.ASCII.GetString(data, offset, length);
        if (value.Count(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '/' or '.' or ' ') < length * 0.75)
        {
            return false;
        }

        var beforeLooksUtf16 = offset % 2 != 0 && offset > 0 && data[offset - 1] == 0;
        var afterLooksUtf16 = offset % 2 != 0 && offset + length < data.Length && data[offset + length] == 0;
        return !(beforeLooksUtf16 || afterLooksUtf16);
    }

    private static IReadOnlyList<TableStringCandidateDto> ExtractUtf8Strings(byte[] data, int maxCount)
    {
        var result = new List<TableStringCandidateDto>();
        for (var index = 0; index < data.Length;)
        {
            if (data[index] < 0x80)
            {
                index++;
                continue;
            }

            var start = index;
            var buffer = new List<byte>();
            while (index < data.Length && data[index] != 0)
            {
                buffer.Add(data[index]);
                index++;
            }

            if (TryDecodeString(buffer.ToArray(), Encoding.UTF8, out var value) && IsUsefulDecodedString(value))
            {
                result.Add(new TableStringCandidateDto(start, buffer.Count, value, "utf-8"));
                if (result.Count >= maxCount) return result;
            }

            index = Math.Max(index + 1, start + 1);
        }

        return result;
    }

    private static IReadOnlyList<TableStringCandidateDto> ExtractUtf16LeStrings(byte[] data, int maxCount)
    {
        var result = new List<TableStringCandidateDto>();
        for (var index = 0; index + 3 < data.Length; index += 2)
        {
            if (!LooksLikeUtf16LeStart(data, index))
            {
                continue;
            }

            var start = index;
            var end = index;
            var terminated = false;
            while (end + 1 < data.Length)
            {
                if (data[end] == 0 && data[end + 1] == 0)
                {
                    terminated = true;
                    break;
                }

                end += 2;
            }

            var length = end - start;
            if (terminated
                && length >= 8
                && TryDecodeString(data.AsSpan(start, length).ToArray(), Encoding.Unicode, out var value)
                && IsUsefulDecodedString(value, requireStrongSignal: true))
            {
                result.Add(new TableStringCandidateDto(start, length, value, "utf-16le"));
                if (result.Count >= maxCount) return result;
            }

            index = Math.Max(index, end);
        }

        return result;
    }

    private static bool LooksLikeUtf16LeStart(byte[] data, int offset)
    {
        if (offset + 3 >= data.Length) return false;
        var first = ReadUInt16(data, offset);
        var second = ReadUInt16(data, offset + 2);
        return IsLikelyTextScalar((char)first) && IsLikelyTextScalar((char)second);
    }

    private static bool TryDecodeString(byte[] bytes, Encoding encoding, out string value)
    {
        value = string.Empty;
        try
        {
            var decoder = Encoding.GetEncoding(
                encoding.CodePage,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            value = decoder.GetString(bytes).TrimEnd('\0');
            return !value.Contains('\uFFFD', StringComparison.Ordinal);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsUsefulDecodedString(string value, bool requireStrongSignal = false)
    {
        if (value.Length < 2) return false;
        var useful = value.Count(IsUsefulTextChar);
        if (useful < 2 || useful < value.Length * 0.75)
        {
            return false;
        }

        if (!requireStrongSignal)
        {
            return true;
        }

        var nonAsciiLetters = value.Count(IsCjkTextChar);
        var asciiText = value.Count(IsAsciiTextChar);
        var suspicious = value.Count(IsSuspiciousDecodedChar);
        var separators = value.Count(IsTextSeparatorChar);
        return suspicious == 0
            && value.Length >= 4
            && (asciiText >= 4 || (nonAsciiLetters >= 2 && (asciiText >= 2 || separators > 0 || value.Length <= 8)));
    }

    private static bool IsUsefulTextChar(char ch)
    {
        if (IsAsciiTextChar(ch)) return true;
        if (IsCjkTextChar(ch)) return true;
        if (char.IsWhiteSpace(ch)) return true;
        if (ch is >= '\u3000' and <= '\u303f') return true;
        if (ch is >= '\uff00' and <= '\uffef') return true;
        return ch is '-' or '_' or ':' or ';' or ',' or '.' or '/' or '\\' or '\'' or '"' or '(' or ')' or '[' or ']';
    }

    private static bool IsTextSeparatorChar(char ch)
    {
        return char.IsWhiteSpace(ch)
            || ch is '-' or '_' or ':' or ';' or ',' or '.' or '/' or '\\' or '\'' or '"' or '(' or ')' or '[' or ']'
            || ch is >= '\u3000' and <= '\u303f'
            || ch is >= '\uff00' and <= '\uffef';
    }

    private static bool IsLikelyTextScalar(char ch)
    {
        return IsAsciiTextChar(ch) || IsCjkTextChar(ch) || ch is >= '\u3000' and <= '\u303f' || ch is >= '\uff00' and <= '\uffef';
    }

    private static bool IsAsciiTextChar(char ch)
    {
        return ch is >= '0' and <= '9'
            || ch is >= 'A' and <= 'Z'
            || ch is >= 'a' and <= 'z';
    }

    private static bool IsCjkTextChar(char ch)
    {
        return ch is >= '\u3400' and <= '\u4dbf'
            || ch is >= '\u4e00' and <= '\u9fff'
            || ch is >= '\uf900' and <= '\ufaff';
    }

    private static bool IsSuspiciousDecodedChar(char ch)
    {
        if (ch == '\0') return true;
        if (char.IsControl(ch) && !char.IsWhiteSpace(ch)) return true;
        if (char.IsSurrogate(ch)) return true;
        if (ch is >= '\u0100' and <= '\u017f') return true;
        if (ch is >= '\u1100' and <= '\u11ff') return true;
        if (ch is >= '\uac00' and <= '\ud7af') return true;
        return ch is >= '\ue000' and <= '\uf8ff';
    }

    private static IReadOnlyList<string> BuildLayoutHints(byte[] data, IReadOnlyList<TableStringCandidateDto> strings)
    {
        var hints = new List<string>
        {
            $"文件大小：{data.Length} bytes"
        };
        foreach (var width in new[] { 8, 12, 16, 24, 32, 40, 48, 64 })
        {
            if (data.Length >= width * 2 && data.Length % width == 0)
            {
                hints.Add($"可能行宽：{width} bytes x {data.Length / width} 行");
                break;
            }
        }

        if (strings.Count > 0)
        {
            var counts = strings
                .GroupBy(item => item.Encoding)
                .Select(group => $"{group.Key.ToUpperInvariant()} {group.Count()} 个");
            hints.Add($"发现字符串候选：{string.Join(" / ", counts)}");
        }
        else if (data.Length >= 4)
        {
            hints.Add($"二进制字段概览：按 4-byte word 显示前 {Math.Min(data.Length / 4, 50)} 项");
        }

        return hints;
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        return (uint)(data[offset]
            | (data[offset + 1] << 8)
            | (data[offset + 2] << 16)
            | (data[offset + 3] << 24));
    }

    private static ushort ReadUInt16(byte[] data, int offset)
    {
        return (ushort)(data[offset] | (data[offset + 1] << 8));
    }

    private static ulong ReadUInt64(byte[] data, int offset)
    {
        ulong value = 0;
        for (var index = 0; index < 8; index++)
        {
            value |= (ulong)data[offset + index] << (index * 8);
        }

        return value;
    }

    private static float ReadSingle(byte[] data, int offset)
    {
        return BitConverter.ToSingle(data, offset);
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value & 0xff);
        data[offset + 1] = (byte)((value >> 8) & 0xff);
        data[offset + 2] = (byte)((value >> 16) & 0xff);
        data[offset + 3] = (byte)((value >> 24) & 0xff);
    }

    private static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)(value & 0xff);
        data[offset + 1] = (byte)((value >> 8) & 0xff);
    }

    private static void WriteUInt64(byte[] data, int offset, ulong value)
    {
        for (var index = 0; index < 8; index++)
        {
            data[offset + index] = (byte)((value >> (index * 8)) & 0xff);
        }
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value & 0xff));
        stream.WriteByte((byte)((value >> 8) & 0xff));
        stream.WriteByte((byte)((value >> 16) & 0xff));
        stream.WriteByte((byte)((value >> 24) & 0xff));
    }

    private static void WriteInt32(Stream stream, int value)
    {
        WriteUInt32(stream, unchecked((uint)value));
    }

    private static void WriteSingle(byte[] data, int offset, float value)
    {
        BitConverter.GetBytes(value).CopyTo(data, offset);
    }

    private static void WriteAscii(byte[] data, int offset, int length, string value)
    {
        if (length <= 0)
        {
            throw new InvalidOperationException("ASCII 字段长度必须大于 0。");
        }

        if (value.Any(ch => ch > 127))
        {
            throw new InvalidOperationException("ASCII 字段只能写入英文/数字/符号，不能写入中文。");
        }

        var bytes = Encoding.ASCII.GetBytes(value);
        if (bytes.Length > length)
        {
            throw new InvalidOperationException($"字段内容超过固定长度：{length} bytes。");
        }

        Array.Clear(data, offset, length);
        bytes.CopyTo(data, offset);
    }

    private static void WriteUtf16ZeroTerminated(Stream stream, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
        stream.WriteByte(0);
    }

    private static void WriteUtf8Z(byte[] data, int offset, int length, string value)
    {
        if (length <= 0)
        {
            throw new InvalidOperationException("UTF-8 字段长度必须大于 0。");
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length >= length)
        {
            throw new InvalidOperationException($"字段内容超过固定长度：{length - 1} bytes。");
        }

        Array.Clear(data, offset, length);
        bytes.CopyTo(data, offset);
    }

    private static void WriteUtf16Z(byte[] data, int offset, int length, string value)
    {
        if (length <= 1)
        {
            throw new InvalidOperationException("UTF-16 字段长度必须至少 2 bytes。");
        }

        if (length % 2 != 0)
        {
            throw new InvalidOperationException("UTF-16 字段长度必须是偶数字节。");
        }

        var bytes = Encoding.Unicode.GetBytes(value);
        if (bytes.Length > length - 2)
        {
            throw new InvalidOperationException($"字段内容超过固定长度：{length - 2} bytes。");
        }

        Array.Clear(data, offset, length);
        bytes.CopyTo(data, offset);
    }

    private sealed record BinaryInspection(
        IReadOnlyDictionary<string, string> HeaderFields,
        IReadOnlyList<TableStringCandidateDto> Strings,
        IReadOnlyList<string> LayoutHints);

    private sealed record TextEditResult(string Text, string EncodingName);

    private readonly record struct Datc64Sections(
        int RowCount,
        int RowLength,
        byte[] Fixed,
        byte[] Variable)
    {
        public int FixedLength => Fixed.Length;
        public int VariableLength => Variable.Length;
    }

    private sealed record LegacyDatRow(IReadOnlyList<string> Values);

    private sealed record CatalogColumn(
        string Name,
        string DisplayName,
        string Type,
        bool Array,
        int Offset,
        int ByteLength);

    public sealed record Datc64EditResult(
        byte[] Data,
        int Applied,
        IReadOnlyList<string> Skipped);
}

internal static class DatSchemaCatalog
{
    private const string SchemaEnvironmentVariable = "POE_STUDIO_DAT_SCHEMA_PATH";
    private const string EmbeddedSchemaResourceName = "PoeStudio.Core.Tables.Schemas.schema.min.json";
    private static readonly Lazy<IReadOnlyDictionary<string, Table>> Tables = new(LoadTables);

    public static Table? TryGet(string tableName)
    {
        return Tables.Value.TryGetValue(tableName, out var table)
            ? table
            : Tables.Value.TryGetValue(tableName.ToLowerInvariant(), out table)
                ? table
                : null;
    }

    private static IReadOnlyDictionary<string, Table> LoadTables()
    {
        try
        {
            using var stream = OpenSchemaStream();
            if (stream is null)
            {
                return new Dictionary<string, Table>(StringComparer.OrdinalIgnoreCase);
            }

            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("tables", out var tablesElement)
                || tablesElement.ValueKind != JsonValueKind.Array)
            {
                return new Dictionary<string, Table>(StringComparer.OrdinalIgnoreCase);
            }

            var result = new Dictionary<string, Table>(StringComparer.OrdinalIgnoreCase);
            foreach (var tableElement in tablesElement.EnumerateArray())
            {
                if (!tableElement.TryGetProperty("name", out var nameElement))
                {
                    continue;
                }

                var name = nameElement.GetString();
                if (string.IsNullOrWhiteSpace(name)
                    || !tableElement.TryGetProperty("columns", out var columnsElement)
                    || columnsElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var columns = new List<Column>();
                foreach (var columnElement in columnsElement.EnumerateArray())
                {
                    if (!columnElement.TryGetProperty("type", out var typeElement))
                    {
                        continue;
                    }

                    var type = typeElement.GetString();
                    if (string.IsNullOrWhiteSpace(type))
                    {
                        continue;
                    }

                    string? columnName = null;
                    if (columnElement.TryGetProperty("name", out var columnNameElement)
                        && columnNameElement.ValueKind == JsonValueKind.String)
                    {
                        columnName = columnNameElement.GetString();
                    }

                    var array = columnElement.TryGetProperty("array", out var arrayElement)
                        && arrayElement.ValueKind == JsonValueKind.True;
                    columns.Add(new Column(columnName, type, array));
                }

                result[name] = new Table(name, columns);
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, Table>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Stream? OpenSchemaStream()
    {
        foreach (var path in CandidateSchemaPaths())
        {
            if (File.Exists(path))
            {
                return File.OpenRead(path);
            }
        }

        return Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedSchemaResourceName);
    }

    private static IEnumerable<string> CandidateSchemaPaths()
    {
        var configured = Environment.GetEnvironmentVariable(SchemaEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return configured;
        }

        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "schema.min.json");
        yield return Path.Combine(baseDirectory, "Tables", "Schemas", "schema.min.json");
        yield return Path.Combine(baseDirectory, "config", "schema.min.json");
    }

    internal sealed record Table(string Name, IReadOnlyList<Column> Columns);

    internal sealed record Column(string? Name, string Type, bool Array);
}

internal static class LegacyDatCatalog
{
    private static readonly IReadOnlyDictionary<string, Table> Tables = new Dictionary<string, Table>(StringComparer.OrdinalIgnoreCase)
    {
        ["Languages"] = new Table("Languages",
        [
            new Column("Index", "i32", "Index @0"),
            new Column("Id", "valuestring", "Id @4"),
            new Column("Text", "valuestring", "Text"),
            new Column("Tag1", "valuestring", "Tag1"),
            new Column("Tag2", "valuestring", "Tag2"),
            new Column("Unknown0", "i32", "Unknown0"),
            new Column("IsEnabled", "i32", "IsEnabled"),
            new Column("Unknown1", "i32", "Unknown1")
        ])
    };

    public static bool TryGet(string tableName, out Table table)
    {
        return Tables.TryGetValue(tableName, out table!);
    }

    internal sealed record Table(string Name, IReadOnlyList<Column> Columns);

    internal sealed record Column(string Name, string Type, string DisplayName);
}
