using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PoeStudio.Contracts;
using PoeStudio.Core.Workspace;
using PoeStudio.Storage.Overlay;

namespace PoeStudio.Mcp;

public static class PoeMcpWriteTools
{
    private const long MaxWriteBytes = 50 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly McpToolAnnotations WriteAnnotations = new(ReadOnlyHint: false, OpenWorldHint: false);

    public static void RegisterAll(
        McpToolRegistry registry,
        PoeWorkspaceResolution workspace)
    {
        registry.Register(
            new McpToolDefinition(
                "poe_write_overlay_text",
                "Write a text overlay for a game resource to the staging area. User reviews and commits before changes affect game files.",
                ObjectSchema(("profileId", "string"), ("resourcePath", "string"), ("text", "string")),
                WriteAnnotations),
            (arguments, cancellationToken) => WriteOverlayTextAsync(workspace, arguments, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_write_overlay_binary",
                "Write a binary overlay for a game resource to the staging area. Provide content as base64. User reviews and commits before changes affect game files.",
                ObjectSchema(("profileId", "string"), ("resourcePath", "string"), ("base64", "string")),
                WriteAnnotations),
            (arguments, cancellationToken) => WriteOverlayBinaryAsync(workspace, arguments, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_list_overlays",
                "List pending overlay changes in the staging area for a profile.",
                ObjectSchema(("profileId", "string")),
                WriteAnnotations),
            (arguments, cancellationToken) => ListOverlaysAsync(workspace, arguments, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_revert_overlay",
                "Revert a pending overlay change in the staging area by resource path.",
                ObjectSchema(("profileId", "string"), ("resourcePath", "string")),
                WriteAnnotations),
            (arguments, cancellationToken) => RevertOverlayAsync(workspace, arguments, cancellationToken));
    }

    private static async Task<McpToolResult> WriteOverlayTextAsync(
        PoeWorkspaceResolution workspace,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
            return McpToolResult.Error(error);

        if (!TryGetString(arguments, "profileId", out var profileId))
            return McpToolResult.Error("Argument 'profileId' is required.");

        if (!TryGetString(arguments, "resourcePath", out var resourcePath))
            return McpToolResult.Error("Argument 'resourcePath' is required.");

        if (!TryGetString(arguments, "text", out var text))
            return McpToolResult.Error("Argument 'text' is required.");

        if (Encoding.UTF8.GetByteCount(text) > MaxWriteBytes)
            return McpToolResult.Error($"Argument 'text' exceeds maximum size of {MaxWriteBytes} bytes.");

        var store = new OverlayStore(workspaceRoot);
        var entry = await store.SaveTextAsync(
            new SaveTextOverlayRequest(profileId, resourcePath, text),
            cancellationToken);

        return McpToolResult.Success(JsonSerializer.Serialize(new
        {
            profileId,
            resourcePath,
            overlaySize = entry.OverlaySize,
            warning = "Overlay written to staging area. Use poe_list_overlays to review or POST /api/patch/build to commit."
        }, JsonOptions));
    }

    private static async Task<McpToolResult> WriteOverlayBinaryAsync(
        PoeWorkspaceResolution workspace,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
            return McpToolResult.Error(error);

        if (!TryGetString(arguments, "profileId", out var profileId))
            return McpToolResult.Error("Argument 'profileId' is required.");

        if (!TryGetString(arguments, "resourcePath", out var resourcePath))
            return McpToolResult.Error("Argument 'resourcePath' is required.");

        if (!TryGetString(arguments, "base64", out var base64))
            return McpToolResult.Error("Argument 'base64' is required.");

        byte[] content;
        try
        {
            content = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return McpToolResult.Error("Argument 'base64' is not valid base64 encoding.");
        }

        if (content.LongLength > MaxWriteBytes)
            return McpToolResult.Error($"Argument 'base64' decoded content exceeds maximum size of {MaxWriteBytes} bytes.");

        var store = new OverlayStore(workspaceRoot);
        var entry = await store.SaveBytesAsync(profileId, resourcePath, content, null, false, cancellationToken);

        return McpToolResult.Success(JsonSerializer.Serialize(new
        {
            profileId,
            resourcePath,
            overlaySize = entry.OverlaySize,
            warning = "Overlay written to staging area. Use poe_list_overlays to review or POST /api/patch/build to commit."
        }, JsonOptions));
    }

    private static async Task<McpToolResult> ListOverlaysAsync(
        PoeWorkspaceResolution workspace,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
            return McpToolResult.Error(error);

        if (!TryGetString(arguments, "profileId", out var profileId))
            return McpToolResult.Error("Argument 'profileId' is required.");

        var store = new OverlayStore(workspaceRoot);
        var list = await store.ListAsync(profileId, cancellationToken);

        return McpToolResult.Success(JsonSerializer.Serialize(list, JsonOptions));
    }

    private static async Task<McpToolResult> RevertOverlayAsync(
        PoeWorkspaceResolution workspace,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
            return McpToolResult.Error(error);

        if (!TryGetString(arguments, "profileId", out var profileId))
            return McpToolResult.Error("Argument 'profileId' is required.");

        if (!TryGetString(arguments, "resourcePath", out var resourcePath))
            return McpToolResult.Error("Argument 'resourcePath' is required.");

        var store = new OverlayStore(workspaceRoot);
        var result = await store.RevertAsync(new RevertOverlayRequest(profileId, resourcePath), cancellationToken);

        return McpToolResult.Success(JsonSerializer.Serialize(new
        {
            profileId,
            resourcePath,
            removed = result.Removed
        }, JsonOptions));
    }

    private static bool TryGetWorkspaceRoot(PoeWorkspaceResolution workspace, out string workspaceRoot, out string error)
    {
        if (workspace.Success && !string.IsNullOrWhiteSpace(workspace.WorkspaceRoot))
        {
            workspaceRoot = workspace.WorkspaceRoot;
            error = string.Empty;
            return true;
        }

        workspaceRoot = string.Empty;
        error = workspace.Error ?? "POE Studio workspace root is not configured.";
        return false;
    }

    private static bool TryGetString(JsonElement arguments, string name, out string value)
    {
        if (arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(element.GetString()))
        {
            value = element.GetString()!;
            return true;
        }

        value = string.Empty;
        return false;
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
                StringComparer.Ordinal),
            ["required"] = properties.Select(p => p.Name).ToArray()
        };

        return JsonSerializer.SerializeToElement(schema);
    }
}
