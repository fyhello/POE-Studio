using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PoeStudio.Contracts;

namespace PoeStudio.Core.Agent;

public sealed class AgentProjectContextService
{
    private const int SummaryMaxLength = 2500;
    private const int SectionMaxLength = 900;
    private const int GuidanceMaxLength = 900;
    private const string Version = "2026-05-23";

    private static readonly string[] SourcePaths =
    [
        Path.Combine("docs", "agent", "poe-studio-project-workflows.md"),
        Path.Combine("docs", "agent", "poe-studio-agent-context.md"),
        Path.Combine("docs", "ai-project-memory.md")
    ];

    private static readonly Regex HeadingRegex = new(@"^(#{1,4})\s+(.+?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly AgentRepositoryRootResolver _repositoryRootResolver;

    public AgentProjectContextService(AgentRepositoryRootResolver repositoryRootResolver)
    {
        _repositoryRootResolver = repositoryRootResolver;
    }

    public async Task<AgentProjectContextDto> BuildAsync(
        string taskKind,
        string goal,
        string? resourcePath,
        string? repositoryRootCandidate,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = _repositoryRootResolver.ResolveFromCandidates(repositoryRootCandidate) ??
                             _repositoryRootResolver.Resolve();
        var unknowns = new List<string>();

        if (repositoryRoot is null)
        {
            unknowns.Add("missing repository root");
        }

        var documents = new List<ProjectDocument>();
        var sources = new List<AgentProjectContextSourceDto>();

        foreach (var sourcePath in SourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = repositoryRoot is null ? null : Path.Combine(repositoryRoot, sourcePath);
            if (fullPath is null || !File.Exists(fullPath))
            {
                sources.Add(new AgentProjectContextSourceDto(NormalizePath(sourcePath), false, null, null));
                unknowns.Add($"missing {NormalizePath(sourcePath)}");
                continue;
            }

            var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            sources.Add(new AgentProjectContextSourceDto(
                NormalizePath(sourcePath),
                true,
                hash,
                File.GetLastWriteTimeUtc(fullPath)));
            documents.Add(new ProjectDocument(NormalizePath(sourcePath), content, ExtractSections(content)));
        }

        var selectedSections = SelectRelevantSections(documents, taskKind, goal, resourcePath);
        var relevantSections = selectedSections
            .Select(section => new AgentProjectContextSectionDto(
                section.Key,
                section.Title,
                Truncate(CollapseWhitespace(section.Content), SectionMaxLength)))
            .ToArray();
        var summary = BuildSummary(relevantSections, unknowns);

        return new AgentProjectContextDto(
            Version,
            sources,
            summary,
            relevantSections,
            BuildToolGuidance(),
            BuildRiskBoundaries(),
            unknowns);
    }

    private static IReadOnlyList<ProjectSection> SelectRelevantSections(
        IReadOnlyList<ProjectDocument> documents,
        string taskKind,
        string goal,
        string? resourcePath)
    {
        var query = $"{taskKind} {goal} {resourcePath}".ToLowerInvariant();
        var matches = new List<ProjectSection>();
        foreach (var document in documents)
        {
            foreach (var section in document.Sections)
            {
                var searchable = $"{section.Title} {section.Content}".ToLowerInvariant();
                if (ShouldIncludeSection(query, searchable))
                {
                    matches.Add(section);
                }
            }
        }

        if (matches.Count == 0)
        {
            matches.AddRange(documents.SelectMany(document => document.Sections).Take(4));
        }

        return matches
            .GroupBy(section => section.Key)
            .Select(group => group.First())
            .Take(8)
            .ToArray();
    }

    private static bool ShouldIncludeSection(string query, string section)
    {
        if (section.Contains("项目总览") || section.Contains("工作流") || section.Contains("风险") || section.Contains("审批"))
        {
            return true;
        }

        if (query.Contains("datc64") || query.Contains("翻译") || query.Contains("当前") || query.Contains(".datc64"))
        {
            return section.Contains("datc64") ||
                   section.Contains("当前工作态") ||
                   section.Contains("草稿") ||
                   section.Contains("overlay") ||
                   section.Contains("mcp") ||
                   section.Contains("审批");
        }

        if (query.Contains("资源") || query.Contains("索引") || query.Contains("search"))
        {
            return section.Contains("资源") || section.Contains("索引");
        }

        return false;
    }

    private static string BuildSummary(
        IReadOnlyList<AgentProjectContextSectionDto> relevantSections,
        IReadOnlyList<string> unknowns)
    {
        var builder = new StringBuilder();
        builder.Append("POE Studio project context loaded. ");
        foreach (var section in relevantSections)
        {
            builder.Append(section.Title).Append(": ").Append(section.Content).Append(' ');
        }

        if (unknowns.Count > 0)
        {
            builder.Append("Unknowns: ").Append(string.Join("; ", unknowns)).Append('.');
        }

        return Truncate(CollapseWhitespace(builder.ToString()), SummaryMaxLength);
    }

    private static IReadOnlyList<ProjectSection> ExtractSections(string content)
    {
        var matches = HeadingRegex.Matches(content);
        if (matches.Count == 0)
        {
            return [new ProjectSection("overview", "Overview", content)];
        }

        var sections = new List<ProjectSection>();
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var start = match.Index + match.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
            var title = match.Groups[2].Value.Trim();
            var body = content[start..end].Trim();
            sections.Add(new ProjectSection(CreateSectionKey(title), title, body));
        }

        return sections;
    }

