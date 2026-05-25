using System.Text;
using System.Text.Json;
using PoeStudio.Contracts;

namespace PoeStudio.Storage.Agent;

public sealed class AgentKnowledgeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string repoRoot;
    private readonly string indexPath;

    public AgentKnowledgeStore(string repoRoot)
    {
        this.repoRoot = Path.GetFullPath(repoRoot);
        indexPath = Path.Combine(this.repoRoot, "docs", "agent", "knowledge", "index.json");
    }

    public async Task<AgentKnowledgeIndexDto> ReadIndexAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(indexPath);
        var index = await JsonSerializer.DeserializeAsync<AgentKnowledgeIndexDto>(
            stream,
            JsonOptions,
            cancellationToken);

        return index ?? throw new InvalidOperationException("Agent knowledge index is empty.");
    }

    public async Task<AgentKnowledgeReadResultDto> ReadSectionsAsync(
        IReadOnlyList<string> sectionIds,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        maxBytes = Math.Clamp(maxBytes, 1000, 24000);
        var index = await ReadIndexAsync(cancellationToken);
        var requested = sectionIds
            .Take(5)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var byId = index.Sections.ToDictionary(section => section.SectionId, StringComparer.Ordinal);
        var sections = new List<AgentKnowledgeSectionDto>();
        var missing = new List<string>();
        var totalBytes = 0;

        foreach (var sectionId in requested)
        {
            if (!byId.TryGetValue(sectionId, out var entry))
            {
                missing.Add(sectionId);
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(repoRoot, entry.File));
            if (!IsSameOrChildPath(repoRoot, fullPath) || !File.Exists(fullPath))
            {
                missing.Add(sectionId);
                continue;
            }

            var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var bytes = Encoding.UTF8.GetByteCount(content);
            var remaining = maxBytes - totalBytes;
            if (remaining <= 0)
            {
                sections.Add(ToSection(entry, string.Empty, truncated: true));
                continue;
            }

            var truncated = bytes > remaining;
            if (truncated)
            {
                content = TrimToUtf8Bytes(content, remaining);
                bytes = Encoding.UTF8.GetByteCount(content);
            }

            totalBytes += bytes;
            sections.Add(ToSection(entry, content, truncated));
        }

        return new AgentKnowledgeReadResultDto(index.Version, requested, sections, missing, totalBytes);
    }

    private static AgentKnowledgeSectionDto ToSection(AgentKnowledgeSectionIndexDto entry, string content, bool truncated)
    {
        return new AgentKnowledgeSectionDto(entry.SectionId, entry.Title, entry.Summary, entry.File, content, truncated);
    }

    private static string TrimToUtf8Bytes(string value, int maxBytes)
    {
        if (maxBytes <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var bytes = 0;
        foreach (var ch in value)
        {
            var charBytes = Encoding.UTF8.GetByteCount([ch]);
            if (bytes + charBytes > maxBytes)
            {
                break;
            }

            builder.Append(ch);
            bytes += charBytes;
        }

        return builder.ToString();
    }

    private static bool IsSameOrChildPath(string rootFullPath, string candidateFullPath)
    {
        var relative = Path.GetRelativePath(rootFullPath, candidateFullPath);
        return relative == "."
            || (!relative.StartsWith("..", StringComparison.Ordinal)
                && !Path.IsPathRooted(relative));
    }
}
