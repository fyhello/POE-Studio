using PoeStudio.Contracts;
using PoeStudio.Storage.Agent;

namespace PoeStudio.Tests;

public sealed class AgentRunTraceStoreTests
{
    [Fact]
    public async Task Append_and_read_run_events_in_order()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var store = new AgentRunTraceStore(root);
        var runId = Guid.NewGuid().ToString("N");

        await store.AppendAsync(runId, new AgentRunTraceEventDto("message", "started", "{}", DateTimeOffset.UtcNow), CancellationToken.None);
        await store.AppendAsync(runId, new AgentRunTraceEventDto("tool_call", "completed", "{\"tool\":\"poe_get_workspace\"}", DateTimeOffset.UtcNow), CancellationToken.None);

        var events = await store.ReadAsync(runId, CancellationToken.None);

        Assert.Equal(["message", "tool_call"], events.Select(x => x.EventName).ToArray());
    }

    [Fact]
    public async Task Append_and_read_same_run_can_overlap_without_io_errors()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var store = new AgentRunTraceStore(root);
        var runId = Guid.NewGuid().ToString("N");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var errors = new List<Exception>();

        var appendTask = Task.Run(async () =>
        {
            for (var i = 0; i < 200; i++)
            {
                await store.AppendAsync(
                    runId,
                    new AgentRunTraceEventDto("codex_event", "observed", $"{{\"index\":{i}}}", DateTimeOffset.UtcNow),
                    cts.Token);
                await Task.Delay(1, cts.Token);
            }
        }, cts.Token);

        var readTask = Task.Run(async () =>
        {
            while (!appendTask.IsCompleted)
            {
                try
                {
                    _ = await store.ReadAsync(runId, cts.Token);
                }
                catch (IOException ex)
                {
                    errors.Add(ex);
                }

                await Task.Delay(1, cts.Token);
            }
        }, cts.Token);

        await appendTask;
        await readTask;

        Assert.Empty(errors);
        var events = await store.ReadAsync(runId, CancellationToken.None);
        Assert.Equal(200, events.Count);
    }
}