    private static IReadOnlyList<AgentToolGuidanceDto> BuildToolGuidance()
    {
        return
        [
            Tool("poe_get_workspace", "Inspect workspace root and process context.", "Read-only; workspace root is not the repository root."),
            Tool("poe_list_profiles", "List available client profiles and display names.", "Read-only; does not infer source/target roles by itself."),
            Tool("poe_get_index_status", "Check whether a profile has an index and resource count.", "Read-only; does not build an index."),
            Tool("poe_search_resources", "Search indexed resources by query or path.", "Read-only; searches index only and does not scan disk."),
            Tool("poe_read_resource", "Read indexed resource bytes or summaries.", "Read-only; no useOverlay parameter, so it does not represent current working state."),
            Tool("poe_datc64_extract_translatable_cells", "Extract DATC64 or string-candidate cells for analysis.", "Read-only; no overlay-aware target current state and no write behavior."),
            Tool("poe_get_project_context", "Fetch summarized project workflow context on demand.", "Read-only; returns summaries and bounded sections, not full documents.")
        ];

        static AgentToolGuidanceDto Tool(string name, string useFor, string limitation)
        {
            return new AgentToolGuidanceDto(name, Truncate(useFor, GuidanceMaxLength / 2), Truncate(limitation, GuidanceMaxLength / 2));
        }
    }

    private static IReadOnlyList<AgentRiskBoundaryDto> BuildRiskBoundaries()
    {
        return
        [
            Risk("read resource", "low", false, "Reading workspace, profiles, index status, search results, or resource summaries is allowed as read-only analysis."),
            Risk("generate proposal", "medium", false, "Generating candidates, dry-run output, or analysis is allowed when evidence and unknowns are shown."),
            Risk("write overlay", "high", true, "Requires approval before writing overlay draft files or applying DATC64 proposals."),
            Risk("bulk write", "high", true, "Requires approval and explicit scope before batch overlay writes or migration applies."),
            Risk("build patch", "high", true, "Requires approval before build, install, uninstall, rollback, or sandbox-changing actions."),
            Risk("install rollback", "high", true, "Requires approval and visible backup/rollback evidence before touching client files.")
        ];

        static AgentRiskBoundaryDto Risk(string action, string level, bool approval, string rule)
        {
            return new AgentRiskBoundaryDto(action, level, approval, Truncate(rule, GuidanceMaxLength));
        }
    }

    private static string CreateSectionKey(string title)
    {
        var normalized = title.ToLowerInvariant();
        if (normalized.Contains("当前工作态") || normalized.Contains("草稿") || normalized.Contains("overlay"))
        {
            return "layering";
        }

        if (normalized.Contains("datc64") || normalized.Contains("表格"))
        {
            return "datc64";
        }

        if (normalized.Contains("mcp") || normalized.Contains("agent"))
        {
            return "mcp";
        }

        if (normalized.Contains("审批") || normalized.Contains("风险"))
        {
            return "approval";
        }

        if (normalized.Contains("索引"))
        {
            return "index";
        }

        if (normalized.Contains("资源"))
        {
            return "resource";
        }

        if (normalized.Contains("native") || normalized.Contains("ggpk") || normalized.Contains("oodle"))
        {
            return "native";
        }

        return "overview-" + Math.Abs(title.GetHashCode()).ToString("x");
    }

    private static string CollapseWhitespace(string value)
    {
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..Math.Max(0, maxLength - 14)].TrimEnd() + " [truncated]";
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private sealed record ProjectDocument(
        string Path,
        string Content,
        IReadOnlyList<ProjectSection> Sections);

    private sealed record ProjectSection(
        string Key,
        string Title,
        string Content);
}
