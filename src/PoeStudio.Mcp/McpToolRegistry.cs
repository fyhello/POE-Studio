using System.Text.Json;
using PoeStudio.Core.Native;

namespace PoeStudio.Mcp;

public sealed class McpToolRegistry
{
    private readonly Dictionary<string, RegisteredTool> tools = new(StringComparer.Ordinal);

    public void Register(
        McpToolDefinition definition,
        Func<JsonElement, CancellationToken, Task<McpToolResult>> handler)
    {
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new ArgumentException("Tool name is required.", nameof(definition));
        }

        tools[definition.Name] = new RegisteredTool(definition, handler);
    }

    public IReadOnlyList<McpToolDefinition> ListTools()
    {
        return tools.Values.Select(tool => tool.Definition).ToArray();
    }

    public async Task<McpToolResult> CallToolAsync(
        string name,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!tools.TryGetValue(name, out var tool))
        {
            return McpToolResult.Error($"Unknown tool: {name}");
        }

        try
        {
            return await tool.Handler(arguments, cancellationToken);
        }
        catch (Exception exception)
        {
            return McpToolResult.Error($"Tool '{name}' failed: {exception.Message}");
        }
    }

    public static McpToolRegistry CreateDefault(
        PoeWorkspaceResolution? workspace = null,
        NativeBundleResourceContentResolver? nativeContentResolver = null)
    {
        workspace ??= new PoeWorkspaceResolution(false, null, "unresolved", "POE Studio workspace root is not configured.");
        var registry = new McpToolRegistry();
        PoeMcpTools.RegisterAll(registry, workspace, nativeContentResolver);
        return registry;
    }

    private sealed record RegisteredTool(
        McpToolDefinition Definition,
        Func<JsonElement, CancellationToken, Task<McpToolResult>> Handler);
}

public sealed record McpToolDefinition(
    string Name,
    string Description,
    JsonElement InputSchema,
    McpToolAnnotations? Annotations = null);

public sealed record McpToolAnnotations(
    bool ReadOnlyHint,
    bool OpenWorldHint);

public sealed record McpToolResult(
    IReadOnlyList<McpContent> Content,
    bool IsError)
{
    public static McpToolResult Success(string text)
    {
        return new McpToolResult([new McpContent("text", text)], false);
    }

    public static McpToolResult Error(string message)
    {
        return new McpToolResult([new McpContent("text", message)], true);
    }
}
