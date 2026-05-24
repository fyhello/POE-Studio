using System.Diagnostics;
using System.Text.Json;

namespace PoeStudio.Tests;

public sealed class McpProtocolTests
{
    [Fact]
    public async Task Initialize_returns_protocol_version_server_info_and_tools_capability()
    {
        using var server = await McpServerProcess.StartAsync();

        await server.SendAsync("""
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}
            """);

        using var response = JsonDocument.Parse(await server.ReadStdoutLineAsync());
        var root = response.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, root.GetProperty("id").GetInt32());

        var result = root.GetProperty("result");
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("protocolVersion").GetString()));
        Assert.Equal("poe-studio", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
    }

    [Fact]
    public async Task Initialized_notification_does_not_return_response()
    {
        using var server = await McpServerProcess.StartAsync();
        await server.InitializeAsync();

        await server.SendAsync("""{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""");

        Assert.Null(await server.TryReadStdoutLineAsync(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public async Task Unknown_method_returns_json_rpc_error_without_crashing()
    {
        using var server = await McpServerProcess.StartAsync();

        await server.SendAsync("""{"jsonrpc":"2.0","id":99,"method":"poe/unknown","params":{}}""");

        using var response = JsonDocument.Parse(await server.ReadStdoutLineAsync());
        var root = response.RootElement;
        Assert.Equal(99, root.GetProperty("id").GetInt32());
        Assert.Equal(-32601, root.GetProperty("error").GetProperty("code").GetInt32());
        Assert.False(server.Process.HasExited);
    }

    [Fact]
    public async Task Invalid_json_returns_error_and_diagnostics_stay_off_stdout()
    {
        using var server = await McpServerProcess.StartAsync();

        await server.SendAsync("{not valid json");

        var line = await server.ReadStdoutLineAsync();
        using var response = JsonDocument.Parse(line);
        Assert.Equal(-32700, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.False(line.Contains("Exception", StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrWhiteSpace(await server.ReadStderrLineAsync()));
    }

    private sealed class McpServerProcess : IDisposable
    {
        private readonly StreamWriter stdin;
        private readonly StreamReader stdout;
        private readonly StreamReader stderr;

        private McpServerProcess(Process process)
        {
            Process = process;
            stdin = process.StandardInput;
            stdout = process.StandardOutput;
            stderr = process.StandardError;
        }

        public Process Process { get; }

        public static async Task<McpServerProcess> StartAsync()
        {
            var root = FindRepositoryRoot();
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "run --project src\\PoeStudio.Mcp\\PoeStudio.Mcp.csproj -- --workspace-root \"" + root + "\"",
                WorkingDirectory = root,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start MCP test process.");
            await Task.Yield();
            return new McpServerProcess(process);
        }

        public async Task InitializeAsync()
        {
            await SendAsync("""
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}
                """);
            _ = await ReadStdoutLineAsync();
        }

        public async Task SendAsync(string json)
        {
            await stdin.WriteLineAsync(json);
            await stdin.FlushAsync();
        }

        public async Task<string> ReadStdoutLineAsync()
        {
            return await TryReadStdoutLineAsync(TimeSpan.FromSeconds(30))
                ?? throw new TimeoutException("MCP server did not write a stdout JSON-RPC response.");
        }

        public async Task<string?> TryReadStdoutLineAsync(TimeSpan timeout)
        {
            var readTask = stdout.ReadLineAsync();
            var completed = await Task.WhenAny(readTask, Task.Delay(timeout));
            return completed == readTask ? await readTask : null;
        }

        public async Task<string> ReadStderrLineAsync()
        {
            var readTask = stderr.ReadLineAsync();
            var completed = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(30)));
            return completed == readTask
                ? await readTask ?? string.Empty
                : throw new TimeoutException("MCP server did not write stderr diagnostics.");
        }

        public void Dispose()
        {
            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }

            Process.Dispose();
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

            throw new InvalidOperationException("Could not locate PoeStudio.sln from test output directory.");
        }
    }
}
