using System.Text.Json;
using System.Text.Json.Serialization;
using PoeStudio.Contracts;

namespace PoeStudio.Core.Agent;

public sealed class AgentTaskPlanParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public AgentTaskPlanDto Parse(string finalMessage)
    {
        var json = ExtractJson(finalMessage);

        try
        {
            return JsonSerializer.Deserialize<AgentTaskPlanDto>(json, JsonOptions)
                ?? throw new JsonException("planner_output_invalid");
        }
        catch (JsonException ex) when (!string.Equals(ex.Message, "planner_output_invalid", StringComparison.Ordinal))
        {
            throw new JsonException("planner_output_invalid", ex);
        }
    }

    private static string ExtractJson(string finalMessage)
    {
        if (string.IsNullOrWhiteSpace(finalMessage))
        {
            throw new JsonException("planner_output_invalid");
        }

        const string fence = "```";
        var searchStart = 0;
        while (searchStart < finalMessage.Length)
        {
            var fenceStart = finalMessage.IndexOf(fence, searchStart, StringComparison.Ordinal);
            if (fenceStart < 0)
            {
                break;
            }

            var lineEnd = finalMessage.IndexOf('\n', fenceStart + fence.Length);
            if (lineEnd < 0)
            {
                break;
            }

            var info = finalMessage[(fenceStart + fence.Length)..lineEnd].Trim();
            var contentStart = lineEnd + 1;
            var fenceEnd = finalMessage.IndexOf(fence, contentStart, StringComparison.Ordinal);
            if (fenceEnd < 0)
            {
                break;
            }

            if (string.Equals(info, "json", StringComparison.OrdinalIgnoreCase))
            {
                return finalMessage[contentStart..fenceEnd].Trim();
            }

            searchStart = fenceEnd + fence.Length;
        }

        return finalMessage.Trim();
    }
}
