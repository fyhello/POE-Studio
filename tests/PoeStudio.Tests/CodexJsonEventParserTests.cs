using System.Text.Json;
using PoeStudio.Core.Agent;

namespace PoeStudio.Tests;

public sealed class CodexJsonEventParserTests
{
    private readonly CodexJsonEventParser _parser = new();

    [Fact]
    public void ParseLine_extracts_mcp_tool_call()
    {
        var parsed = _parser.ParseLine("""
            {"type":"item.started","item":{"type":"mcp_tool_call","server":"poe-studio","tool":"poe_list_profiles","arguments":{"limit":5},"status":"in_progress"}}
            """);

        Assert.Equal(CodexParsedEventType.McpToolCall, parsed.EventType);
        Assert.True(parsed.IsToolCall);
        Assert.Equal("poe_list_profiles", parsed.ToolName);
        Assert.Contains("poe-studio", parsed.Message);
        using var payload = JsonDocument.Parse(parsed.PayloadJson!);
        Assert.Equal("poe-studio", payload.RootElement.GetProperty("server").GetString());
        Assert.Equal("in_progress", payload.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public void ParseLine_marks_failed_mcp_tool_call_as_error(string status)
    {
        var parsed = _parser.ParseLine(
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"mcp_tool_call\",\"server\":\"poe-studio\",\"tool\":\"poe_get_workspace\",\"status\":\""
            + status
            + "\",\"error\":\"user cancelled MCP tool call\"}}");

        Assert.Equal(CodexParsedEventType.Error, parsed.EventType);
        Assert.True(parsed.IsToolCall);
        Assert.Equal("poe_get_workspace", parsed.ToolName);
        Assert.Contains(status, parsed.Message);
        Assert.Contains("user cancelled", parsed.Message);
    }

    [Fact]
    public void ParseLine_extracts_mcp_error_message_from_error_object()
    {
        var parsed = _parser.ParseLine("""
            {"type":"item.completed","item":{"type":"mcp_tool_call","server":"poe-studio","tool":"poe_get_workspace","arguments":{},"result":null,"error":{"message":"user cancelled MCP tool call"},"status":"failed"}}
            """);

        Assert.Equal(CodexParsedEventType.Error, parsed.EventType);
        Assert.Contains("user cancelled MCP tool call", parsed.Message);
        using var payload = JsonDocument.Parse(parsed.PayloadJson!);
        Assert.Equal("user cancelled MCP tool call", payload.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void ParseLine_extracts_mcp_failure_message_from_result_content()
    {
        var parsed = _parser.ParseLine("""
            {"type":"item.completed","item":{"type":"mcp_tool_call","server":"poe-studio","tool":"poe_datc64_extract_translatable_cells","arguments":{"limit":5},"result":{"content":[{"type":"text","text":"native_resource_not_supported_in_stage1: Native Bundles2 or non-physical resources are not supported by Stage 1 MCP read tools."}],"structured_content":null},"error":null,"status":"failed"}}
            """);

        Assert.Equal(CodexParsedEventType.Error, parsed.EventType);
        Assert.Contains("native_resource_not_supported_in_stage1", parsed.Message);
        using var payload = JsonDocument.Parse(parsed.PayloadJson!);
        Assert.Contains("native_resource_not_supported_in_stage1", payload.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void ParseLine_extracts_completed_mcp_result_content()
    {
        var parsed = _parser.ParseLine("""
            {"type":"item.completed","item":{"type":"mcp_tool_call","server":"poe-studio","tool":"poe_find_current_table_untranslated_cells","arguments":{"limit":3},"result":{"content":[{"type":"text","text":"{\"candidates\":3,\"items\":[{\"rowNumber\":1,\"sourceText\":\"火球\",\"targetText\":\"\"}]}"}]},"status":"completed"}}
            """);

        Assert.Equal(CodexParsedEventType.McpToolCall, parsed.EventType);
        Assert.Contains("火球", parsed.PayloadJson);
        using var payload = JsonDocument.Parse(parsed.PayloadJson!);
        Assert.Contains("\"candidates\":3", payload.RootElement.GetProperty("resultText").GetString());
    }

    [Fact]
    public void ParseLine_extracts_agent_message()
    {
        var parsed = _parser.ParseLine("""
            {"type":"item.completed","item":{"type":"agent_message","text":"done"}}
            """);

        Assert.Equal(CodexParsedEventType.AgentMessage, parsed.EventType);
        Assert.Equal("done", parsed.Message);
        Assert.False(parsed.IsToolCall);
    }

    [Fact]
    public void ParseLine_marks_turn_completed_as_terminal_success()
    {
        var parsed = _parser.ParseLine("""
            {"type":"turn.completed","usage":{"input_tokens":10,"output_tokens":2}}
            """);

        Assert.Equal(CodexParsedEventType.FinalMessage, parsed.EventType);
        Assert.True(parsed.IsTerminal);
        Assert.Equal(string.Empty, parsed.Message);
    }

    [Fact]
    public void ParseLine_extracts_command_execution()
    {
        var parsed = _parser.ParseLine("""
            {"type":"item.completed","item":{"type":"command_execution","command":"dotnet test","exit_code":0,"status":"completed"}}
            """);

        Assert.Equal(CodexParsedEventType.CommandExecution, parsed.EventType);
        Assert.Contains("dotnet test", parsed.Message);
        using var payload = JsonDocument.Parse(parsed.PayloadJson!);
        Assert.Equal(0, payload.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void ParseLine_returns_unknown_for_invalid_json_and_preserves_raw_line()
    {
        var parsed = _parser.ParseLine("{not-json");

        Assert.Equal(CodexParsedEventType.Unknown, parsed.EventType);
        Assert.Equal("{not-json", parsed.RawJson);
        Assert.Equal("{not-json", parsed.Message);
    }
}
