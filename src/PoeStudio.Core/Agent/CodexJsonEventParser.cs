using System.Text.Encodings.Web;
using System.Text.Json;

namespace PoeStudio.Core.Agent;

public sealed class CodexJsonEventParser
{
    public CodexParsedEvent ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return Unknown(line);
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = GetString(root, "type");
            if (!root.TryGetProperty("item", out var item))
            {
                return ParseRootEvent(line, root, type);
            }

            return GetString(item, "type") switch
            {
                "mcp_tool_call" => ParseMcpToolCall(line, item),
                "agent_message" => ParseAgentMessage(line, item, type),
                "command_execution" => ParseCommandExecution(line, item),
                _ => Unknown(line)
            };
        }
        catch (JsonException)
        {
            return Unknown(line);
        }
    }

    private static CodexParsedEvent ParseRootEvent(string rawJson, JsonElement root, string? type)
    {
        if (string.Equals(type, "turn.completed", StringComparison.Ordinal))
        {
            return new CodexParsedEvent(
                rawJson,
                CodexParsedEventType.FinalMessage,
                string.Empty,
                CompactJson(root),
                true,
                false,
                null);
        }

        if (string.Equals(type, "turn.failed", StringComparison.Ordinal))
        {
            var message = root.TryGetProperty("error", out var error)
                ? GetErrorMessage(error) ?? error.GetRawText()
                : "turn.failed";
            return new CodexParsedEvent(
                rawJson,
                CodexParsedEventType.Error,
                message,
                CompactJson(root),
                true,
                false,
                null);
        }

        if (string.Equals(type, "error", StringComparison.Ordinal))
        {
            var message = GetString(root, "message") ?? "Codex error";
            return new CodexParsedEvent(
                rawJson,
                CodexParsedEventType.Error,
                message,
                CompactJson(root),
                true,
                false,
                null);
        }

        if (string.Equals(type, "agent_message", StringComparison.Ordinal))
        {
            var message = GetString(root, "text") ?? GetString(root, "message") ?? string.Empty;
            return new CodexParsedEvent(
                rawJson,
                CodexParsedEventType.AgentMessage,
                message,
                CompactJson(root),
                false,
                false,
                null);
        }

        return Unknown(rawJson);
    }

    private static CodexParsedEvent ParseMcpToolCall(string rawJson, JsonElement item)
    {
        var server = GetString(item, "server") ?? "unknown-server";
        var tool = GetString(item, "tool") ?? "unknown-tool";
        var status = GetString(item, "status") ?? "unknown";
        var error = GetErrorMessage(item)
            ?? GetString(item, "message")
            ?? (IsFailedStatus(status) ? GetResultContentText(item) : null);
        var resultText = GetResultContentText(item);
        var payload = new
        {
            server,
            tool,
            arguments = TryGetRaw(item, "arguments"),
            status,
            error,
            resultText
        };
        var failed = IsFailedStatus(status)
            || !string.IsNullOrWhiteSpace(error);
        return new CodexParsedEvent(
            rawJson,
            failed ? CodexParsedEventType.Error : CodexParsedEventType.McpToolCall,
            string.IsNullOrWhiteSpace(error) ? $"{server}.{tool} {status}" : $"{server}.{tool} {status}: {error}",
            JsonSerializer.Serialize(payload, JsonLineOptions),
            failed,
            true,
            tool);
    }

    private static CodexParsedEvent ParseAgentMessage(string rawJson, JsonElement item, string? rootType)
    {
        var message = GetString(item, "text") ?? GetString(item, "message") ?? string.Empty;
        var isTerminal = string.Equals(rootType, "turn.completed", StringComparison.Ordinal)
            || string.Equals(GetString(item, "status"), "completed", StringComparison.Ordinal);
        return new CodexParsedEvent(
            rawJson,
            isTerminal ? CodexParsedEventType.FinalMessage : CodexParsedEventType.AgentMessage,
            message,
            CompactJson(item),
            isTerminal,
            false,
            null);
    }

    private static CodexParsedEvent ParseCommandExecution(string rawJson, JsonElement item)
    {
        var command = GetString(item, "command") ?? string.Empty;
        var status = GetString(item, "status") ?? "unknown";
        int? exitCode = item.TryGetProperty("exit_code", out var exitCodeElement)
            && exitCodeElement.ValueKind == JsonValueKind.Number
            ? exitCodeElement.GetInt32()
            : null;
        var payload = new
        {
            command,
            exitCode,
            status
        };
        return new CodexParsedEvent(
            rawJson,
            CodexParsedEventType.CommandExecution,
            string.IsNullOrWhiteSpace(command) ? $"Command {status}" : $"{command} {status}",
            JsonSerializer.Serialize(payload, JsonLineOptions),
            false,
            false,
            null);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? GetErrorMessage(JsonElement element)
    {
        if (!element.TryGetProperty("error", out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        if (property.ValueKind == JsonValueKind.Object)
        {
            return GetString(property, "message")
                ?? GetString(property, "error")
                ?? property.GetRawText();
        }

        return property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : property.GetRawText();
    }

    private static bool IsFailedStatus(string status)
    {
        return string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetResultContentText(JsonElement element)
    {
        if (!element.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var messages = content.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.Object ? GetString(item, "text") : null)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        return messages.Length == 0 ? null : string.Join(Environment.NewLine, messages);
    }

    private static string? TryGetRaw(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? property.GetRawText()
            : null;
    }

    private static string CompactJson(JsonElement element)
    {
        return JsonSerializer.Serialize(element, JsonLineOptions);
    }

    private static CodexParsedEvent Unknown(string line)
    {
        return new CodexParsedEvent(line, CodexParsedEventType.Unknown, line, line, false, false, null);
    }

    private static readonly JsonSerializerOptions JsonLineOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
