using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using PoeStudio.Contracts;

namespace PoeStudio.Storage.Agent;

public sealed class AgentRunTraceStore
{
    private static readonly Regex SafeId = new("^[a-f0-9]{32}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string root;

    public AgentRunTraceStore(string workspaceRoot)
    {
        root = Path.Combine(workspaceRoot, "agent", "runs");
    }

    public async Task AppendAsync(string runId, AgentRunTraceEventDto evt, CancellationToken cancellationToken)
    {
        if (!SafeId.IsMatch(runId))
        {
            throw new ArgumentException("Invalid run id.", nameof(runId));
        }

        Directory.CreateDirectory(root);
        var line = JsonSerializer.Serialize(evt, JsonOptions) + Environment.NewLine;
        var path = Path.Combine(root, runId + ".jsonl");
        await using var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);
        var bytes = Encoding.UTF8.GetBytes(line);
        await stream.WriteAsync(bytes, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentRunTraceEventDto>> ReadAsync(string runId, CancellationToken cancellationToken)
    {
        if (!SafeId.IsMatch(runId))
        {
            return [];
        }

        var path = Path.Combine(root, runId + ".jsonl");
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(cancellationToken);
        var events = new List<AgentRunTraceEventDto>();
        foreach (var line in text.Split(Environment.NewLine))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var evt = JsonSerializer.Deserialize<AgentRunTraceEventDto>(line, JsonOptions);
                if (evt is not null)
                {
                    events.Add(evt);
                }
            }
            catch (JsonException)
            {
                // A concurrent append may leave the final line temporarily incomplete.
            }
        }

        return events;
    }
}
