using System.Text.Json;
using PoeStudio.Contracts;

namespace PoeStudio.Api;

public static class AgentDiagnosticsService
{
    public static AgentDiagnosticFindingDto Analyze(
        string runId,
        IReadOnlyList<AgentRunTraceEventDto> events)
    {
        return Analyze(runId, events, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(30));
    }

    public static AgentDiagnosticFindingDto Analyze(
        string runId,
        IReadOnlyList<AgentRunTraceEventDto> events,
        DateTimeOffset now,
        TimeSpan toolHangThreshold)
    {
        var runMode = DetectRunMode(events);
        if (!string.Equals(runMode, AgentRunModes.Normal, StringComparison.Ordinal))
        {
            return NoFinding(runId, runMode);
        }

        var hasCompletedTool = events.Any(IsCompletedToolCall);
        var latestCompletedTool = events.LastOrDefault(IsCompletedToolCall);
        var hasAssistantMessageAfterTool = HasSubstantiveAssistantMessageAfterLastCompletedTool(events);
        var hasNextToolAfterCompletedTool = HasToolCallAfter(events, latestCompletedTool);
        if (hasCompletedTool
            && !hasAssistantMessageAfterTool
            && !hasNextToolAfterCompletedTool)
        {
            return new AgentDiagnosticFindingDto(
                runId,
                "no_final_answer_after_tool_result",
                "high",
                "MCP tool completed but Codex did not produce a user-facing final answer.",
                true,
                Evidence(events),
                runMode);
        }

        var latestOpenTool = events.LastOrDefault(IsInProgressToolCall);
        var hasOpenTool = latestOpenTool is not null
            && now - latestOpenTool.CreatedAt >= toolHangThreshold
            && !events.Any(IsClosedToolCall);
        if (hasOpenTool)
        {
            return new AgentDiagnosticFindingDto(
                runId,
                "tool_call_left_in_progress",
                "high",
                "A tool call started but no completed/failed event was observed after the hang threshold.",
                true,
                Evidence(events),
                runMode);
        }

        return NoFinding(runId, runMode);
    }

    private static AgentDiagnosticFindingDto NoFinding(string runId, string runMode)
    {
        return new AgentDiagnosticFindingDto(
            runId,
            "none",
            "info",
            "No agent run anomaly detected.",
            false,
            [],
            runMode);
    }

    private static bool HasSubstantiveAssistantMessageAfterLastCompletedTool(IReadOnlyList<AgentRunTraceEventDto> events)
    {
        var lastCompletedToolIndex = -1;
        for (var i = 0; i < events.Count; i++)
        {
            if (IsCompletedToolCall(events[i]))
            {
                lastCompletedToolIndex = i;
            }
        }

        return lastCompletedToolIndex >= 0
            && events.Skip(lastCompletedToolIndex + 1).Any(evt =>
                evt.EventName == "message"
                && IsSubstantiveAssistantMessage(evt));
    }

    private static bool HasToolCallAfter(IReadOnlyList<AgentRunTraceEventDto> events, AgentRunTraceEventDto? marker)
    {
        if (marker is null)
        {
            return false;
        }

        var markerIndex = -1;
        for (var i = 0; i < events.Count; i++)
        {
            if (ReferenceEquals(events[i], marker) || events[i].Equals(marker))
            {
                markerIndex = i;
                break;
            }
        }

        return markerIndex >= 0 && events.Skip(markerIndex + 1).Any(evt => evt.EventName == "tool_call");
    }

    private static bool IsSubstantiveAssistantMessage(AgentRunTraceEventDto evt)
    {
        try
        {
            using var doc = JsonDocument.Parse(evt.DataJson);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeProperty) && typeProperty.ValueKind == JsonValueKind.String
                ? typeProperty.GetString()
                : null;
            if (!string.Equals(type, "agent_message", StringComparison.Ordinal)
                && !string.Equals(type, "final_message", StringComparison.Ordinal))
            {
                return false;
            }

            var text = root.TryGetProperty("text", out var textProperty) && textProperty.ValueKind == JsonValueKind.String
                ? textProperty.GetString() ?? string.Empty
                : string.Empty;
            return !IsPlaceholderThinkingMessage(text);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsPlaceholderThinkingMessage(string text)
    {
        var normalized = text.Trim().TrimEnd('.', '。', '…');
        return normalized.Length == 0
            || string.Equals(normalized, "思考中", StringComparison.Ordinal)
            || string.Equals(normalized, "Thinking", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Thinking...", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompletedToolCall(AgentRunTraceEventDto evt)
    {
        return evt.EventName == "tool_call"
            && evt.DataJson.Contains("\"status\":\"completed\"", StringComparison.Ordinal);
    }

    private static bool IsInProgressToolCall(AgentRunTraceEventDto evt)
    {
        return evt.EventName == "tool_call"
            && evt.DataJson.Contains("\"status\":\"in_progress\"", StringComparison.Ordinal);
    }

    private static bool IsClosedToolCall(AgentRunTraceEventDto evt)
    {
        return evt.EventName == "tool_call"
            && (evt.DataJson.Contains("\"status\":\"completed\"", StringComparison.Ordinal)
                || evt.DataJson.Contains("\"status\":\"failed\"", StringComparison.Ordinal));
    }

    private static string DetectRunMode(IReadOnlyList<AgentRunTraceEventDto> events)
    {
        var run = events.FirstOrDefault(evt => evt.EventName == "run");
        if (run is null)
        {
            return AgentRunModes.Normal;
        }

        try
        {
            using var doc = JsonDocument.Parse(run.DataJson);
            return doc.RootElement.TryGetProperty("runMode", out var mode) && mode.ValueKind == JsonValueKind.String
                ? mode.GetString() ?? AgentRunModes.Normal
                : AgentRunModes.Normal;
        }
        catch (JsonException)
        {
            return AgentRunModes.Normal;
        }
    }

    private static IReadOnlyList<string> Evidence(IReadOnlyList<AgentRunTraceEventDto> events)
    {
        return events.Select(evt => $"{evt.EventName}:{evt.DataJson}").Take(10).ToArray();
    }
}
