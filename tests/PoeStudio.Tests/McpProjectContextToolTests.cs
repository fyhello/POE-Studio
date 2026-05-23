using System.Text.Json;
using PoeStudio.Mcp;

namespace PoeStudio.Tests;

public sealed class McpProjectContextToolTests
{
    [Fact]
    public async Task Get_project_context_returns_summary_sources_tool_guidance_and_risk_boundaries()
    {
        using var repository = CreateRepositoryRoot(
            workflowContent:
            """
            # Workflow

            ## 7. 原始层、草稿层与当前工作态
            current working state overlay MCP approval No useOverlay parameter.
            """);
        var registry = McpToolRegistry.CreateDefault();

        var result = await registry.CallToolAsync(
            "poe_get_project_context",
            JsonSerializer.SerializeToElement(new
            {
                taskKind = "datc64-translation",
                goal = "继续翻译当前表",
                resourcePath = "metadata/items.datc64",
                repositoryRoot = repository.Root
            }),
            CancellationToken.None);
        using var payload = ParsePayload(result);

        Assert.False(result.IsError);
        Assert.True(payload.RootElement.TryGetProperty("summary", out _));
        Assert.True(payload.RootElement.TryGetProperty("sources", out _));
        Assert.True(payload.RootElement.TryGetProperty("toolGuidance", out _));
        Assert.True(payload.RootElement.TryGetProperty("riskBoundaries", out _));
        Assert.Contains("current working state", payload.RootElement.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_project_context_missing_documents_returns_success_with_unknowns()
    {
        using var repository = CreateRepositoryRoot(workflowContent: "# Workflow");
        File.Delete(Path.Combine(repository.Root, "docs", "agent", "poe-studio-agent-context.md"));
        File.Delete(Path.Combine(repository.Root, "docs", "ai-project-memory.md"));
        var registry = McpToolRegistry.CreateDefault();

        var result = await registry.CallToolAsync(
            "poe_get_project_context",
            JsonSerializer.SerializeToElement(new
            {
                taskKind = "question",
                goal = "Explain project",
                repositoryRoot = repository.Root
            }),
            CancellationToken.None);
        using var payload = ParsePayload(result);

        Assert.False(result.IsError);
        var unknowns = payload.RootElement.GetProperty("unknowns").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains(unknowns, unknown => unknown!.Contains("missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Get_project_context_returns_bounded_content_without_full_document()
    {
        var longSection = new string('x', 8_000);
        using var repository = CreateRepositoryRoot(
            workflowContent:
            $"""
            # Workflow

            ## 7. 原始层、草稿层与当前工作态
            current working state overlay mcp approval {longSection}
            """);
        var registry = McpToolRegistry.CreateDefault();

        var result = await registry.CallToolAsync(
            "poe_get_project_context",
            JsonSerializer.SerializeToElement(new
            {
                taskKind = "datc64-translation",
                goal = "继续翻译当前表",
                resourcePath = "metadata/items.datc64",
                repositoryRoot = repository.Root
            }),
            CancellationToken.None);
        var responseText = result.Content.Single().Text;
        using var payload = JsonDocument.Parse(responseText);
        var original = await File.ReadAllTextAsync(Path.Combine(repository.Root, "docs", "agent", "poe-studio-project-workflows.md"));

        Assert.False(result.IsError);
        Assert.True(payload.RootElement.GetProperty("summary").GetString()!.Length <= 2500);
        foreach (var section in payload.RootElement.GetProperty("relevantSections").EnumerateArray())
        {
            Assert.True(section.GetProperty("content").GetString()!.Length <= 900);
        }

        Assert.DoesNotContain(original, responseText);
    }

    private static JsonDocument ParsePayload(McpToolResult result)
    {
        return JsonDocument.Parse(result.Content.Single().Text);
    }

    private static TemporaryDirectory CreateRepositoryRoot(
        string workflowContent = "# Workflow\n\n## 3. 项目总览\nProject overview.",
        string agentContextContent = "# Agent Context\n\n## 1. Agent 总目标\nAgent context.",
        string memoryContent = "# Memory\n\n## 项目定位\nProject memory.")
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
            Root = Path.Combine(Path.GetTempPath(), "poe-studio-mcp-project-context-tests", Guid.NewGuid().ToString("N"));
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
