using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;

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

    [Fact]
    public async Task BuildAsync_reads_project_documents_and_records_source_metadata()
    {
        using var repo = CreateRepositoryRoot(
            workflowContent:
            """
            # Workflow

            ## 7. 原始层、草稿层与当前工作态
            overlay current working state MCP 当前读取层限制 No useOverlay parameter.

            ## 9. DATC64 表格工作流样例
            DATC64 translation should confirm target current working state.
            """,
            agentContextContent:
            """
            # Agent Context

            ## 5. 原始层、草稿层和当前工作态
            MCP reads base resource and cannot represent current working state.
            """,
            memoryContent:
            """
            # Memory

            ## Overlay 模型
            Overlay draft requires approval before writing.
            """);
        var service = new AgentProjectContextService(new AgentRepositoryRootResolver());

        var context = await service.BuildAsync(
            "datc64-translation",
            "继续翻译当前表",
            "data/balance/traditional chinese/activeskills.datc64",
            repo.Root,
            CancellationToken.None);

        Assert.All(context.Sources, source => Assert.True(source.Exists));
        Assert.All(context.Sources, source => Assert.False(string.IsNullOrWhiteSpace(source.Hash)));
        Assert.All(context.Sources, source => Assert.NotNull(source.LastModifiedAt));
        Assert.Contains("overlay", context.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("current working state", context.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MCP", context.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.True(context.Summary.Length <= 2500);
        Assert.All(context.RelevantSections, section => Assert.True(section.Content.Length <= 900));
    }

    [Fact]
    public async Task BuildAsync_returns_unknowns_when_documents_are_missing()
    {
        using var repo = CreateRepositoryRoot(workflowContent: "# Workflow");
        File.Delete(Path.Combine(repo.Root, "docs", "agent", "poe-studio-agent-context.md"));
        File.Delete(Path.Combine(repo.Root, "docs", "ai-project-memory.md"));
        var service = new AgentProjectContextService(new AgentRepositoryRootResolver());

        var context = await service.BuildAsync("question", "Explain workspace", null, repo.Root, CancellationToken.None);

        Assert.Contains(context.Sources, source => source.Exists);
        Assert.Contains(context.Sources, source => !source.Exists);
        Assert.Contains(context.Unknowns, unknown => unknown.Contains("missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_enforces_summary_section_and_full_document_budgets()
    {
        var longSection = new string('A', 8_000);
        using var repo = CreateRepositoryRoot(
            workflowContent:
            $"""
            # Workflow

            ## 7. 原始层、草稿层与当前工作态
            overlay current working state MCP approval {longSection}
            """);
        var service = new AgentProjectContextService(new AgentRepositoryRootResolver());

        var context = await service.BuildAsync(
            "datc64-translation",
            "继续翻译当前表",
            "metadata/items.datc64",
            repo.Root,
            CancellationToken.None);
        var serialized = JsonSerializer.Serialize(context, JsonOptions);
        var original = await File.ReadAllTextAsync(
            Path.Combine(repo.Root, "docs", "agent", "poe-studio-project-workflows.md"),
            CancellationToken.None);

        Assert.True(context.Summary.Length <= 2500);
        Assert.All(context.RelevantSections, section => Assert.True(section.Content.Length <= 900));
        Assert.DoesNotContain(original, serialized);
    }

    [Fact]
    public async Task BuildAsync_includes_default_tool_guidance_and_risk_boundaries()
    {
        using var repo = CreateRepositoryRoot();
        var service = new AgentProjectContextService(new AgentRepositoryRootResolver());

        var context = await service.BuildAsync(
            "datc64-translation",
            "Translate DATC64",
            "metadata/items.datc64",
            repo.Root,
            CancellationToken.None);

        Assert.Contains(context.ToolGuidance, tool => tool.ToolName == "poe_get_workspace");
        Assert.Contains(context.ToolGuidance, tool => tool.ToolName == "poe_get_project_context");
        Assert.Contains(context.RiskBoundaries, risk => risk.Action.Contains("overlay", StringComparison.OrdinalIgnoreCase) && risk.RequiresApproval);
        Assert.All(context.ToolGuidance, tool => Assert.True((tool.UseFor.Length + tool.Limitation.Length) <= 900));
        Assert.All(context.RiskBoundaries, risk => Assert.True(risk.Rule.Length <= 900));
    }

    private static TemporaryDirectory CreateRepositoryRoot(
        string workflowContent = "# Workflow\n\n## 3. 项目总览\nProject overview.",
        string agentContextContent = "# Agent Context\n\n## 1. Agent 总目标\nAgent project context.",
        string memoryContent = "# Memory\n\n## 项目定位\nPOE Studio project memory.")
    {
        var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Root, "PoeStudio.sln"), string.Empty);
        var agentDocs = Directory.CreateDirectory(Path.Combine(directory.Root, "docs", "agent"));
        File.WriteAllText(Path.Combine(agentDocs.FullName, "poe-studio-project-workflows.md"), workflowContent);
        File.WriteAllText(Path.Combine(agentDocs.FullName, "poe-studio-agent-context.md"), agentContextContent);
        File.WriteAllText(Path.Combine(directory.Root, "docs", "ai-project-memory.md"), memoryContent);
        return directory;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "poe-studio-project-context-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
