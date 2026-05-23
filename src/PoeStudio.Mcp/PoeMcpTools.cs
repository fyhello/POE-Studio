using System.Text.Json;
using System.Text.Json.Serialization;
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;
using PoeStudio.Core.Native;
using PoeStudio.Core.Oodle;
using PoeStudio.Core.Tables;
using PoeStudio.Core.Workspace;
using PoeStudio.Storage.Profiles;
using PoeStudio.Storage.Resources;

namespace PoeStudio.Mcp;

public static class PoeMcpTools
{
    private const int Datc64ExtractMaxBytes = 16 * 1024 * 1024;
    private static readonly McpToolAnnotations ReadOnlyAnnotations = new(true, false);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void RegisterAll(
        McpToolRegistry registry,
        PoeWorkspaceResolution workspace,
        NativeBundleResourceContentResolver? nativeContentResolver = null)
    {
        nativeContentResolver ??= new NativeBundleResourceContentResolver(new MissingOodleCodec());

        registry.Register(
            new McpToolDefinition(
                "poe_get_workspace",
                "Return POE Studio workspace root, resolution source, data directory, and current process directory.",
                ObjectSchema(),
                ReadOnlyAnnotations),
            (_, _) => Task.FromResult(GetWorkspace(workspace)));

        registry.Register(
            new McpToolDefinition(
                "poe_list_profiles",
                "List POE Studio client profiles with ids, display names, client type, and workspace binding summary.",
                ObjectSchema(),
                ReadOnlyAnnotations),
            (_, cancellationToken) => ListProfilesAsync(workspace, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_get_profile",
                "Return details for a single POE Studio client profile by profileId.",
                ObjectSchema(("profileId", "string")),
                ReadOnlyAnnotations),
            (arguments, cancellationToken) => GetProfileAsync(workspace, arguments, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_get_index_status",
                "Return resource index existence, resource count, index path, and last update details.",
                ObjectSchema(("profileId", "string")),
                ReadOnlyAnnotations),
            (arguments, cancellationToken) => GetIndexStatusAsync(workspace, arguments, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_search_resources",
                "Search indexed POE Studio resources by query and limit. Does not scan disk.",
                ObjectSchema(("profileId", "string"), ("query", "string"), ("limit", "integer")),
                ReadOnlyAnnotations),
            (arguments, cancellationToken) => SearchResourcesAsync(workspace, arguments, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_read_resource",
                "Read an indexed physical, native-bundles2://, or ggpk-bundles2:// resource through the Stage 1 read-only boundary with maxBytes limits.",
                ObjectSchema(("profileId", "string"), ("resourcePath", "string"), ("maxBytes", "integer"), ("oodlePath", "string")),
                ReadOnlyAnnotations),
            (arguments, cancellationToken) => ReadResourceAsync(workspace, nativeContentResolver, arguments, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_datc64_extract_translatable_cells",
                "Extract translatable DATC64 or string-candidate cells through the Stage 1 read-only resource boundary.",
                ObjectSchema(("profileId", "string"), ("resourcePath", "string"), ("limit", "integer"), ("oodlePath", "string")),
                ReadOnlyAnnotations),
            (arguments, cancellationToken) => ExtractDatc64TranslatableCellsAsync(workspace, nativeContentResolver, arguments, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_get_project_context",
                "Return summarized POE Studio project workflow context, source metadata, tool boundaries, risk boundaries, and unknowns.",
                ObjectSchema(("taskKind", "string"), ("goal", "string"), ("resourcePath", "string"), ("repositoryRoot", "string")),
                ReadOnlyAnnotations),
            (arguments, cancellationToken) => GetProjectContextAsync(arguments, cancellationToken));
    }

    private static async Task<McpToolResult> GetProjectContextAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var taskKind = TryGetString(arguments, "taskKind", out var taskKindValue) ? taskKindValue : "question";
        var goal = TryGetString(arguments, "goal", out var goalValue) ? goalValue : string.Empty;
        var resourcePath = TryGetString(arguments, "resourcePath", out var resourcePathValue) ? resourcePathValue : null;
        var repositoryRoot = TryGetString(arguments, "repositoryRoot", out var repositoryRootValue) ? repositoryRootValue : null;
        var resolver = new AgentRepositoryRootResolver(repositoryRoot);
        var context = await new AgentProjectContextService(resolver).BuildAsync(
            taskKind,
            goal,
            resourcePath,
            repositoryRoot,
            cancellationToken);

        return JsonSuccess(context);
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
        NativeBundleResourceContentResolver nativeContentResolver,
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
        var profile = await new ProfileStore(workspaceRoot).GetAsync(profileId, cancellationToken);
        if (profile is null)
        {
            return McpToolResult.Error($"Profile '{profileId}' was not found.");
        }

        var oodlePath = ResolveOodlePath(arguments, profile);
        var read = await new PoeResourceContentReader(
                new ResourceIndexStore(workspaceRoot),
                GetAllowedPhysicalRoots(profile),
                profile,
                nativeContentResolver)
            .ReadAsync(profileId, resourcePath, maxBytes, oodlePath, cancellationToken);

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

