using PoeStudio.Core.Agent;

namespace PoeStudio.Tests;

public sealed class Datc64TranslationDraftParserTests
{
    private readonly Datc64TranslationDraftParser _parser = new();

    [Fact]
    public void Parse_extracts_proposal_from_json_fenced_block()
    {
        var proposal = _parser.Parse(
            """
            Final result:
            ```json
            {
              "taskKind": "datc64-translation",
              "profileId": "profile-1",
              "resourcePath": "metadata/example.datc64",
              "candidates": [
                {
                  "locator": "row:1;column:3;name:text_3 @12",
                  "rowIndex": 0,
                  "columnIndex": 3,
                  "sourceText": "NoMana",
                  "translatedText": "法力不足",
                  "confidence": 0.86,
                  "notes": "game UI prompt text"
                }
              ]
            }
            ```
            """,
            "profile-1",
            "metadata/example.datc64");

        Assert.Equal("profile-1", proposal.ProfileId);
        Assert.Equal("metadata/example.datc64", proposal.ResourcePath);
        var candidate = Assert.Single(proposal.Candidates);
        Assert.Equal(0, candidate.RowIndex);
        Assert.Equal(3, candidate.ColumnIndex);
        Assert.Equal("法力不足", candidate.TranslatedText);
    }

    [Fact]
    public void Parse_rejects_missing_locator()
    {
        var error = Assert.Throws<ArgumentException>(() => _parser.Parse(
            ProposalJson("""{ "translatedText": "法力不足", "rowIndex": 0, "columnIndex": 3, "sourceText": "NoMana" }"""),
            "profile-1",
            "metadata/example.datc64"));

        Assert.Equal("locator_required", error.Message);
    }

    [Fact]
    public void Parse_rejects_empty_translated_text()
    {
        var error = Assert.Throws<ArgumentException>(() => _parser.Parse(
            ProposalJson("""{ "locator": "row:1", "translatedText": "", "rowIndex": 0, "columnIndex": 3, "sourceText": "NoMana" }"""),
            "profile-1",
            "metadata/example.datc64"));

        Assert.Equal("translated_text_required", error.Message);
    }

    [Theory]
    [InlineData("other-profile", "metadata/example.datc64", "profile_mismatch")]
    [InlineData("profile-1", "metadata/other.datc64", "resource_mismatch")]
    public void Parse_rejects_profile_or_resource_mismatch(string profileId, string resourcePath, string expected)
    {
        var error = Assert.Throws<ArgumentException>(() => _parser.Parse(
            ProposalJson("""{ "locator": "row:1", "translatedText": "法力不足", "rowIndex": 0, "columnIndex": 3, "sourceText": "NoMana" }""", profileId, resourcePath),
            "profile-1",
            "metadata/example.datc64"));

        Assert.Equal(expected, error.Message);
    }

    private static string ProposalJson(string candidateJson, string profileId = "profile-1", string resourcePath = "metadata/example.datc64")
    {
        return $$"""
            ```json
            {
              "taskKind": "datc64-translation",
              "profileId": "{{profileId}}",
              "resourcePath": "{{resourcePath}}",
              "candidates": [{{candidateJson}}]
            }
            ```
            """;
    }
}
