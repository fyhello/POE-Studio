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
        var payload = new
        {
            server,
            tool,
            arguments = TryGetRaw(item, "arguments"),
            status
        };
        return new CodexParsedEvent(
            rawJson,
            CodexParsedEventType.McpToolCall,
            $"{server}.{tool} {status}",
            JsonSerializer.Serialize(payload, JsonLineOptions),
            false,
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

    private static readonly JsonSerializerOptions JsonLineOptions = new(JsonSerializerDefaults.Web);
}
