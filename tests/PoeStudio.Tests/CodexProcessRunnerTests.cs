using PoeStudio.Contracts;
using PoeStudio.Core.Agent;

namespace PoeStudio.Tests;

public sealed class CodexProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_converts_stdout_jsonl_to_events()
    {
        var script = await CreateScriptAsync("""
            Write-Output '{"type":"item.started","item":{"type":"mcp_tool_call","server":"poe-studio","tool":"poe_list_profiles","status":"in_progress"}}'
            Write-Output '{"type":"item.completed","item":{"type":"agent_message","text":"done"}}'
            exit 0
            """);
        var runner = new CodexProcessRunner(new CodexJsonEventParser());

        var result = await runner.RunAsync(
            Settings("powershell"),
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.Cancelled);
        Assert.Contains(result.Events, x => x.EventType == CodexParsedEventType.McpToolCall && x.ToolName == "poe_list_profiles");
        Assert.Contains(result.Events, x => x.EventType == CodexParsedEventType.AgentMessage && x.Message == "done");
    }

    [Fact]
    public async Task RunAsync_captures_stderr_as_events()
    {
        var script = await CreateScriptAsync("""
            [Console]::Error.WriteLine('bad things')
            exit 0
            """);
        var runner = new CodexProcessRunner(new CodexJsonEventParser());

        var result = await runner.RunAsync(
            Settings("powershell"),
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
            CancellationToken.None);

        Assert.Contains(result.Events, x => x.EventType == CodexParsedEventType.StdErr && x.Message.Contains("bad things", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_marks_non_zero_exit_as_failed()
    {
        var script = await CreateScriptAsync("""
            Write-Output '{"type":"item.completed","item":{"type":"agent_message","text":"before failure"}}'
            exit 7
            """);
        var runner = new CodexProcessRunner(new CodexJsonEventParser());

        var result = await runner.RunAsync(
            Settings("powershell"),
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
            CancellationToken.None);

        Assert.Equal(7, result.ExitCode);
        Assert.True(result.Failed);
    }

    [Fact]
    public async Task RunAsync_kills_process_when_cancelled()
    {
        var script = await CreateScriptAsync("""
            Start-Sleep -Seconds 10
            exit 0
            """);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var runner = new CodexProcessRunner(new CodexJsonEventParser());

        var result = await runner.RunAsync(
            Settings("powershell"),
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
            cts.Token);

        Assert.True(result.Cancelled);
        Assert.Contains(result.Events, x => x.EventType == CodexParsedEventType.Cancelled);
    }

    private static AgentSettingsDto Settings(string codexPath)
    {
        return new AgentSettingsDto(codexPath, null, null, "workspace-write", "poe-studio", Directory.GetCurrentDirectory(), "manual");
    }

    private static async Task<string> CreateScriptAsync(string content)
    {
        var directory = Path.Combine(Path.GetTempPath(), "poe-studio-codex-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "fake-codex.ps1");
        await File.WriteAllTextAsync(path, content);
        return path;
    }
}
