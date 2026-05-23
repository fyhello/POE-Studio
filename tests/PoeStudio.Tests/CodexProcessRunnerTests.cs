using System.Diagnostics;
using System.Reflection;
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;

namespace PoeStudio.Tests;

public sealed class CodexProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_streams_stdout_events_before_process_exits()
    {
        var script = await CreateScriptAsync("""
            [Console]::Out.WriteLine('{"type":"item.started","item":{"type":"mcp_tool_call","server":"poe-studio","tool":"poe_list_profiles","status":"in_progress"}}')
            [Console]::Out.Flush()
            Start-Sleep -Seconds 2
            exit 0
            """);
        var runner = new CodexProcessRunner(new CodexJsonEventParser());
        var streamed = new List<CodexParsedEvent>();

        var runTask = runner.RunAsync(
            Settings("powershell"),
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
            parsedEvent =>
            {
                streamed.Add(parsedEvent);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await WaitForAsync(() => streamed.Any(x => x.EventType == CodexParsedEventType.McpToolCall));
        Assert.False(runTask.IsCompleted);

        var result = await runTask;
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void RunAsync_passes_approval_mode_as_codex_global_argument()
    {
        var runner = new CodexProcessRunner(new CodexJsonEventParser());
        var buildStartInfo = typeof(CodexProcessRunner).GetMethod("BuildStartInfo", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildStartInfo was not found.");

        var startInfo = (ProcessStartInfo)buildStartInfo.Invoke(
            runner,
            [Settings("codex") with { ApprovalMode = "never" }, "prompt"])!;

        var args = startInfo.ArgumentList.ToArray();
        Assert.Contains("-a", args);
        Assert.Contains("never", args);
        Assert.True(Array.IndexOf(args, "-a") < Array.IndexOf(args, "exec"));
    }

    [Fact]
    public void RunAsync_passes_oodle_path_to_child_environment()
    {
        var runner = new CodexProcessRunner(new CodexJsonEventParser());
        var buildStartInfo = typeof(CodexProcessRunner).GetMethod("BuildStartInfo", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildStartInfo was not found.");
        var oodlePath = Path.Combine(Path.GetTempPath(), "oo2core.dll");

        var startInfo = (ProcessStartInfo)buildStartInfo.Invoke(
            runner,
            [Settings("codex") with { OodlePath = oodlePath }, "prompt"])!;

        Assert.True(startInfo.Environment.TryGetValue("POE_STUDIO_OODLE_PATH", out var value));
        Assert.Equal(oodlePath, value);
        var args = startInfo.ArgumentList.ToArray();
        Assert.Contains("-c", args);
        Assert.Contains($"mcp_servers.poe-studio.env.POE_STUDIO_OODLE_PATH=\"{oodlePath.Replace("\\", "\\\\", StringComparison.Ordinal)}\"", args);
    }

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
    public async Task RunAsync_marks_error_event_as_failed_even_when_exit_code_is_zero()
    {
        var script = await CreateScriptAsync("""
            Write-Output '{"type":"item.completed","item":{"type":"mcp_tool_call","server":"poe-studio","tool":"poe_get_workspace","status":"failed","error":"user cancelled MCP tool call"}}'
            exit 0
            """);
        var runner = new CodexProcessRunner(new CodexJsonEventParser());

        var result = await runner.RunAsync(
            Settings("powershell"),
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Failed);
        Assert.Contains("user cancelled", result.StderrSummary);
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

    [Fact]
    public async Task RunAsync_fails_when_no_output_is_observed_before_watchdog_timeout()
    {
        var script = await CreateScriptAsync("""
            Start-Sleep -Seconds 10
            exit 0
            """);
        var runner = new CodexProcessRunner(
            new CodexJsonEventParser(),
            TimeSpan.FromMilliseconds(300),
            TimeSpan.FromSeconds(10));

        var result = await runner.RunAsync(
            Settings("powershell"),
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
            CancellationToken.None);

        Assert.True(result.Failed);
        Assert.Equal("no_output_timeout", result.ErrorCode);
        Assert.Contains(result.Events, x => x.EventType == CodexParsedEventType.Error && x.Message.Contains("No Codex output", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_allows_longer_silence_after_tool_output()
    {
        var script = await CreateScriptAsync("""
            Write-Output '{"type":"item.completed","item":{"type":"mcp_tool_call","server":"poe-studio","tool":"poe_datc64_extract_translatable_cells","status":"completed"}}'
            Start-Sleep -Milliseconds 900
            Write-Output '{"type":"item.completed","item":{"type":"agent_message","text":"```json\n{\"taskKind\":\"datc64-translation\",\"candidates\":[]}\n```"}}'
            exit 0
            """);
        var runner = new CodexProcessRunner(
            new CodexJsonEventParser(),
            TimeSpan.FromMilliseconds(300),
            TimeSpan.FromSeconds(2));

        var result = await runner.RunAsync(
            Settings("powershell"),
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
            CancellationToken.None);

        Assert.False(result.Failed);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.ErrorCode);
        Assert.Contains(result.Events, x => x.EventType == CodexParsedEventType.McpToolCall && x.ToolName == "poe_datc64_extract_translatable_cells");
        Assert.Contains(result.Events, x => x.EventType == CodexParsedEventType.AgentMessage && x.Message.Contains("datc64-translation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_kills_child_process_when_cancelled()
    {
        var marker = Path.Combine(Path.GetTempPath(), "poe-studio-codex-runner-tests", Guid.NewGuid().ToString("N"), "child.pid");
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        var childScript = await CreateScriptAsync("""
            $pid | Set-Content -Path $args[0]
            Start-Sleep -Seconds 30
            """);
        var parentScript = await CreateScriptAsync($$"""
            $child = Start-Process powershell -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', '{{childScript}}', '{{marker}}') -PassThru
            Start-Sleep -Seconds 30
            exit 0
            """);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        var runner = new CodexProcessRunner(new CodexJsonEventParser());

        var result = await runner.RunAsync(
            Settings("powershell"),
            $"-NoProfile -ExecutionPolicy Bypass -File \"{parentScript}\"",
            cts.Token);

        Assert.True(result.Cancelled);
        if (File.Exists(marker) && int.TryParse(await File.ReadAllTextAsync(marker), out var childPid))
        {
            await WaitForAsync(() => Process.GetProcesses().All(x => x.Id != childPid));
        }
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

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Condition was not met.");
    }
}
