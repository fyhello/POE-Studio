using System.Diagnostics;
using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;
using PoeStudio.Storage.Agent;

namespace PoeStudio.Api;

public sealed class AgentRepairService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICodexProcessRunner runner;
    private readonly AgentRunTraceStore traceStore;
    private readonly string repositoryRoot;
    private readonly string codexPath;
    private readonly string? model;
    private readonly string? profile;
    private readonly string mcpServerName;

    public AgentRepairService(
        ICodexProcessRunner runner,
        AgentRunTraceStore traceStore,
        string repositoryRoot,
        string codexPath,
        string? model,
        string? profile,
        string mcpServerName)
    {
        this.runner = runner;
        this.traceStore = traceStore;
        this.repositoryRoot = repositoryRoot;
        this.codexPath = codexPath;
        this.model = model;
        this.profile = profile;
        this.mcpServerName = mcpServerName;
    }

    public async Task<AgentRepairStartResultDto> StartRepairAsync(
        string runId,
        string diagnosticCode,
        bool userApproved,
        CancellationToken cancellationToken)
    {
        if (!userApproved)
        {
            return new AgentRepairStartResultDto(false, "User approval is required before starting code repair.", null);
        }

        var repairRunId = Guid.NewGuid().ToString("N");
        await traceStore.AppendAsync(
            repairRunId,
            new AgentRunTraceEventDto(
                "run",
                "queued",
                JsonSerializer.Serialize(new { runMode = AgentRunModes.Repair, sourceRunId = runId, diagnosticCode }, JsonOptions),
                DateTimeOffset.UtcNow),
            cancellationToken);

        _ = Task.Run(() => RunRepairInBackgroundAsync(repairRunId, runId, diagnosticCode, CancellationToken.None), CancellationToken.None);
        return new AgentRepairStartResultDto(true, "Repair run started.", repairRunId);
    }

    private async Task RunRepairInBackgroundAsync(
        string repairRunId,
        string sourceRunId,
        string diagnosticCode,
        CancellationToken cancellationToken)
    {
        try
        {
            var gitStatus = await RunProcessCaptureAsync("git", "status --short --branch", repositoryRoot, cancellationToken);
            await traceStore.AppendAsync(
                repairRunId,
                new AgentRunTraceEventDto(
                    "run",
                    "started",
                    JsonSerializer.Serialize(new { runMode = AgentRunModes.Repair, sourceRunId, diagnosticCode, gitStatus }, JsonOptions),
                    DateTimeOffset.UtcNow),
                cancellationToken);

            var repairSettings = new AgentSettingsDto(
                codexPath,
                model,
                profile,
                "workspace-write",
                mcpServerName,
                repositoryRoot,
                "never",
                OodlePath: null,
                Memories: false,
                Skills: false,
                CommandExecution: true);
            var prompt = BuildRepairPrompt(sourceRunId, diagnosticCode, gitStatus);
            var result = await runner.RunAsync(repairSettings, prompt, async parsedEvent =>
            {
                await traceStore.AppendAsync(
                    repairRunId,
                    new AgentRunTraceEventDto(
                        "codex_event",
                        parsedEvent.EventType.ToString(),
                        JsonSerializer.Serialize(new
                        {
                            parsedEvent.EventType,
                            parsedEvent.Message,
                            parsedEvent.ToolName,
                            parsedEvent.IsTerminal,
                            parsedEvent.PayloadJson
                        }, JsonOptions),
                        DateTimeOffset.UtcNow),
                    cancellationToken);
            }, cancellationToken);

            await traceStore.AppendAsync(
                repairRunId,
                new AgentRunTraceEventDto(
                    "run",
                    result.Failed ? "failed" : result.Cancelled ? "cancelled" : "completed",
                    JsonSerializer.Serialize(new { result.ExitCode, result.ErrorCode, result.StderrSummary }, JsonOptions),
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await traceStore.AppendAsync(
                repairRunId,
                new AgentRunTraceEventDto(
                    "run",
                    "failed",
                    JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions),
                    DateTimeOffset.UtcNow),
                CancellationToken.None);
        }
    }

    private static string BuildRepairPrompt(string runId, string diagnosticCode, string gitStatus)
    {
        return string.Join("\n", [
            $"The user approved code repair for POE Studio Agent run {runId}.",
            "You may inspect and edit project files inside repository root only.",
            "Run mode: repair.",
            "Codex capabilities for this run: memories disabled, skills disabled, command execution enabled, workspace-write sandbox.",
            "Git status command: git status --short --branch",
            "Git status before repair:",
            gitStatus,
            $"Diagnostic code: {diagnosticCode}",
            "Before editing:",
            "1. inspect run trace with poe_get_agent_run_trace",
            "2. inspect relevant code",
            "3. state the root cause in the chat",
            "Then:",
            "1. write failing test",
            "2. confirm it fails",
            "3. implement minimal fix",
            "4. run targeted tests",
            "5. run broader regression tests",
            "6. restart POE Studio if needed",
            "7. re-run or instruct user to re-run the original task",
            "Do not modify unrelated files.",
            "Do not use destructive git commands."
        ]);
    }

    private static async Task<string> RunProcessCaptureAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return string.IsNullOrWhiteSpace(stderr) ? stdout : stdout + Environment.NewLine + stderr;
    }
}
