using System.Text;
using PoeStudio.Contracts;

namespace PoeStudio.Core.Translation;

public static class TranslationCsv
{
    private sealed record GlossaryTerm(string Source, string Target);

    public static string Write(IReadOnlyList<TranslationEntryDto> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("virtualPath,sourceText,targetText,status");
        foreach (var entry in entries)
        {
            builder
                .Append(Escape(entry.VirtualPath)).Append(',')
                .Append(Escape(entry.SourceText)).Append(',')
                .Append(Escape(entry.TargetText)).Append(',')
                .Append(Escape(entry.Status)).AppendLine();
        }

        return builder.ToString();
    }

    public static IReadOnlyList<TranslationEntryDto> Read(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return [];
        }

        var rows = ParseRows(csv);
        if (rows.Count == 0)
        {
            return [];
        }

        var start = IsHeader(rows[0]) ? 1 : 0;
        var entries = new List<TranslationEntryDto>();
        for (var i = start; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count < 2 || string.IsNullOrWhiteSpace(row[0]))
            {
                continue;
            }

            entries.Add(new TranslationEntryDto(
                row[0],
                row.ElementAtOrDefault(1) ?? string.Empty,
                row.ElementAtOrDefault(2) ?? string.Empty,
                row.ElementAtOrDefault(3) ?? string.Empty));
        }

        return entries;
    }

    public static TranslationApplyGlossaryResponse ApplyGlossary(TranslationApplyGlossaryRequest request)
    {
        var entries = Read(request.Csv);
        var terms = ReadGlossary(request.Glossary);
        var warnings = new List<string>();
        if (terms.Count == 0)
        {
            warnings.Add("术语表为空。");
        }

        var changed = 0;
        var output = new List<TranslationEntryDto>(entries.Count);
        foreach (var entry in entries)
        {
            var target = string.IsNullOrEmpty(entry.TargetText) ? entry.SourceText : entry.TargetText;
            foreach (var term in terms)
            {
                target = target.Replace(term.Source, term.Target, StringComparison.Ordinal);
            }

            var status = string.Equals(target, entry.SourceText, StringComparison.Ordinal)
                ? entry.Status
                : "glossary";
            if (!string.Equals(target, entry.TargetText, StringComparison.Ordinal))
            {
                changed++;
            }

            output.Add(entry with { TargetText = target, Status = status });
        }

        return new TranslationApplyGlossaryResponse(
            request.ProfileId,
            entries.Count,
            terms.Count,
            changed,
            Write(output),
            warnings);
    }

    private static IReadOnlyList<GlossaryTerm> ReadGlossary(string glossary)
    {
        if (string.IsNullOrWhiteSpace(glossary))
        {
            return [];
        }

        return ParseRows(glossary)
            .Where(row => row.Count >= 2 && !string.IsNullOrWhiteSpace(row[0]))
            .Select(row => new GlossaryTerm(row[0], row[1]))
            .Where(term => !string.IsNullOrEmpty(term.Source))
            .DistinctBy(term => term.Source)
            .ToArray();
    }

    private static bool IsHeader(IReadOnlyList<string> row)
    {
        return row.Count >= 4
            && string.Equals(row[0], "virtualPath", StringComparison.OrdinalIgnoreCase)
            && string.Equals(row[1], "sourceText", StringComparison.OrdinalIgnoreCase);
    }

    private static string Escape(string value)
    {
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static List<List<string>> ParseRows(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                    continue;
                }

                if (ch == '"')
                {
                    quoted = false;
                    continue;
                }

                field.Append(ch);
                continue;
            }

            if (ch == '"')
            {
                quoted = true;
                continue;
            }

            if (ch == ',')
            {
                row.Add(field.ToString());
                field.Clear();
                continue;
            }

            if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                if (row.Any(value => value.Length > 0))
                {
                    rows.Add(row);
                }

                row = [];
                continue;
            }

            field.Append(ch);
        }

        row.Add(field.ToString());
        if (row.Any(value => value.Length > 0))
        {
            rows.Add(row);
        }

        return rows;
    }
}
