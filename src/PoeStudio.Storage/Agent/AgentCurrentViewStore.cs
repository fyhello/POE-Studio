using System.Text.Json;
using System.Text.RegularExpressions;
using PoeStudio.Contracts;

namespace PoeStudio.Storage.Agent;

public sealed class AgentCurrentViewStore
{
    private static readonly Regex SafeIdPattern = new("^[a-f0-9]{32}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string root;

    public AgentCurrentViewStore(string workspaceRoot)
    {
        root = Path.Combine(workspaceRoot, "agent", "current-view");
    }

    public async Task<AgentCurrentViewSnapshotDto> SaveAsync(
        AgentCurrentViewRequestDto view,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        var contextId = Guid.NewGuid().ToString("N");
        var snapshot = new AgentCurrentViewSnapshotDto(contextId, DateTimeOffset.UtcNow, view);
        await File.WriteAllTextAsync(PathFor(contextId), JsonSerializer.Serialize(snapshot, JsonOptions), cancellationToken);
        return snapshot;
    }

    public async Task<AgentCurrentViewSnapshotDto?> LoadAsync(
        string? contextId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contextId) || !SafeIdPattern.IsMatch(contextId))
        {
            return null;
        }

        var path = PathFor(contextId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AgentCurrentViewSnapshotDto>(stream, JsonOptions, cancellationToken);
    }

    private string PathFor(string contextId)
    {
        return Path.Combine(root, contextId + ".json");
    }
}
