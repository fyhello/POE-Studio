using System.Text;
using System.Text.Json;
using PoeStudio.Contracts;

namespace PoeStudio.Storage.Agent;

public sealed class AgentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions JsonLineOptions = new(JsonSerializerDefaults.Web);

    private readonly string _workspaceRoot;

    public AgentStore(string workspaceRoot)
    {
        _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? throw new ArgumentException("workspaceRoot is required", nameof(workspaceRoot))
            : workspaceRoot;
    }

    public async Task SaveSettingsAsync(AgentSettingsDto settings, CancellationToken cancellationToken)
    {
        await WriteJsonAsync(SettingsPath, settings, cancellationToken);
    }

    public async Task<AgentSettingsDto?> GetSettingsAsync(CancellationToken cancellationToken)
    {
        return await ReadJsonAsync<AgentSettingsDto>(SettingsPath, cancellationToken);
    }

    public async Task SaveThreadAsync(AgentThreadDto thread, CancellationToken cancellationToken)
    {
        await WriteJsonAsync(ThreadPath(thread.Id), thread, cancellationToken);
    }

    public async Task<AgentThreadDto> SaveNewThreadAsync(
        string profileId,
        string title,
        string goal,
        string taskKind,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var thread = new AgentThreadDto(
            NewId("thread"),
            profileId,
            title,
            goal,
            taskKind,
            AgentThreadStatus.Active,
            now,
            now);
        await SaveThreadAsync(thread, cancellationToken);
        return thread;
    }

    public async Task<AgentThreadDto?> GetThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        return await ReadJsonAsync<AgentThreadDto>(ThreadPath(threadId), cancellationToken);
    }

    public async Task<AgentRunDto?> FindRunAsync(string runId, CancellationToken cancellationToken)
    {
        var threadsRoot = Path.Combine(AgentRoot, "threads");
        if (!Directory.Exists(threadsRoot))
        {
            return null;
        }

        foreach (var runPath in Directory.EnumerateFiles(threadsRoot, "run.json", SearchOption.AllDirectories))
        {
            var run = await ReadJsonAsync<AgentRunDto>(runPath, cancellationToken);
            if (run is not null && string.Equals(run.Id, runId, StringComparison.Ordinal))
            {
                return run;
            }
        }

        return null;
    }

    public async Task AppendMessageAsync(AgentMessageDto message, CancellationToken cancellationToken)
    {
        var path = MessagesPath(message.ThreadId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var line = JsonSerializer.Serialize(message, JsonLineOptions);
        await File.AppendAllTextAsync(path, line + Environment.NewLine, Encoding.UTF8, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentMessageDto>> ListMessagesAsync(string threadId, CancellationToken cancellationToken)
    {
        return await ReadJsonLinesAsync<AgentMessageDto>(MessagesPath(threadId), cancellationToken);
    }

    public async Task SaveRunAsync(AgentRunDto run, CancellationToken cancellationToken)
    {
        await WriteJsonAsync(RunPath(run.ThreadId, run.Id), run, cancellationToken);
    }

    public async Task<AgentRunDto?> GetRunAsync(string threadId, string runId, CancellationToken cancellationToken)
    {
        return await ReadJsonAsync<AgentRunDto>(RunPath(threadId, runId), cancellationToken);
    }

    public async Task<IReadOnlyList<AgentRunDto>> ListRunsAsync(string threadId, CancellationToken cancellationToken)
    {
        var runsRoot = Path.Combine(ThreadRoot(threadId), "runs");
        if (!Directory.Exists(runsRoot))
        {
            return [];
        }

        var runs = new List<AgentRunDto>();
        foreach (var path in Directory.EnumerateFiles(runsRoot, "run.json", SearchOption.AllDirectories))
        {
            var run = await ReadJsonAsync<AgentRunDto>(path, cancellationToken);
            if (run is not null)
            {
                runs.Add(run);
            }
        }

        return runs.OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public async Task SavePlanAsync(
        string threadId,
        string runId,
        IReadOnlyList<AgentPlanStepDto> steps,
        CancellationToken cancellationToken)
    {
        await WriteJsonAsync(PlanPath(threadId, runId), steps, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentPlanStepDto>> GetPlanAsync(
        string threadId,
        string runId,
        CancellationToken cancellationToken)
    {
        return await ReadJsonAsync<IReadOnlyList<AgentPlanStepDto>>(PlanPath(threadId, runId), cancellationToken)
            ?? [];
    }

    public async Task<AgentEventDto> AppendEventAsync(
        string threadId,
        string runId,
        AgentEventType type,
        string message,
        string? payloadJson,
        CancellationToken cancellationToken)
    {
        var path = EventsPath(threadId, runId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sequence = await GetNextEventSequenceAsync(path, cancellationToken);
        var evt = new AgentEventDto(
            NewId("event"),
            runId,
            sequence,
            type,
            message,
            payloadJson,
            DateTimeOffset.UtcNow);
        var line = JsonSerializer.Serialize(evt, JsonLineOptions);
        await File.AppendAllTextAsync(path, line + Environment.NewLine, Encoding.UTF8, cancellationToken);
        return evt;
    }

    public async Task<IReadOnlyList<AgentEventDto>> ListEventsAsync(
        string threadId,
        string runId,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        var events = await ReadJsonLinesAsync<AgentEventDto>(EventsPath(threadId, runId), cancellationToken);
        return events.Where(x => x.Sequence > afterSequence).ToArray();
    }

    public async Task SaveApprovalsAsync(
        string threadId,
        string runId,
        IReadOnlyList<AgentApprovalDto> approvals,
        CancellationToken cancellationToken)
    {
        await WriteJsonAsync(ApprovalsPath(threadId, runId), approvals, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentApprovalDto>> ListApprovalsAsync(
        string threadId,
        string runId,
        CancellationToken cancellationToken)
    {
        return await ReadJsonAsync<IReadOnlyList<AgentApprovalDto>>(ApprovalsPath(threadId, runId), cancellationToken)
            ?? [];
    }

    public async Task<(string ThreadId, string RunId, AgentApprovalDto Approval)?> FindApprovalAsync(
        string approvalId,
        CancellationToken cancellationToken)
    {
        var threadsRoot = Path.Combine(AgentRoot, "threads");
        if (!Directory.Exists(threadsRoot))
        {
            return null;
        }

        foreach (var approvalsPath in Directory.EnumerateFiles(threadsRoot, "approvals.json", SearchOption.AllDirectories))
        {
            var approvals = await ReadJsonAsync<IReadOnlyList<AgentApprovalDto>>(approvalsPath, cancellationToken) ?? [];
            var approval = approvals.FirstOrDefault(x => string.Equals(x.Id, approvalId, StringComparison.Ordinal));
            if (approval is null)
            {
                continue;
            }

            var runPath = Path.Combine(Path.GetDirectoryName(approvalsPath)!, "run.json");
            var run = await ReadJsonAsync<AgentRunDto>(runPath, cancellationToken);
            if (run is not null)
            {
                return (run.ThreadId, run.Id, approval);
            }
        }

        return null;
    }

    public async Task<bool> TryUpdateApprovalStatusAsync(
        string threadId,
        string runId,
        string approvalId,
        AgentApprovalStatus expectedStatus,
        AgentApprovalStatus nextStatus,
        string? appliedOverlayPath,
        CancellationToken cancellationToken)
    {
        var approvals = (await ListApprovalsAsync(threadId, runId, cancellationToken)).ToArray();
        var index = Array.FindIndex(approvals, x => string.Equals(x.Id, approvalId, StringComparison.Ordinal));
        if (index < 0 || approvals[index].Status != expectedStatus)
        {
            return false;
        }

        approvals[index] = approvals[index] with
        {
            Status = nextStatus,
            UpdatedAt = DateTimeOffset.UtcNow,
            AppliedOverlayPath = appliedOverlayPath ?? approvals[index].AppliedOverlayPath
        };
        await SaveApprovalsAsync(threadId, runId, approvals, cancellationToken);
        return true;
    }

    private string AgentRoot => Path.Combine(_workspaceRoot, "agent");

    private string SettingsPath => Path.Combine(AgentRoot, "settings.json");

    private string ThreadPath(string threadId) => Path.Combine(ThreadRoot(threadId), "thread.json");

    private string MessagesPath(string threadId) => Path.Combine(ThreadRoot(threadId), "messages.jsonl");

    private string RunPath(string threadId, string runId) => Path.Combine(RunRoot(threadId, runId), "run.json");

    private string PlanPath(string threadId, string runId) => Path.Combine(RunRoot(threadId, runId), "plan.json");

    private string EventsPath(string threadId, string runId) => Path.Combine(RunRoot(threadId, runId), "events.jsonl");

    private string ApprovalsPath(string threadId, string runId) => Path.Combine(RunRoot(threadId, runId), "approvals.json");

    private string ThreadRoot(string threadId) => Path.Combine(AgentRoot, "threads", SafeSegment(threadId));

    private string RunRoot(string threadId, string runId) => Path.Combine(ThreadRoot(threadId), "runs", SafeSegment(runId));

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static async Task<long> GetNextEventSequenceAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return 1;
        }

        long last = 0;
        await foreach (var line in File.ReadLinesAsync(path, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var evt = JsonSerializer.Deserialize<AgentEventDto>(line, JsonLineOptions);
            if (evt is not null)
            {
                last = Math.Max(last, evt.Sequence);
            }
        }

        return last + 1;
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(
            tempPath,
            JsonSerializer.Serialize(value, JsonOptions),
            Encoding.UTF8,
            cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task<IReadOnlyList<T>> ReadJsonLinesAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var items = new List<T>();
        await foreach (var line in File.ReadLinesAsync(path, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<T>(line, JsonLineOptions);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static string SafeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || value.Contains("..", StringComparison.Ordinal)
            || value.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || value.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("invalid_agent_id_segment", nameof(value));
        }

        return value;
    }
}
