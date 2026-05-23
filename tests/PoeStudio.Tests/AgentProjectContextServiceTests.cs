using System.Text.Json;
using PoeStudio.Contracts;

namespace PoeStudio.Tests;

public sealed class AgentProjectContextServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AgentProjectContextDto_serializes_project_context_contract_fields()
    {
        var now = DateTimeOffset.Parse("2026-05-23T00:00:00Z");
        var context = new AgentProjectContextDto(
            "2026-05-23",
            [
                new AgentProjectContextSourceDto(
                    "docs/agent/poe-studio-project-workflows.md",
                    true,
                    "hash",
                    now)
            ],
            "Project workflow summary",
            [
                new AgentProjectContextSectionDto("overview", "Overview", "Read before acting.")
            ],
            [
                new AgentToolGuidanceDto("poe_read_resource", "Read indexed resources", "No useOverlay parameter.")
            ],
            [
                new AgentRiskBoundaryDto("write overlay", "high", true, "Requires approval.")
            ],
            ["missing docs/ai-project-memory.md"]);

        var json = JsonSerializer.Serialize(context, JsonOptions);

        Assert.Contains("\"version\"", json);
        Assert.Contains("\"sources\"", json);
        Assert.Contains("\"summary\"", json);
        Assert.Contains("\"relevantSections\"", json);
        Assert.Contains("\"toolGuidance\"", json);
        Assert.Contains("\"riskBoundaries\"", json);
        Assert.Contains("\"unknowns\"", json);
    }
}
