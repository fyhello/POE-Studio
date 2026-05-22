using System.Diagnostics;
using System.Text.Json;

namespace PoeStudio.Tests;

public sealed class McpProcessSmokeTests
{
    [Fact]
    public async Task Stdio_process_initializes_and_lists_tools()
    {
        var workspaceRoot = CreateTempDirectory();
        var repoRoot = FindRepoRoot();
        using var process = StartMcpProcess(repoRoot, workspaceRoot);

        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""");
        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");
        process.StandardInput.Close();

        var firstLine = await ReadRequiredLineAsync(process.StandardOutput, process);
        var secondLine = await ReadRequiredLineAsync(process.StandardOutput, process);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(0, process.ExitCode);
        using var initialize = JsonDocument.Parse(firstLine);
        using var tools = JsonDocument.Parse(secondLine);
        Assert.Equal(1, initialize.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(2, tools.RootElement.GetProperty("id").GetInt32());
        var toolNames = tools.RootElement
            .GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("poe_datc64_extract_translatable_cells", toolNames);
    }

    private static Process StartMcpProcess(string repoRoot, string workspaceRoot)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repoRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(Path.Combine(repoRoot, "src", "PoeStudio.Mcp", "PoeStudio.Mcp.csproj"));
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--workspace-root");
        startInfo.ArgumentList.Add(workspaceRoot);

        var process = Process.Start(startInfo);
        Assert.NotNull(process);
        return process;
    }

    private static async Task<string> ReadRequiredLineAsync(StreamReader reader, Process process)
    {
        var lineTask = reader.ReadLineAsync();
        var completed = await Task.WhenAny(lineTask, Task.Delay(TimeSpan.FromSeconds(20)));
        if (completed == lineTask && lineTask.Result is { } line)
        {
            return line;
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        var error = await process.StandardError.ReadToEndAsync();
        throw new TimeoutException($"MCP process did not write an expected stdout line. stderr: {error}");
    }

    private static string FindRepoRoot()
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

        throw new InvalidOperationException("Could not locate PoeStudio.sln.");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "poe-mcp-process-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
