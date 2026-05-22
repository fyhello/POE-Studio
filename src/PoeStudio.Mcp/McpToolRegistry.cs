using System.Text.Json;

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

    public static McpToolRegistry CreateDefault(PoeWorkspaceResolution? workspace = null)
    {
        workspace ??= new PoeWorkspaceResolution(false, null, "unresolved", "POE Studio workspace root is not configured.");
        var registry = new McpToolRegistry();
        foreach (var definition in CreateDefaultDefinitions())
        {
            registry.Register(definition, (_, _) => Task.FromResult(McpToolResult.Error($"Tool '{definition.Name}' is not implemented yet.")));
        }

        registry.Register(
            new McpToolDefinition(
                "poe_get_workspace",
                "Return POE Studio workspace root, resolution source, data directory, and current process directory.",
                ObjectSchema()),
            (_, _) => Task.FromResult(CreateWorkspaceResult(workspace)));

        return registry;
    }

    private static IEnumerable<McpToolDefinition> CreateDefaultDefinitions()
    {
        yield return new McpToolDefinition(
            "poe_get_workspace",
            "Return POE Studio workspace root, resolution source, data directory, and current process directory.",
            ObjectSchema());
        yield return new McpToolDefinition(
            "poe_list_profiles",
            "List POE Studio client profiles with ids, display names, client type, and workspace binding summary.",
            ObjectSchema());
        yield return new McpToolDefinition(
            "poe_get_profile",
            "Return details for a single POE Studio client profile by profileId.",
            ObjectSchema(("profileId", "string")));
        yield return new McpToolDefinition(
            "poe_get_index_status",
            "Return resource index existence, resource count, index path, and last update details.",
            ObjectSchema(("profileId", "string")));
        yield return new McpToolDefinition(
            "poe_search_resources",
            "Search indexed POE Studio resources by query and limit. Does not scan disk.",
            ObjectSchema(("profileId", "string"), ("query", "string"), ("limit", "integer")));
        yield return new McpToolDefinition(
            "poe_read_resource",
            "Read an indexed physical resource through the Stage 1 read-only boundary with maxBytes limits.",
            ObjectSchema(("profileId", "string"), ("resourcePath", "string"), ("maxBytes", "integer")));
        yield return new McpToolDefinition(
            "poe_datc64_extract_translatable_cells",
            "Extract translatable DATC64 or string-candidate cells through the Stage 1 read-only resource boundary.",
            ObjectSchema(("profileId", "string"), ("resourcePath", "string"), ("limit", "integer")));
    }

    private static JsonElement ObjectSchema(params (string Name, string Type)[] properties)
    {
        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = properties.ToDictionary(
                property => property.Name,
                property => (object)new Dictionary<string, object?>
                {
                    ["type"] = property.Type
                },
                StringComparer.Ordinal)
        };

        return JsonSerializer.SerializeToElement(schema);
    }

    private static McpToolResult CreateWorkspaceResult(PoeWorkspaceResolution workspace)
    {
        if (!workspace.Success)
        {
            return McpToolResult.Error(workspace.Error ?? "POE Studio workspace root is not configured.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            workspaceRoot = workspace.WorkspaceRoot,
            source = workspace.Source,
            dataDirectory = Path.Combine(workspace.WorkspaceRoot!, ".poe-studio"),
            currentDirectory = Environment.CurrentDirectory
        });
        return McpToolResult.Success(payload);
    }

    private sealed record RegisteredTool(
        McpToolDefinition Definition,
        Func<JsonElement, CancellationToken, Task<McpToolResult>> Handler);
}

public sealed record McpToolDefinition(
    string Name,
    string Description,
    JsonElement InputSchema);

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
