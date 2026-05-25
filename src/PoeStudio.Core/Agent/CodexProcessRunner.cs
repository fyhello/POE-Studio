using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
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
    private readonly TimeSpan _startupNoOutputTimeout;
    private readonly TimeSpan _activeNoOutputTimeout;
    private readonly TimeSpan _terminalExitTimeout;

    public CodexProcessRunner(CodexJsonEventParser parser)
        : this(parser, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(2))
    {
    }

    public CodexProcessRunner(CodexJsonEventParser parser, TimeSpan noOutputTimeout)
        : this(parser, noOutputTimeout, noOutputTimeout)
    {
    }

    public CodexProcessRunner(
        CodexJsonEventParser parser,
        TimeSpan startupNoOutputTimeout,
        TimeSpan activeNoOutputTimeout)
        : this(parser, startupNoOutputTimeout, activeNoOutputTimeout, TimeSpan.FromSeconds(2))
    {
    }

    public CodexProcessRunner(
        CodexJsonEventParser parser,
        TimeSpan startupNoOutputTimeout,
        TimeSpan activeNoOutputTimeout,
        TimeSpan terminalExitTimeout)
    {
        _parser = parser;
        _startupNoOutputTimeout = startupNoOutputTimeout;
        _activeNoOutputTimeout = activeNoOutputTimeout;
        _terminalExitTimeout = terminalExitTimeout;
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
        var observedOutput = false;
        DateTimeOffset? terminalObservedAt = null;
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
                observedOutput = true;
                if (parsedEvent.IsTerminal)
                {
                    terminalObservedAt = lastOutputAt;
                }
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
        var killedAfterTerminal = false;
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

            DateTimeOffset? terminalAt;
            TimeSpan timeout;
            lock (events)
            {
                timeout = observedOutput ? _activeNoOutputTimeout : _startupNoOutputTimeout;
                terminalAt = terminalObservedAt;
            }

            if (terminalAt is not null && DateTimeOffset.UtcNow - terminalAt > _terminalExitTimeout)
            {
                if (!process.HasExited)
                {
                    killedAfterTerminal = true;
                    KillProcessTree(process);
                }

                break;
            }

            if (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow - lastOutputAt > timeout)
            {
                timedOut = true;
                await PublishAsync(new CodexParsedEvent(
                    string.Empty,
                    CodexParsedEventType.Error,
                    $"No Codex output was observed for {timeout.TotalSeconds:0.#} seconds.",
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
        var hasErrorEvent = HasUnrecoveredError(events);
        var failed = !cancelled && (timedOut || (exitCode != 0 && !killedAfterTerminal) || hasErrorEvent);
        var errorCode = timedOut ? "no_output_timeout" : failed ? "codex_failed" : null;
        var errorSummary = events.LastOrDefault(x => x.EventType == CodexParsedEventType.Error && !IsRecoveredReconnectError(events, x))?.Message
            ?? (stderrLines.Count == 0 ? null : string.Join(Environment.NewLine, stderrLines.Take(20)));
        return new CodexRunResult(
            exitCode,
            failed,
            cancelled,
            events.ToArray(),
            errorSummary,
            errorCode);
    }

    private static bool HasUnrecoveredError(IReadOnlyList<CodexParsedEvent> events)
    {
        return events.Any(x => x.EventType == CodexParsedEventType.Error && !IsRecoveredReconnectError(events, x));
    }

    private static bool IsRecoveredReconnectError(IReadOnlyList<CodexParsedEvent> events, CodexParsedEvent error)
    {
        if (!error.Message.StartsWith("Reconnecting...", StringComparison.Ordinal))
        {
            return false;
        }

        var errorIndex = IndexOfEvent(events, error);
        return errorIndex >= 0
            && (events.Skip(errorIndex + 1).Any(x => x.EventType == CodexParsedEventType.FinalMessage && x.IsTerminal)
                || events.Take(errorIndex).Any(x => x.EventType is CodexParsedEventType.AgentMessage or CodexParsedEventType.FinalMessage));
    }

    private static int IndexOfEvent(IReadOnlyList<CodexParsedEvent> events, CodexParsedEvent target)
    {
        for (var index = 0; index < events.Count; index++)
        {
            if (ReferenceEquals(events[index], target) || events[index].Equals(target))
            {
                return index;
            }
        }

        return -1;
    }

    private ProcessStartInfo BuildStartInfo(AgentSettingsDto settings, string prompt)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveCodexExecutable(settings.CodexPath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = string.IsNullOrWhiteSpace(settings.WorkingDirectory)
                ? Environment.CurrentDirectory
                : settings.WorkingDirectory
        };

        if (!string.IsNullOrWhiteSpace(settings.OodlePath))
        {
            startInfo.Environment["POE_STUDIO_OODLE_PATH"] = settings.OodlePath;
        }

        if (IsFakePowerShell(settings.CodexPath, prompt))
        {
            foreach (var argument in SplitPowerShellArguments(prompt))
            {
                startInfo.ArgumentList.Add(argument);
            }

            return startInfo;
        }

        startInfo.Environment["CODEX_HOME"] = PrepareIsolatedCodexHome(settings);

        AddPoeStudioMcpOverrides(startInfo.ArgumentList, settings);

        if (!string.IsNullOrWhiteSpace(settings.OodlePath))
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add($"mcp_servers.{settings.McpServerName}.env.POE_STUDIO_OODLE_PATH={JsonSerializer.Serialize(settings.OodlePath)}");
        }

        var approvalMode = NormalizeApprovalMode(settings.ApprovalMode);
        if (!string.IsNullOrWhiteSpace(approvalMode))
        {
            startInfo.ArgumentList.Add("-a");
            startInfo.ArgumentList.Add(approvalMode);
        }

        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add("--json");
        startInfo.ArgumentList.Add("--ignore-rules");
        startInfo.ArgumentList.Add("--skip-git-repo-check");
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

    private static string PrepareIsolatedCodexHome(AgentSettingsDto settings)
    {
        var sourceHome = ResolveSourceCodexHome();
        var targetHome = ResolvePoeStudioCodexHome();
        Directory.CreateDirectory(targetHome);
        CopyIfExists(Path.Combine(sourceHome, "auth.json"), Path.Combine(targetHome, "auth.json"));
        CopyIfExists(Path.Combine(sourceHome, ".credentials.json"), Path.Combine(targetHome, ".credentials.json"));
        WriteIsolatedConfig(sourceHome, targetHome, settings);
        return targetHome;
    }

    private static string ResolveSourceCodexHome()
    {
        var configured = Environment.GetEnvironmentVariable("POE_STUDIO_CODEX_SOURCE_HOME");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(codexHome))
        {
            return codexHome;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    }

    private static string ResolvePoeStudioCodexHome()
    {
        var configured = Environment.GetEnvironmentVariable("POE_STUDIO_AGENT_CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(ResolvePoeStudioWorkspaceRoot(), "agent", "codex-home");
    }

    private static void CopyIfExists(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static void WriteIsolatedConfig(string sourceHome, string targetHome, AgentSettingsDto settings)
    {
        var sourceConfigPath = Path.Combine(sourceHome, "config.toml");
        var targetConfigPath = Path.Combine(targetHome, "config.toml");
        var lines = File.Exists(sourceConfigPath)
            ? File.ReadAllLines(sourceConfigPath)
            : [];
        var selectedLines = ExtractCodexRuntimeConfig(lines).ToList();
        if (selectedLines.Count > 0 && !string.IsNullOrWhiteSpace(selectedLines[^1]))
        {
            selectedLines.Add(string.Empty);
        }

        selectedLines.Add("[features]");
        selectedLines.Add($"memories = {FormatTomlBoolean(settings.Memories)}");
        selectedLines.Add($"skills = {FormatTomlBoolean(settings.Skills)}");
        selectedLines.Add($"command_execution = {FormatTomlBoolean(settings.CommandExecution)}");
        File.WriteAllLines(targetConfigPath, selectedLines);
    }

    private static string FormatTomlBoolean(bool value)
    {
        return value ? "true" : "false";
    }

    private static IEnumerable<string> ExtractCodexRuntimeConfig(IReadOnlyList<string> lines)
    {
        var keepSection = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                keepSection = trimmed.Equals("[model_providers]", StringComparison.Ordinal)
                    || trimmed.StartsWith("[model_providers.", StringComparison.Ordinal);
            }

            if (keepSection || ShouldKeepRootConfigLine(trimmed))
            {
                yield return line;
            }
        }
    }

    private static bool ShouldKeepRootConfigLine(string trimmedLine)
    {
        if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#", StringComparison.Ordinal))
        {
            return true;
        }

        if (trimmedLine.StartsWith("[", StringComparison.Ordinal))
        {
            return false;
        }

        var equalsIndex = trimmedLine.IndexOf('=');
        if (equalsIndex < 0)
        {
            return false;
        }

        var key = trimmedLine[..equalsIndex].Trim();
        return key is "model_provider"
            or "model"
            or "model_reasoning_effort"
            or "model_reasoning_summary"
            or "disable_response_storage";
    }

    private static void AddPoeStudioMcpOverrides(Collection<string> arguments, AgentSettingsDto settings)
    {
        if (!string.Equals(settings.McpServerName, "poe-studio", StringComparison.Ordinal))
        {
            return;
        }

        var repositoryRoot = ResolveRepositoryRoot(settings.WorkingDirectory);
        var mcpProjectPath = repositoryRoot is null
            ? null
            : Path.Combine(repositoryRoot, "src", "PoeStudio.Mcp", "PoeStudio.Mcp.csproj");
        var mcpExecutablePath = repositoryRoot is null
            ? null
            : Path.Combine(repositoryRoot, "src", "PoeStudio.Mcp", "bin", "Debug", "net8.0", "PoeStudio.Mcp.exe");
        var workspaceRoot = ResolvePoeStudioWorkspaceRoot();

        if (!string.IsNullOrWhiteSpace(mcpExecutablePath) && File.Exists(mcpExecutablePath))
        {
            arguments.Add("-c");
            arguments.Add($"mcp_servers.poe-studio.command={JsonSerializer.Serialize(mcpExecutablePath)}");
            arguments.Add("-c");
            arguments.Add($"mcp_servers.poe-studio.args=[{JsonSerializer.Serialize("--workspace-root")},{JsonSerializer.Serialize(workspaceRoot)}]");
            return;
        }

        if (!string.IsNullOrWhiteSpace(mcpProjectPath) && File.Exists(mcpProjectPath))
        {
            arguments.Add("-c");
            arguments.Add("mcp_servers.poe-studio.command=\"dotnet\"");
            arguments.Add("-c");
            arguments.Add($"mcp_servers.poe-studio.args=[{JsonSerializer.Serialize("run")},{JsonSerializer.Serialize("--project")},{JsonSerializer.Serialize(mcpProjectPath)},{JsonSerializer.Serialize("--")},{JsonSerializer.Serialize("--workspace-root")},{JsonSerializer.Serialize(workspaceRoot)}]");
        }
    }

    private static string? ResolveRepositoryRoot(string? workingDirectory)
    {
        foreach (var candidate in CandidateRepositoryRoots(workingDirectory))
        {
            var directory = new DirectoryInfo(candidate);
            while (directory is not null)
            {
                var projectPath = Path.Combine(directory.FullName, "src", "PoeStudio.Mcp", "PoeStudio.Mcp.csproj");
                if (File.Exists(projectPath))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateRepositoryRoots(string? workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            yield return workingDirectory;
        }

        yield return Environment.CurrentDirectory;
        yield return AppContext.BaseDirectory;
    }

    private static string ResolvePoeStudioWorkspaceRoot()
    {
        var configured = Environment.GetEnvironmentVariable("POE_STUDIO_WORKSPACE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PoeStudio");
    }

    private static string ResolveCodexExecutable(string codexPath)
    {
        if (!string.Equals(codexPath, "codex", StringComparison.OrdinalIgnoreCase))
        {
            return codexPath;
        }

        var appResourceExecutable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Codex",
            "resources",
            "codex.exe");
        return File.Exists(appResourceExecutable) ? appResourceExecutable : codexPath;
    }

    private static string? NormalizeApprovalMode(string? approvalMode)
    {
        if (string.IsNullOrWhiteSpace(approvalMode))
        {
            return null;
        }

        return approvalMode.Trim().ToLowerInvariant() switch
        {
            "manual" => "on-request",
            "on-request" => "on-request",
            "never" => "never",
            "untrusted" => "untrusted",
            "on-failure" => "on-failure",
            _ => null
        };
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

            if (IsIgnorableCodexStderr(line))
            {
                continue;
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

    private static bool IsIgnorableCodexStderr(string line)
    {
        return line.Contains("codex_core_plugins::remote::remote_installed_plugin_sync", StringComparison.Ordinal)
            || line.Contains("codex_core_plugins::startup_remote_sync", StringComparison.Ordinal)
            || line.Contains("codex_core_plugins::startup_sync:", StringComparison.Ordinal)
            || line.Contains("codex_core_plugins::manager: failed to warm featured plugin ids cache", StringComparison.Ordinal)
            || line.Contains("codex_core_plugins::loader: failed to load plugin", StringComparison.Ordinal)
            || line.Contains("codex_core::shell_snapshot: Failed to create shell snapshot", StringComparison.Ordinal)
            || line.Contains("codex_core::session::turn: stream disconnected - retrying sampling request", StringComparison.Ordinal);
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