    private static async Task<McpToolResult> ExtractDatc64TranslatableCellsAsync(
        PoeWorkspaceResolution workspace,
        NativeBundleResourceContentResolver nativeContentResolver,
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

        var limit = GetInt32(arguments, "limit") ?? 100;
        if (limit is < 1 or > 1000)
        {
            return McpToolResult.Error("Argument 'limit' must be between 1 and 1000.");
        }

        var profile = await new ProfileStore(workspaceRoot).GetAsync(profileId, cancellationToken);
        if (profile is null)
        {
            return McpToolResult.Error($"Profile '{profileId}' was not found.");
        }

        var oodlePath = ResolveOodlePath(arguments, profile);
        var read = await new PoeResourceContentReader(
                new ResourceIndexStore(workspaceRoot),
                GetAllowedPhysicalRoots(profile),
                profile,
                nativeContentResolver)
            .ReadAsync(profileId, resourcePath, Datc64ExtractMaxBytes, oodlePath, Datc64ExtractMaxBytes, cancellationToken);
        if (read.IsError)
        {
            return McpToolResult.Error($"{read.ErrorCode}: {read.ErrorMessage}");
        }

        var resource = read.Resource!;
        if (resource.Kind != ResourceKind.Table || !resource.Extension.Equals(".datc64", StringComparison.OrdinalIgnoreCase))
        {
            return McpToolResult.Error("Resource must be a .datc64 table.");
        }

        var inspection = new TableInspector().Inspect(resource, read.Bytes, read.Bytes.Length);
        var skipped = 0;
        var cells = ExtractCells(inspection, limit, ref skipped);
        var warnings = inspection.Warnings.ToList();
        if (skipped > 0)
        {
            warnings.Add($"Skipped {skipped} non-translatable empty, numeric, path-like, or hash-like cells.");
        }

        return JsonSuccess(new
        {
            profileId,
            resourcePath = resource.NormalizedPath,
            format = inspection.Format,
            delimiter = inspection.Delimiter,
            cells,
            warnings
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

    private static IReadOnlyList<string> GetAllowedPhysicalRoots(ClientProfileDto profile)
    {
        var roots = new List<string>();
        AddRoot(roots, profile.RootPath);
        AddRoot(roots, profile.Bundles2Path);

        if (!string.IsNullOrWhiteSpace(profile.ContentGgpkPath))
        {
            AddRoot(roots, Path.GetDirectoryName(profile.ContentGgpkPath));
        }

        return roots;
    }

    private static void AddRoot(List<string> roots, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            roots.Add(path);
        }
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

    private static IReadOnlyList<object> ExtractCells(TableInspectResponse inspection, int limit, ref int skipped)
    {
        var cells = new List<object>();
        if (inspection.Rows.Count > 0)
        {
            var columns = inspection.Columns ?? [];
            foreach (var row in inspection.Rows)
            {
                for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
                {
                    if (cells.Count >= limit)
                    {
                        return cells;
                    }

                    var sourceText = row.Cells[columnIndex];
                    if (!IsTranslatableCandidate(sourceText))
                    {
                        skipped++;
                        continue;
                    }

                    var columnName = columnIndex < columns.Count ? columns[columnIndex] : $"column_{columnIndex}";
                    if (!IsTranslatableColumn(columnName))
                    {
                        skipped++;
                        continue;
                    }

                    cells.Add(new
                    {
                        rowIndex = Math.Max(0, row.RowNumber - 1),
                        columnIndex,
                        columnName,
                        sourceText,
                        textEncoding = inspection.TextEncoding,
                        locator = $"row:{row.RowNumber};column:{columnIndex};name:{columnName}"
                    });
                }
            }

            return cells;
        }

        foreach (var candidate in inspection.Strings ?? [])
        {
            if (cells.Count >= limit)
            {
                return cells;
            }

            if (!IsTranslatableCandidate(candidate.Value))
            {
                skipped++;
                continue;
            }

            cells.Add(new
            {
                rowIndex = 0,
                columnIndex = 0,
                columnName = "string_candidate",
                sourceText = candidate.Value,
                textEncoding = candidate.Encoding,
                offset = candidate.Offset,
                locator = $"offset:{candidate.Offset};length:{candidate.Length};encoding:{candidate.Encoding}"
            });
        }

        return cells;
    }

    private static bool IsTranslatableCandidate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.All(char.IsDigit))
        {
            return false;
        }

        if (trimmed.Length >= 16 && trimmed.All(Uri.IsHexDigit))
        {
            return false;
        }

        if (IsDatc64ReferenceSummary(trimmed))
        {
            return false;
        }

        return !trimmed.Contains("://", StringComparison.Ordinal)
            && !trimmed.Contains('\\', StringComparison.Ordinal)
            && !(trimmed.Contains('/', StringComparison.Ordinal) && trimmed.Contains('.', StringComparison.Ordinal));
    }

    private static bool IsTranslatableColumn(string columnName)
    {
        var name = columnName.Split(' ', 2)[0].Trim();
        return name.Equals("DisplayedName", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Description", StringComparison.OrdinalIgnoreCase)
            || name.Equals("WebsiteDescription", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Text", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Name", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("text_", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Name", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Text", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Description", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDatc64ReferenceSummary(string value)
    {
        if (!value.StartsWith("[", StringComparison.Ordinal))
        {
            return false;
        }

        var close = value.IndexOf(']');
        if (close <= 1 || close + 2 >= value.Length || value[close + 1] != ' ' || value[close + 2] != '@')
        {
            return false;
        }

        return value[1..close].All(char.IsDigit)
            && value[(close + 3)..].All(char.IsDigit);
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
            new McpToolDefinition(name, description, inputSchema, ReadOnlyAnnotations),
            (_, _) => Task.FromResult(McpToolResult.Error($"Tool '{name}' is not implemented yet.")));
    }

    private static string? ResolveOodlePath(JsonElement arguments, ClientProfileDto profile)
    {
        if (TryGetString(arguments, "oodlePath", out var explicitOodlePath))
        {
            return explicitOodlePath;
        }

        var environmentOodlePath = Environment.GetEnvironmentVariable("POE_STUDIO_OODLE_PATH");
        if (!string.IsNullOrWhiteSpace(environmentOodlePath))
        {
            return environmentOodlePath;
        }

        var detected = OodleDetector.Detect(profile.RootPath);
        return detected.Status == OodleStatus.Found ? detected.Path : null;
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
