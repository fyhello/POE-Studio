using Microsoft.Extensions.Configuration;
using PoeStudio.Api;
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;
using PoeStudio.Storage.Agent;

namespace PoeStudio.Tests;

public sealed class AgentTaskFrameTraceTests
{
    [Fact]
    public async Task ChatService_records_task_frame_events_in_run_trace()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-task-frame-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var traceStore = new AgentRunTraceStore(root);
        var events = new List<CodexParsedEvent>
        {
            new(
                "{}",
                CodexParsedEventType.AgentMessage,
                """{"type":"agent_task_frame","taskFrame":{"userGoal":"check target cells","currentState":"tableComparison","reference":"current source table","editableTarget":"current target table","desiredOutputLanguage":"Simplified Chinese","writeIntent":"read-only","preferredContext":"current-view","requiredKnowledge":["core.contract"],"toolFitCheck":"Need non-simplified detector."}}""",
                null,
                false,
                false,
                null)
        };
        var service = CreateChatService(events, traceStore, root);

        await foreach (var _ in service.RunCodexAsync("检查当前表格繁中", null, null, null, null, null, null, null, CancellationToken.None))
        {
        }

        var runId = Directory.EnumerateFiles(Path.Combine(root, "agent", "runs"), "*.jsonl")
            .Select(Path.GetFileNameWithoutExtension)
            .Single();
        var trace = await traceStore.ReadAsync(runId!, CancellationToken.None);

        Assert.Contains(trace, evt => evt.EventName == "task_frame" && evt.DataJson.Contains("toolFitCheck", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChatService_records_capability_gap_events_in_run_trace()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-capability-gap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var traceStore = new AgentRunTraceStore(root);
        var events = new List<CodexParsedEvent>
        {
            new(
                "{}",
                CodexParsedEventType.AgentMessage,
                """{"type":"agent_capability_gap","failureType":"tool_semantics_mismatch","userGoal":"check Traditional Chinese target cells","missingCapability":"non-simplified current target detector","proposedNextAction":"use poe_find_current_table_non_simplified_chinese_cells"}""",
                null,
                false,
                false,
                null)
        };
        var service = CreateChatService(events, traceStore, root);

        await foreach (var _ in service.RunCodexAsync("为什么你刚才说没有漏翻", null, null, null, null, null, null, null, CancellationToken.None))
        {
        }

        var runId = Directory.EnumerateFiles(Path.Combine(root, "agent", "runs"), "*.jsonl")
            .Select(Path.GetFileNameWithoutExtension)
            .Single();
        var trace = await traceStore.ReadAsync(runId!, CancellationToken.None);

        Assert.Contains(trace, evt => evt.EventName == "capability_gap" && evt.DataJson.Contains("tool_semantics_mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChatService_records_semantic_events_embedded_in_agent_message_text()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-embedded-semantic-events-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var traceStore = new AgentRunTraceStore(root);
        var events = new List<CodexParsedEvent>
        {
            new(
                "{}",
                CodexParsedEventType.AgentMessage,
                """
                {"type":"agent_task_frame","taskFrame":{"userGoal":"check target cells","currentState":"tableComparison","reference":"current source table","editableTarget":"current target table","desiredOutputLanguage":"Simplified Chinese","writeIntent":"read-only","preferredContext":"current-view","requiredKnowledge":["core.contract"],"toolFitCheck":"Need non-simplified detector."}}
                {"type":"agent_capability_gap","failureType":"tool_semantics_mismatch","userGoal":"check Traditional Chinese target cells","missingCapability":"non-simplified current target detector","proposedNextAction":"use poe_find_current_table_non_simplified_chinese_cells"}

                Visible explanation for the user.
                """,
                null,
                false,
                false,
                null)
        };
        var service = CreateChatService(events, traceStore, root);

        await foreach (var _ in service.RunCodexAsync("检查当前表格繁中", null, null, null, null, null, null, null, CancellationToken.None))
        {
        }

        var runId = Directory.EnumerateFiles(Path.Combine(root, "agent", "runs"), "*.jsonl")
            .Select(Path.GetFileNameWithoutExtension)
            .Single();
        var trace = await traceStore.ReadAsync(runId!, CancellationToken.None);

        Assert.Contains(trace, evt => evt.EventName == "task_frame" && evt.DataJson.Contains("toolFitCheck", StringComparison.Ordinal));
        Assert.Contains(trace, evt => evt.EventName == "capability_gap" && evt.DataJson.Contains("tool_semantics_mismatch", StringComparison.Ordinal));
    }

    private static ChatService CreateChatService(
        IReadOnlyList<CodexParsedEvent> events,
        AgentRunTraceStore traceStore,
        string root)
    {
        var runner = new FakeCodexRunner(async (settings, prompt, onEvent, ct) =>
        {
            foreach (var evt in events)
            {
                if (onEvent is not null)
                {
                    await onEvent(evt);
                }
            }

            return new CodexRunResult(0, false, false, events, null);
        });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PoeStudio:WorkspaceRoot"] = root
            })
            .Build();
        var workspaceRoot = new WorkspaceRootProvider(config);
        return new ChatService(
            runner,
            workspaceRoot,
            config,
            new AgentCurrentViewStore(workspaceRoot.CurrentRoot),
            traceStore,
            TimeSpan.FromSeconds(30));
    }

    private sealed class FakeCodexRunner : ICodexProcessRunner
    {
        private readonly Func<AgentSettingsDto, string, Func<CodexParsedEvent, Task>?, CancellationToken, Task<CodexRunResult>> handler;

        public FakeCodexRunner(Func<AgentSettingsDto, string, Func<CodexParsedEvent, Task>?, CancellationToken, Task<CodexRunResult>> handler)
        {
            this.handler = handler;
        }

        public Task<CodexRunResult> RunAsync(AgentSettingsDto settings, string prompt, Func<CodexParsedEvent, Task>? onEvent, CancellationToken cancellationToken)
        {
            return handler(settings, prompt, onEvent, cancellationToken);
        }
    }
}
