using System.Text.Json;
using System.Text.RegularExpressions;

namespace PoeStudio.Core.Agent;

public sealed partial class Datc64TranslationDraftParser
{
    public Datc64TranslationDraftProposal Parse(string finalMessage, string expectedProfileId, string expectedResourcePath)
    {
        var json = ExtractJson(finalMessage);
        var proposal = JsonSerializer.Deserialize<Datc64TranslationDraftProposal>(json, JsonOptions)
            ?? throw new ArgumentException("proposal_required");
        if (!string.Equals(proposal.ProfileId, expectedProfileId, StringComparison.Ordinal))
        {
            throw new ArgumentException("profile_mismatch");
        }

        if (!string.Equals(proposal.ResourcePath, expectedResourcePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("resource_mismatch");
        }

        foreach (var candidate in proposal.Candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Locator))
            {
                throw new ArgumentException("locator_required");
            }

            if (string.IsNullOrWhiteSpace(candidate.TranslatedText))
            {
                throw new ArgumentException("translated_text_required");
            }
        }

        return proposal;
    }

    private static string ExtractJson(string finalMessage)
    {
        var match = JsonFenceRegex().Match(finalMessage);
        if (!match.Success)
        {
            throw new ArgumentException("json_fenced_block_required");
        }

        return match.Groups["json"].Value;
    }

    [GeneratedRegex("```json\\s*(?<json>.*?)\\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex JsonFenceRegex();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

public sealed record Datc64TranslationDraftProposal(
    string TaskKind,
    string ProfileId,
    string ResourcePath,
    IReadOnlyList<Datc64TranslationCandidate> Candidates);

public sealed record Datc64TranslationCandidate(
    string Locator,
    int RowIndex,
    int ColumnIndex,
    string SourceText,
    string TranslatedText,
    double Confidence,
    string? Notes);
