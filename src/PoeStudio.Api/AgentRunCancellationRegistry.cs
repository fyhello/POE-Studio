using System.Collections.Concurrent;

namespace PoeStudio.Api;

public sealed class AgentRunCancellationRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runs = new(StringComparer.Ordinal);

    public CancellationToken Register(string runId)
    {
        var cts = new CancellationTokenSource();
        _runs[runId] = cts;
        return cts.Token;
    }

    public bool Cancel(string runId)
    {
        if (!_runs.TryGetValue(runId, out var cts))
        {
            return false;
        }

        cts.Cancel();
        return true;
    }

    public void Complete(string runId)
    {
        if (_runs.TryRemove(runId, out var cts))
        {
            cts.Dispose();
        }
    }
}
