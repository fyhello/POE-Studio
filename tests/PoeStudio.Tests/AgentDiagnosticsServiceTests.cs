using PoeStudio.Api;
using PoeStudio.Contracts;

namespace PoeStudio.Tests;

public sealed class AgentDiagnosticsServiceTests
{
    [Fact]
    public void Analyze_detects_tool_completed_without_final_answer()
    {
        var events = new[]
        {
            new AgentRunTraceEventDto("run", "started", "{\"runMode\":\"normal\"}", DateTimeOffset.UtcNow),
            new AgentRunTraceEventDto("tool_call", "observed", "{\"tool\":\"poe_find_current_table_untranslated_cells\",\"status\":\"completed\"}", DateTimeOffset.UtcNow),
            new AgentRunTraceEventDto("done", "observed", "{\"type\":\"completed\"}", DateTimeOffset.UtcNow)
        };

        var result = AgentDiagnosticsService.Analyze("run-1", events);

        Assert.Equal("no_final_answer_after_tool_result", result.Code);
        Assert.True(result.ShouldStartDiagnosticRun);
    }

    [Fact]
    public void Analyze_does_not_mark_recent_tool_call_as_hung_before_threshold()
    {
        var now = DateTimeOffset.UtcNow;
        var events = new[]
        {
            new AgentRunTraceEventDto("run", "started", "{\"runMode\":\"normal\"}", now.AddSeconds(-5)),
            new AgentRunTraceEventDto("tool_call", "observed", "{\"tool\":\"poe_find_current_table_untranslated_cells\",\"status\":\"in_progress\"}", now.AddSeconds(-5))
        };

        var result = AgentDiagnosticsService.Analyze("run-1", events, now, TimeSpan.FromSeconds(30));

        Assert.Equal("none", result.Code);
        Assert.False(result.ShouldStartDiagnosticRun);
    }

    [Fact]
    public void Analyze_marks_tool_call_as_hung_after_threshold()
    {
        var now = DateTimeOffset.UtcNow;
        var events = new[]
        {
            new AgentRunTraceEventDto("run", "started", "{\"runMode\":\"normal\"}", now.AddSeconds(-40)),
            new AgentRunTraceEventDto("tool_call", "observed", "{\"tool\":\"poe_find_current_table_untranslated_cells\",\"status\":\"in_progress\"}", now.AddSeconds(-40))
        };

        var result = AgentDiagnosticsService.Analyze("run-1", events, now, TimeSpan.FromSeconds(30));

        Assert.Equal("tool_call_left_in_progress", result.Code);
        Assert.True(result.ShouldStartDiagnosticRun);
    }

    [Theory]
    [InlineData(AgentRunModes.Diagnostic)]
    [InlineData(AgentRunModes.Repair)]
    public void Analyze_never_auto_diagnoses_diagnostic_or_repair_runs(string runMode)
    {
        var now = DateTimeOffset.UtcNow;
        var events = new[]
        {
            new AgentRunTraceEventDto("run", "started", $"{{\"runMode\":\"{runMode}\"}}", now.AddSeconds(-40)),
            new AgentRunTraceEventDto("tool_call", "observed", "{\"tool\":\"poe_find_current_table_untranslated_cells\",\"status\":\"in_progress\"}", now.AddSeconds(-40))
        };

        var result = AgentDiagnosticsService.Analyze("run-1", events, now, TimeSpan.FromSeconds(30));

        Assert.Equal("none", result.Code);
        Assert.False(result.ShouldStartDiagnosticRun);
        Assert.Equal(runMode, result.RunMode);
    }
}
