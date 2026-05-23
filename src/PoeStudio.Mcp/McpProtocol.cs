using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoeStudio.Mcp;

public static class McpProtocol
{
    private const string ServerName = "poe-studio";
    private const string ServerTitle = "POE Studio MCP Tools";
    private const string ServerVersion = "0.1.0";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task RunAsync(
        TextReader input,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var workspace = new PoeWorkspaceResolver().Resolve(Environment.GetCommandLineArgs().Skip(1).ToArray(), Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => entry.Value?.ToString(), StringComparer.OrdinalIgnoreCase));
        var registry = McpToolRegistry.CreateDefault(workspace);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            await HandleLineAsync(registry, line, output, error, cancellationToken);
        }
    }

    public static async Task HandleLineAsync(
        McpToolRegistry registry,
        string line,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        McpRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<McpRequest>(line, JsonOptions);
        }
        catch (JsonException exception)
        {
            await error.WriteLineAsync($"Invalid JSON-RPC input: {exception.Message}");
            await WriteResponseAsync(output, McpResponse.Failure(null, -32700, "Parse error"), cancellationToken);
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Method))
        {
            await WriteResponseAsync(output, McpResponse.Failure(request?.Id, -32600, "Invalid Request"), cancellationToken);
            return;
        }

        switch (request.Method)
        {
            case "initialize":
                await WriteResponseAsync(output, McpResponse.Success(request.Id, CreateInitializeResult(request)), cancellationToken);
                return;
            case "notifications/initialized":
                return;
            case "tools/list":
                await WriteResponseAsync(output, McpResponse.Success(request.Id, CreateToolsListResult(registry)), cancellationToken);
                return;
            case "tools/call":
                await WriteResponseAsync(output, McpResponse.Success(request.Id, await CallToolAsync(registry, request, cancellationToken)), cancellationToken);
                return;
            default:
                await WriteResponseAsync(output, McpResponse.Failure(request.Id, -32601, $"Method not found: {request.Method}"), cancellationToken);
                return;
        }
    }

    public static async Task WriteResponseAsync(
        TextWriter output,
        McpResponse response,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);
        await output.WriteLineAsync(json.AsMemory(), cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static object CreateInitializeResult(McpRequest request)
    {
        var protocolVersion = "2024-11-05";
        if (request.Params.ValueKind == JsonValueKind.Object
            && request.Params.TryGetProperty("protocolVersion", out var version)
            && version.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(version.GetString()))
        {
            protocolVersion = version.GetString()!;
        }

        return new
        {
            protocolVersion,
            capabilities = new
            {
                tools = new { }
            },
            serverInfo = new
            {
                name = ServerName,
                title = ServerTitle,
                version = ServerVersion
            },
            instructions = "Read-only POE Studio project context tools."
        };
    }

    private static object CreateToolsListResult(McpToolRegistry registry)
    {
        return new
        {
            tools = registry.ListTools().Select(tool => new
            {
                name = tool.Name,
                description = tool.Description,
                inputSchema = tool.InputSchema,
                annotations = tool.Annotations
            }).ToArray()
        };
    }

    private static async Task<object> CallToolAsync(
        McpToolRegistry registry,
        McpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Params.ValueKind != JsonValueKind.Object
            || !request.Params.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(nameElement.GetString()))
        {
            return new
            {
                content = new[] { new McpContent("text", "Invalid params: tools/call requires name.") },
                isError = true
            };
        }

        var arguments = request.Params.TryGetProperty("arguments", out var argumentsElement)
            && argumentsElement.ValueKind == JsonValueKind.Object
                ? argumentsElement
                : JsonSerializer.SerializeToElement(new { });
        var result = await registry.CallToolAsync(nameElement.GetString()!, arguments, cancellationToken);
        return new
        {
            content = result.Content,
            isError = result.IsError
        };
    }

}

public sealed record McpRequest(
    [property: JsonPropertyName("jsonrpc")] string? JsonRpc,
    [property: JsonPropertyName("id")] JsonElement? Id,
    [property: JsonPropertyName("method")] string? Method,
    [property: JsonPropertyName("params")] JsonElement Params);

public sealed record McpResponse(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] JsonElement? Id,
    [property: JsonPropertyName("result")] object? Result,
    [property: JsonPropertyName("error")] McpError? Error)
{
    public static McpResponse Success(JsonElement? id, object result)
    {
        return new McpResponse("2.0", id, result, null);
    }

    public static McpResponse Failure(JsonElement? id, int code, string message)
    {
        return new McpResponse("2.0", id, null, new McpError(code, message));
    }
}

public sealed record McpError(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string Message);

public sealed record McpContent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);
