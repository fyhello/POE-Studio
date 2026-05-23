using PoeStudio.Contracts;
using PoeStudio.Core.Agent;

namespace PoeStudio.Tests;

public sealed class AgentPlannerPromptBuilderTests
{
    [Fact]
    public void Build_instructs_codex_to_plan_not_execute()
    {
        var builder = new AgentPlannerPromptBuilder();
        var settings = new AgentSettingsDto("codex", null, null, "workspace-write", "poe-studio", "C:/repo", "manual");
        var thread = new AgentThreadDto("thread-1", "profile-target", "Agent", "Goal", "auto", AgentThreadStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var prompt = builder.Build(
            settings,
            thread,
            messages: [],
            goal: "重新翻译刚才的表",
            selectedResourcePath: null,
            recentRuns:
            [
                new AgentRunDto("run-1", "thread-1", "profile-target", "Translate", "datc64-translation", AgentRunStatus.WaitingForApproval, 90, "Waiting", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, null, null, null, "data/balance/traditional chinese/activeskills.datc64")
            ],
            capabilities: AgentCapabilities.All,
            projectContext: null);

        Assert.Contains("You are the planning brain", prompt);
        Assert.Contains("Do not execute the task", prompt);
        Assert.Contains("Return only a fenced JSON block", prompt);
        Assert.Contains("datc64-translation", prompt);
        Assert.Contains("data/balance/traditional chinese/activeskills.datc64", prompt);
        Assert.DoesNotContain("keyword classifier", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AgentCapabilities.GetRequired(\"auto\")", prompt, StringComparison.Ordinal);
    }
}
