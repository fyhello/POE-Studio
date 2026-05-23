namespace PoeStudio.Tests;

public sealed class FrontendAgentWorkspaceTests
{
    [Fact]
    public async Task Index_contains_agent_workspace_shell()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "src", "PoeStudio.Api", "wwwroot", "index.html"));

        Assert.Contains("id=\"openAgentWorkspaceBtn\"", html);
        Assert.Contains("id=\"agentWorkspace\"", html);
        Assert.Contains("id=\"agentThreadList\"", html);
        Assert.Contains("id=\"agentGoalInput\"", html);
        Assert.Contains("id=\"agentRunBtn\"", html);
        Assert.Contains("id=\"agentPlanList\"", html);
        Assert.Contains("id=\"agentEventTimeline\"", html);
        Assert.Contains("id=\"agentApprovalsPanel\"", html);
        Assert.Contains("id=\"agentResultPanel\"", html);
    }

    [Fact]
    public async Task App_js_contains_agent_bootstrap_and_restore_flow()
    {
        var js = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("agent:", js);
        Assert.Contains("loadAgentWorkspace", js);
        Assert.Contains("/api/agent/settings", js);
        Assert.Contains("/api/agent/capabilities", js);
        Assert.Contains("/api/agent/threads?take=", js);
        Assert.Contains("localStorage.getItem(\"poeStudioAgentThreadId\")", js);
        Assert.Contains("renderAgentThreads", js);
        Assert.Contains("renderAgentSnapshot", js);
        Assert.Contains("/api/agent/runs/${encodeURIComponent(latestRun.id)}/events?afterSequence=0", js);
    }

    [Fact]
    public async Task App_js_starts_agent_runs_from_natural_language_goal()
    {
        var js = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("startAgentRun", js);
        Assert.Contains("/api/agent/threads", js);
        Assert.Contains("/api/agent/runs", js);
        Assert.Contains("agentGoalInput", js);
        Assert.Contains("agentTaskKindSelect", js);
        Assert.Contains("agentResourcePathInput", js);
        Assert.Contains("datc64-translation", js);
    }

    [Fact]
    public async Task App_js_passes_current_oodle_path_to_agent_runs()
    {
        var js = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("oodlePath: currentOodlePath()", js);
        Assert.Contains("resourcePath: taskKind === \"datc64-translation\" ? resourcePath : null", js);
    }

    [Fact]
    public async Task App_js_renders_agent_plan_events_status_and_result()
    {
        var js = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("startAgentEventPolling", js);
        Assert.Contains("/api/agent/runs/${encodeURIComponent(run.id)}/events", js);
        Assert.Contains("renderAgentPlan", js);
        Assert.Contains("renderAgentEvents", js);
        Assert.Contains("renderAgentRunStatus", js);
        Assert.Contains("agentResultPanel", js);
        Assert.Contains("Project context loaded", js);
    }

    [Fact]
    public async Task App_js_exposes_agent_approval_retry_and_cancel_actions()
    {
        var js = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("renderAgentApprovals", js);
        Assert.Contains("/api/agent/approvals/${encodeURIComponent(approvalId)}/approve", js);
        Assert.Contains("/api/agent/approvals/${encodeURIComponent(approvalId)}/reject", js);
        Assert.Contains("/api/agent/runs/${encodeURIComponent(run.id)}/cancel", js);
        Assert.Contains("/api/agent/runs/${encodeURIComponent(run.id)}/retry", js);
        Assert.Contains("approval.proposalJson", js);
    }

    [Fact]
    public async Task Styles_define_agent_workspace_layout_without_marketing_shell()
    {
        var css = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "src", "PoeStudio.Api", "wwwroot", "styles.css"));

        Assert.Contains(".agent-workspace", css);
        Assert.Contains(".agent-sidebar", css);
        Assert.Contains(".agent-event-timeline", css);
        Assert.Contains(".agent-approval", css);
        Assert.DoesNotContain("agent-hero", css, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PoeStudio.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
