using System.Diagnostics;
using PoeStudio.Contracts;

namespace PoeStudio.Core.Agent;

public interface ICodexProcessRunner
{
    Task<CodexRunResult> RunAsync(
        AgentSettingsDto settings,
        string prompt,
        Func<CodexParsedEvent, Task>? onEvent,
        CancellationToken cancellationToken);

    Task<CodexRunResult> RunAsync(
        AgentSettingsDto settings,
        string prompt,
        CancellationToken cancellationToken)
    {
        return RunAsync(settings, prompt, null, cancellationToken);
    }
}

public sealed class CodexProcessRunner : ICodexProcessRunner
{
    private readonly CodexJsonEventParser _parser;
    private readonly TimeSpan _noOutputTimeout;

    public CodexProcessRunner(CodexJsonEventParser parser)
        : this(parser, TimeSpan.FromSeconds(30))
    {
    }

    public CodexProcessRunner(CodexJsonEventParser parser, TimeSpan noOutputTimeout)
    {
        _parser = parser;
        _noOutputTimeout = noOutputTimeout;
    }

    public Task<CodexRunResult> RunAsync(
        AgentSettingsDto settings,
        string prompt,
        CancellationToken cancellationToken)
    {
        return RunAsync(settings, prompt, null, cancellationToken);
    }

    public async Task<CodexRunResult> RunAsync(
        AgentSettingsDto settings,
        string prompt,
        Func<CodexParsedEvent, Task>? onEvent,
        CancellationToken cancellationToken)
    {
        var events = new List<CodexParsedEvent>();
        var stderrLines = new List<string>();
        var lastOutputAt = DateTimeOffset.UtcNow;
        using var process = new Process
        {
            StartInfo = BuildStartInfo(settings, prompt),
            EnableRaisingEvents = true
        };

        async Task PublishAsync(CodexParsedEvent parsedEvent)
        {
            lock (events)
            {
                events.Add(parsedEvent);
                lastOutputAt = DateTimeOffset.UtcNow;
            }

            if (onEvent is not null)
            {
                await onEvent(parsedEvent);
            }
        }

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            var parsedEvent = new CodexParsedEvent(
                string.Empty,
                CodexParsedEventType.Error,
                ex.Message,
                null,
                true,
                false,
                null);
            await PublishAsync(parsedEvent);
            return new CodexRunResult(null, true, false, events.ToArray(), ex.Message, "process_start_failed");
        }

        var stdoutTask = ReadStdoutAsync(process, PublishAsync);
        var stderrTask = ReadStderrAsync(process, PublishAsync, stderrLines);
        var cancelled = false;
        var timedOut = false;
        await using var cancellationRegistration = cancellationToken.Register(() =>
        {
            if (!process.HasExited)
            {
                KillProcessTree(process);
            }
        });

        var waitTask = process.WaitForExitAsync(CancellationToken.None);
        while (!waitTask.IsCompleted)
        {
            var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken.None));
            if (completed == waitTask)
            {
                break;
            }

            if (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow - lastOutputAt > _noOutputTimeout)
            {
                timedOut = true;
                await PublishAsync(new CodexParsedEvent(
                    string.Empty,
                    CodexParsedEventType.Error,
                    $"No Codex output was observed for {_noOutputTimeout.TotalSeconds:0.#} seconds.",
                    null,
                    true,
                    false,
                    null));
                if (!process.HasExited)
                {
                    KillProcessTree(process);
                }

                break;
            }
        }

        await waitTask;
        if (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
            await PublishAsync(new CodexParsedEvent(
                string.Empty,
                CodexParsedEventType.Cancelled,
                "Codex run cancelled",
                null,
                true,
                false,
                null));
        }

        await Task.WhenAll(SwallowCancellation(stdoutTask), SwallowCancellation(stderrTask));
        int? exitCode = process.HasExited ? process.ExitCode : null;
        var hasErrorEvent = events.Any(x => x.EventType == CodexParsedEventType.Error);
        var failed = !cancelled && (timedOut || exitCode != 0 || hasErrorEvent);
        var errorCode = timedOut ? "no_output_timeout" : failed ? "codex_failed" : null;
        var errorSummary = events.LastOrDefault(x => x.EventType == CodexParsedEventType.Error)?.Message
            ?? (stderrLines.Count == 0 ? null : string.Join(Environment.NewLine, stderrLines.Take(20)));
        return new CodexRunResult(
            exitCode,
            failed,
            cancelled,
            events.ToArray(),
            errorSummary,
            errorCode);
    }

    private ProcessStartInfo BuildStartInfo(AgentSettingsDto settings, string prompt)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = settings.CodexPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = string.IsNullOrWhiteSpace(settings.WorkingDirectory)
                ? Environment.CurrentDirectory
                : settings.WorkingDirectory
        };

        if (IsFakePowerShell(settings.CodexPath, prompt))
        {
            foreach (var argument in SplitPowerShellArguments(prompt))
            {
                startInfo.ArgumentList.Add(argument);
            }

            return startInfo;
        }

        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add("--json");
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(settings.WorkingDirectory);
        if (!string.IsNullOrWhiteSpace(settings.Model))
        {
            startInfo.ArgumentList.Add("-m");
            startInfo.ArgumentList.Add(settings.Model);
        }

        if (!string.IsNullOrWhiteSpace(settings.Profile))
        {
            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add(settings.Profile);
        }

        if (!string.IsNullOrWhiteSpace(settings.Sandbox))
        {
            startInfo.ArgumentList.Add("-s");
            startInfo.ArgumentList.Add(settings.Sandbox);
        }

        startInfo.ArgumentList.Add(prompt);
        return startInfo;
    }

    private async Task ReadStdoutAsync(
        Process process,
        Func<CodexParsedEvent, Task> onEvent)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            await onEvent(_parser.ParseLine(line));
        }
    }

    private static async Task ReadStderrAsync(
        Process process,
        Func<CodexParsedEvent, Task> onEvent,
        List<string> stderrLines)
    {
        while (true)
        {
            var line = await process.StandardError.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            stderrLines.Add(line);
            await onEvent(new CodexParsedEvent(
                line,
                CodexParsedEventType.StdErr,
                line,
                line,
                false,
                false,
                null));
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task SwallowCancellation(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool IsFakePowerShell(string codexPath, string prompt)
    {
        return (string.Equals(codexPath, "powershell", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codexPath, "pwsh", StringComparison.OrdinalIgnoreCase))
            && prompt.Contains("-File", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> SplitPowerShellArguments(string value)
    {
        var arguments = new List<string>();
        var current = new List<char>();
        var inQuotes = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                Flush();
                continue;
            }

            current.Add(c);
        }

        Flush();
        return arguments;

        void Flush()
        {
            if (current.Count == 0)
            {
                return;
            }

            arguments.Add(new string(current.ToArray()));
            current.Clear();
        }
    }
}
