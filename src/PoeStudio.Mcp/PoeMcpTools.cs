using System.Text.Json;
using System.Text.Json.Serialization;
using PoeStudio.Contracts;
using PoeStudio.Core.Workspace;
using PoeStudio.Storage.Profiles;
using PoeStudio.Storage.Resources;

namespace PoeStudio.Mcp;

public static class PoeMcpTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void RegisterAll(McpToolRegistry registry, PoeWorkspaceResolution workspace)
    {
        registry.Register(
            new McpToolDefinition(
                "poe_get_workspace",
                "Return POE Studio workspace root, resolution source, data directory, and current process directory.",
                ObjectSchema()),
            (_, _) => Task.FromResult(GetWorkspace(workspace)));

        registry.Register(
            new McpToolDefinition(
                "poe_list_profiles",
                "List POE Studio client profiles with ids, display names, client type, and workspace binding summary.",
                ObjectSchema()),
            (_, cancellationToken) => ListProfilesAsync(workspace, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_get_profile",
                "Return details for a single POE Studio client profile by profileId.",
                ObjectSchema(("profileId", "string"))),
            (arguments, cancellationToken) => GetProfileAsync(workspace, arguments, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_get_index_status",
                "Return resource index existence, resource count, index path, and last update details.",
                ObjectSchema(("profileId", "string"))),
            (arguments, cancellationToken) => GetIndexStatusAsync(workspace, arguments, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_search_resources",
                "Search indexed POE Studio resources by query and limit. Does not scan disk.",
                ObjectSchema(("profileId", "string"), ("query", "string"), ("limit", "integer"))),
            (arguments, cancellationToken) => SearchResourcesAsync(workspace, arguments, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_read_resource",
                "Read an indexed physical resource through the Stage 1 read-only boundary with maxBytes limits.",
                ObjectSchema(("profileId", "string"), ("resourcePath", "string"), ("maxBytes", "integer"))),
            (arguments, cancellationToken) => ReadResourceAsync(workspace, arguments, cancellationToken));

        RegisterPlaceholder(
            registry,
            "poe_datc64_extract_translatable_cells",
            "Extract translatable DATC64 or string-candidate cells through the Stage 1 read-only resource boundary.",
            ObjectSchema(("profileId", "string"), ("resourcePath", "string"), ("limit", "integer")));
    }

    private static McpToolResult GetWorkspace(PoeWorkspaceResolution workspace)
    {
        if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
        {
            return McpToolResult.Error(error);
        }

        return JsonSuccess(new
        {
            workspaceRoot,
            source = workspace.Source,
            dataDirectory = Path.Combine(workspaceRoot, ".poe-studio"),
            currentDirectory = Environment.CurrentDirectory
        });
    }

    private static async Task<McpToolResult> ListProfilesAsync(
        PoeWorkspaceResolution workspace,
        CancellationToken cancellationToken)
    {
        if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
        {
            return McpToolResult.Error(error);
        }

        var profiles = await new ProfileStore(workspaceRoot).ListAsync(cancellationToken);
        return JsonSuccess(new
        {
            workspaceRoot,
            profiles
        });
    }

    private static async Task<McpToolResult> GetProfileAsync(
        PoeWorkspaceResolution workspace,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
        {
            return McpToolResult.Error(error);
        }

        if (!TryGetString(arguments, "profileId", out var profileId))
        {
            return McpToolResult.Error("Argument 'profileId' is required.");
        }

        var profile = await new ProfileStore(workspaceRoot).GetAsync(profileId, cancellationToken);
        if (profile is null)
        {
            return McpToolResult.Error($"Profile '{profileId}' was not found.");
        }

        return JsonSuccess(new
        {
            profile
        });
    }

    private static async Task<McpToolResult> GetIndexStatusAsync(
        PoeWorkspaceResolution workspace,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
        {
            return McpToolResult.Error(error);
        }

        if (!TryGetString(arguments, "profileId", out var profileId))
        {
            return McpToolResult.Error("Argument 'profileId' is required.");
        }

        var profile = await new ProfileStore(workspaceRoot).GetAsync(profileId, cancellationToken);
        if (profile is null)
        {
            return McpToolResult.Error($"Profile '{profileId}' was not found.");
        }

        var layout = WorkspaceLayout.ForProfile(workspaceRoot, profileId);
        var indexRoot = Path.Combine(layout.CacheRoot, "index");
        var legacyIndexPath = Path.Combine(indexRoot, "resources.json");
        var shardRoot = Path.Combine(indexRoot, "resources-v2", "shards");
        var shardManifestPath = Path.Combine(shardRoot, "manifest.json");
        var status = await ReadIndexStatusAsync(profileId, indexRoot, legacyIndexPath, shardManifestPath, cancellationToken);

        return JsonSuccess(status);
    }

    private static async Task<McpToolResult> SearchResourcesAsync(
        PoeWorkspaceResolution workspace,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
        {
            return McpToolResult.Error(error);
        }

        if (!TryGetString(arguments, "profileId", out var profileId))
        {
            return McpToolResult.Error("Argument 'profileId' is required.");
        }

        var limit = GetInt32(arguments, "limit") ?? 20;
        if (limit is < 1 or > 100)
        {
            return McpToolResult.Error("Argument 'limit' must be between 1 and 100.");
        }

        var query = TryGetString(arguments, "query", out var queryValue) ? queryValue : null;
        var response = await new ResourceIndexStore(workspaceRoot)
            .SearchAsync(new ResourceSearchRequest(profileId, Query: query, Take: limit), cancellationToken);

        return JsonSuccess(new
        {
            profileId,
            query,
            limit,
            total = response.Total,
            items = response.Items
        });
    }

    private static async Task<McpToolResult> ReadResourceAsync(
        PoeWorkspaceResolution workspace,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
        {
            return McpToolResult.Error(error);
        }

        if (!TryGetString(arguments, "profileId", out var profileId))
        {
            return McpToolResult.Error("Argument 'profileId' is required.");
        }

        if (!TryGetString(arguments, "resourcePath", out var resourcePath))
        {
            return McpToolResult.Error("Argument 'resourcePath' is required.");
        }

        var maxBytes = GetInt32(arguments, "maxBytes") ?? PoeResourceContentReader.DefaultMaxBytes;
        var read = await new PoeResourceContentReader(new ResourceIndexStore(workspaceRoot))
            .ReadAsync(profileId, resourcePath, maxBytes, cancellationToken);

        if (read.IsError)
        {
            return McpToolResult.Error($"{read.ErrorCode}: {read.ErrorMessage}");
        }

        var isText = IsTextResource(read.Resource!, read.Bytes);
        return JsonSuccess(new
        {
            profileId,
            resourcePath = read.Resource!.NormalizedPath,
            physicalPath = read.PhysicalPath,
            kind = read.Resource.Kind.ToString(),
            size = read.Resource.Size,
            bytesRead = read.Bytes.Length,
            truncated = read.Truncated,
            encoding = isText ? "text" : "base64",
            text = isText ? System.Text.Encoding.UTF8.GetString(read.Bytes) : null,
            base64 = isText ? null : Convert.ToBase64String(read.Bytes),
            hexPreview = isText ? null : Convert.ToHexString(read.Bytes).ToLowerInvariant()
        });
    }

    private static async Task<object> ReadIndexStatusAsync(
        string profileId,
        string indexRoot,
        string legacyIndexPath,
        string shardManifestPath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(shardManifestPath))
        {
            await using var stream = File.OpenRead(shardManifestPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            return new
            {
                profileId,
                exists = true,
                format = "sharded",
                indexRoot,
                legacyIndexPath,
                shardManifestPath,
                resourceCount = GetInt32(root, "totalResources"),
                indexedAt = GetDateTimeOffset(root, "indexedAt"),
                warnings = GetStringArray(root, "warnings")
            };
        }

        if (File.Exists(legacyIndexPath))
        {
            await using var stream = File.OpenRead(legacyIndexPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var resourceCount = root.TryGetProperty("resources", out var resources) && resources.ValueKind == JsonValueKind.Array
                ? resources.GetArrayLength()
                : 0;
            return new
            {
                profileId,
                exists = true,
                format = "legacy",
                indexRoot,
                legacyIndexPath,
                shardManifestPath,
                resourceCount,
                indexedAt = GetDateTimeOffset(root, "indexedAt"),
                warnings = GetStringArray(root, "warnings")
            };
        }

        return new
        {
            profileId,
            exists = false,
            indexRoot,
            legacyIndexPath,
            shardManifestPath,
            hint = "Resource index is missing. Build the profile resource index in POE Studio before using indexed resource tools."
        };
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

    private static int? GetInt32(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var element) && element.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static bool IsTextResource(ResourceSummaryDto resource, byte[] bytes)
    {
        if (resource.Kind is ResourceKind.Text or ResourceKind.Ui)
        {
            return true;
        }

        return resource.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || resource.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || resource.Extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
            || (bytes.Length > 0 && !bytes.Contains((byte)0) && bytes.All(value => value is 9 or 10 or 13 or >= 32));
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var element) && element.TryGetDateTimeOffset(out var value)
            ? value
            : null;
    }

    private static string[] GetStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
    }

    private static void RegisterPlaceholder(
        McpToolRegistry registry,
        string name,
        string description,
        JsonElement inputSchema)
    {
        registry.Register(
            new McpToolDefinition(name, description, inputSchema),
            (_, _) => Task.FromResult(McpToolResult.Error($"Tool '{name}' is not implemented yet.")));
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

    private static McpToolResult JsonSuccess(object payload)
    {
        return McpToolResult.Success(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
