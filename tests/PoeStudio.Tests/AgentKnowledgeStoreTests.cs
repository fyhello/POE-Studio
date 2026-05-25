using PoeStudio.Storage.Agent;

namespace PoeStudio.Tests;

public sealed class AgentKnowledgeStoreTests
{
    [Fact]
    public async Task ReadSectionsAsync_returns_registered_sections_only()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var store = new AgentKnowledgeStore(root);

        var result = await store.ReadSectionsAsync(["core.contract"], 12000, CancellationToken.None);

        Assert.Equal("0.1", result.Version);
        var section = Assert.Single(result.Sections);
        Assert.Equal("core.contract", section.SectionId);
        Assert.Contains("source", section.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.MissingSectionIds);
    }

    [Fact]
    public async Task ReadSectionsAsync_reports_missing_sections()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var store = new AgentKnowledgeStore(root);

        var result = await store.ReadSectionsAsync(["missing.section"], 12000, CancellationToken.None);

        Assert.Empty(result.Sections);
        Assert.Contains("missing.section", result.MissingSectionIds);
    }

    [Fact]
    public async Task ReadSectionsAsync_limits_total_bytes()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var store = new AgentKnowledgeStore(root);

        var result = await store.ReadSectionsAsync(["core.contract", "workflow.datc64-translation"], 1000, CancellationToken.None);

        Assert.True(result.TotalBytes <= 1000);
        Assert.Contains(result.Sections, section => section.Truncated);
    }
}
