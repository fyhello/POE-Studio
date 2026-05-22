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

            await HandleLineAsync(line, output, error, cancellationToken);
        }
    }

    public static async Task HandleLineAsync(
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
                await WriteResponseAsync(output, McpResponse.Success(request.Id, CreateToolsListResult()), cancellationToken);
                return;
            case "tools/call":
                await WriteResponseAsync(output, McpResponse.Success(request.Id, CreateToolNotImplementedResult(request)), cancellationToken);
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

    private static object CreateToolsListResult()
    {
        return new
        {
            tools = new[]
            {
                new
                {
                    name = "poe_get_workspace",
                    description = "Return POE Studio workspace resolution details. Placeholder registered for MCP lifecycle validation.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { }
                    }
                }
            }
        };
    }

    private static object CreateToolNotImplementedResult(McpRequest request)
    {
        var name = "<unknown>";
        if (request.Params.ValueKind == JsonValueKind.Object
            && request.Params.TryGetProperty("name", out var nameElement)
            && nameElement.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(nameElement.GetString()))
        {
            name = nameElement.GetString()!;
        }

        return new
        {
            content = new[]
            {
                new McpContent("text", $"Tool '{name}' is not implemented yet.")
            },
            isError = true
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
