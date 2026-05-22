using System.Diagnostics;
using PoeStudio.Contracts;

namespace PoeStudio.Core.Agent;

public interface ICodexProcessRunner
{
    Task<CodexRunResult> RunAsync(
        AgentSettingsDto settings,
        string prompt,
        CancellationToken cancellationToken);
}

public sealed class CodexProcessRunner : ICodexProcessRunner
{
    private readonly CodexJsonEventParser _parser;

    public CodexProcessRunner(CodexJsonEventParser parser)
    {
        _parser = parser;
    }

    public async Task<CodexRunResult> RunAsync(
        AgentSettingsDto settings,
        string prompt,
        CancellationToken cancellationToken)
    {
        var events = new List<CodexParsedEvent>();
        var stderrLines = new List<string>();
        using var process = new Process
        {
            StartInfo = BuildStartInfo(settings, prompt),
            EnableRaisingEvents = true
        };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            events.Add(new CodexParsedEvent(
                string.Empty,
                CodexParsedEventType.Error,
                ex.Message,
                null,
                true,
                false,
                null));
            return new CodexRunResult(null, true, false, events, ex.Message);
        }

        var stdoutTask = ReadStdoutAsync(process, events);
        var stderrTask = ReadStderrAsync(process, events, stderrLines);
        var cancelled = false;
        await using var cancellationRegistration = cancellationToken.Register(() =>
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        });

        await process.WaitForExitAsync(CancellationToken.None);
        if (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
            events.Add(new CodexParsedEvent(
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
        var failed = !cancelled && exitCode != 0;
        return new CodexRunResult(
            exitCode,
            failed,
            cancelled,
            events.ToArray(),
            stderrLines.Count == 0 ? null : string.Join(Environment.NewLine, stderrLines.Take(20)));
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
        List<CodexParsedEvent> events)
    {
        while (!process.StandardOutput.EndOfStream)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            events.Add(_parser.ParseLine(line));
        }
    }

    private static async Task ReadStderrAsync(
        Process process,
        List<CodexParsedEvent> events,
        List<string> stderrLines)
    {
        while (!process.StandardError.EndOfStream)
        {
            var line = await process.StandardError.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            stderrLines.Add(line);
            events.Add(new CodexParsedEvent(
                line,
                CodexParsedEventType.StdErr,
                line,
                line,
                false,
                false,
                null));
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
