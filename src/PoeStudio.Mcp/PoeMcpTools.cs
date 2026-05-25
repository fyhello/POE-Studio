using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;
using PoeStudio.Core.Native;
using PoeStudio.Core.Oodle;
using PoeStudio.Core.Tables;
using PoeStudio.Core.Workspace;
using PoeStudio.Storage.Agent;
using PoeStudio.Storage.Profiles;
using PoeStudio.Storage.Resources;

namespace PoeStudio.Mcp;

public static class PoeMcpTools
{
    private const int Datc64ExtractMaxBytes = 16 * 1024 * 1024;
    private const int Datc64ExtractMaxCells = 100;
    private const int McpProjectSummaryMaxLength = 1000;
    private const int McpProjectSectionMaxLength = 300;
    private const int McpProjectGuidanceMaxLength = 240;
    private static readonly McpToolAnnotations ReadOnlyAnnotations = new(true, false);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void RegisterAll(
        McpToolRegistry registry,
        PoeWorkspaceResolution workspace,
        NativeBundleResourceContentResolver? nativeContentResolver = null)
    {
        nativeContentResolver ??= new NativeBundleResourceContentResolver(new MissingOodleCodec());

        registry.Register(
            new McpToolDefinition(
                "poe_get_project_overview",
                "Return POE Studio project overview, domain terminology, and tool guidance for Path of Exile 2 modding. CODEX agents should call this first to understand the project domain before processing user requests.",
                ObjectSchema(),
                ReadOnlyAnnotations),
            (_, _) => Task.FromResult(GetProjectOverview(workspace)));

        registry.Register(
            new McpToolDefinition(
                "poe_get_project_knowledge",
                "Read selected POE Studio Agent project knowledge sections by sectionId. Use this after poe_get_project_overview when a task needs workflow-specific project semantics. Does not read game resources.",
                ObjectSchema(("sectionIds", "array"), ("maxBytes", "integer")),
                ReadOnlyAnnotations),
            (arguments, cancellationToken) => GetProjectKnowledgeAsync(workspace, arguments, cancellationToken));

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
                "Extract up to 100 translatable DATC64 or string-candidate cells through the Stage 1 read-only resource boundary.",
                ObjectSchema(("profileId", "string"), ("resourcePath", "string"), ("limit", "integer"), ("oodlePath", "string")),
                ReadOnlyAnnotations),
            (arguments, cancellationToken) => ExtractDatc64TranslatableCellsAsync(workspace, nativeContentResolver, arguments, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_get_current_view_context",
                "Read the current UI view snapshot provided by POE Studio chat. Use this before reading raw resources when the user refers to the current table, current draft, or current comparison. Does not read Native/GGPK bundles and does not require Oodle.",
                ObjectSchema(("contextId", "string")),
                ReadOnlyAnnotations),
            (arguments, cancellationToken) => GetCurrentViewContextAsync(workspace, arguments, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_find_current_table_untranslated_cells",
                "Find likely missing translations from the current UI table comparison snapshot. Uses already-opened target/source rows; does not read raw resources, Native/GGPK bundles, or Oodle.",
                ObjectSchema(("contextId", "string"), ("limit", "integer")),
                ReadOnlyAnnotations),
            (arguments, cancellationToken) => FindCurrentTableUntranslatedCellsAsync(workspace, arguments, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_find_current_table_non_simplified_chinese_cells",
                "Find editable target cells in the current UI table comparison that still contain Traditional Chinese or mixed text when the user wants Simplified Chinese. Source/current source is only the reference table; target/current target is the editable table. Does not read raw resources, Native/GGPK bundles, or Oodle.",
                ObjectSchema(("contextId", "string"), ("limit", "integer")),
                ReadOnlyAnnotations),
            (arguments, cancellationToken) => FindCurrentTableNonSimplifiedChineseCellsAsync(workspace, arguments, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_get_agent_run_trace",
                "Read a POE Studio Agent run trace by runId. Use this to diagnose why a prior chat/tool run failed or produced no final answer.",
                ObjectSchema(("runId", "string")),
                ReadOnlyAnnotations),
            (arguments, cancellationToken) => GetAgentRunTraceAsync(workspace, arguments, cancellationToken));

        registry.Register(
            new McpToolDefinition(
                "poe_get_agent_recent_logs",
                "Read recent POE Studio API/MCP log summaries for diagnosing agent bridge failures. Returns bounded text only.",
                ObjectSchema(("maxLines", "integer")),
                ReadOnlyAnnotations),
            (arguments, cancellationToken) => GetAgentRecentLogsAsync(workspace, arguments, cancellationToken));

        // poe_get_project_context removed — CODEX plans autonomously via MCP tool discovery

        PoeMcpWriteTools.RegisterAll(registry, workspace);
    }

    // GetProjectContextAsync removed — CODEX plans autonomously via MCP tool discovery

    private static McpToolResult GetProjectOverview(PoeWorkspaceResolution workspace)
    {
        TryGetWorkspaceRoot(workspace, out var workspaceRoot, out _);
        var knowledgeRoot = ResolveKnowledgeRoot(workspaceRoot);
        var knowledgeIndex = ReadKnowledgeIndexSummary(knowledgeRoot);

        return JsonSuccess(new
        {
            projectName = "POE Studio",
            projectDescription = "Path of Exile 2 game file modding tool. Manages game resource editing through overlay staging with user review.",
            game = "Path of Exile 2 (PoE2)",
            domainTerminology = new
            {
                table = "DATC64 (.datc64) binary data tables — structured game configuration data (e.g. Stats.datc64, BaseItemTypes.datc64). Analogous to spreadsheets but in binary format.",
                resource = "Any game file: DATC64 table, texture (.dds), audio, or other asset inside the game's bundle archives.",
                profile = "A client profile configuring which PoE 2 installation to work with, including root path and bundle paths.",
                overlay = "A staged file change that the user reviews before committing to a patch build.",
                patch = "A deployable mod package built from committed overlays."
            },
            domainConcepts = new
            {
                profilePairing = "POE Studio can compare two profiles (e.g. source=Simplified Chinese, target=Traditional Chinese) for translation workflows. Each profile points to a different game client installation.",
                overlayDraftPatch = "Three-stage workflow: (1) write changes to overlay staging via poe_write_overlay_*, (2) user reviews staged changes in POE Studio UI, (3) user explicitly commits changes to a patch build via /api/patch/build.",
                readAwareness = "poe_read_resource reads the base game file from disk/bundles. It does NOT include uncommitted overlay changes. To edit: read base file, modify content, then write back as a new overlay."
            },
            toolGuidance = new
            {
                poe_get_current_view_context = "Reads the short-lived current UI view snapshot by currentViewContextId. Use this when the user refers to the current table, current draft, opened table, or current comparison.",
                poe_find_current_table_untranslated_cells = "Finds likely missing translations from the current UI table comparison snapshot. If currentViewContextId is present, use this first for current-table missing-translation checks. It does not read raw bundles and does not require Oodle.",
                poe_find_current_table_non_simplified_chinese_cells = "Finds editable target cells that still contain Traditional Chinese or mixed text when the user asks which current-table target cells are not converted to Simplified Chinese. Treat source/current source as reference and target/current target as editable overlay target; do not infer output language from resource path names.",
                poe_datc64_extract_translatable_cells = "Extracts up to 100 cells from a raw/base DATC64 table resource. Use only when currentViewContextId is absent or the user explicitly asks to reread original/raw resources; it may read Native/GGPK bundles and may require Oodle."
            },
            knowledgeRuntime = new
            {
                coreContract = "Always use the short core contract; read workflow details through poe_get_project_knowledge.",
                tool = "poe_get_project_knowledge",
                knowledgeIndex
            },
            limits = new
            {
                maxSearchResults = 100,
                maxExtractCells = 100,
                maxReadBytes = 16 * 1024 * 1024,
                maxWriteBytes = 50 * 1024 * 1024
            },
            stagingNotice = "All poe_write_overlay_* calls write to overlay staging. Changes are NOT applied to game files until the user commits them through the POE Studio patch build workflow (/api/patch/build).",
            commonWorkflows = new[]
            {
                "Check game resources: Search then read a resource by path through poe_search_resources + poe_read_resource.",
                "Find untranslated cells in the current table: If currentViewContextId is available, you must call poe_find_current_table_untranslated_cells before any raw resource tool.",
                "Find target cells not converted to Simplified Chinese: If currentViewContextId is available, call poe_find_current_table_non_simplified_chinese_cells. The source table is a reference; the target table is the editable/write target even if its resource path says traditional chinese.",
                "Find untranslated cells from raw resources: Use poe_datc64_extract_translatable_cells only when the user explicitly asks to reread original/raw files or no currentViewContextId is available.",
                "Edit game data: Read a resource, then write changes via poe_write_overlay_text/poe_write_overlay_binary. Writes go to staging — user commits them later.",
                "Review changes: Use poe_list_overlays to see staged changes, poe_revert_overlay to discard."
            },
            workspaceRoot
        });
    }

    private static async Task<McpToolResult> GetProjectKnowledgeAsync(
        PoeWorkspaceResolution workspace,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var sectionIds = GetStringArray(arguments, "sectionIds");
        if (sectionIds.Length == 0)
        {
            return McpToolResult.Error("Argument 'sectionIds' is required.");
        }

        TryGetWorkspaceRoot(workspace, out var workspaceRoot, out _);
        var knowledgeRoot = ResolveKnowledgeRoot(workspaceRoot);
        var maxBytes = GetInt32(arguments, "maxBytes") ?? 12000;
        var result = await new AgentKnowledgeStore(knowledgeRoot).ReadSectionsAsync(sectionIds, maxBytes, cancellationToken);
        return JsonSuccess(result);
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

        var requestedLimit = GetInt32(arguments, "limit") ?? Datc64ExtractMaxCells;
        var limit = Math.Clamp(requestedLimit, 1, Datc64ExtractMaxCells);

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
        if (requestedLimit != limit)
        {
            warnings.Add($"Requested limit was clamped from {requestedLimit} to {limit}.");
        }

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
            limit,
            cells,
            warnings
        });
    }

    private static async Task<McpToolResult> GetCurrentViewContextAsync(
        PoeWorkspaceResolution workspace,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
        {
            return McpToolResult.Error(error);
        }

        if (!TryGetString(arguments, "contextId", out var contextId))
        {
            return McpToolResult.Error("Argument 'contextId' is required.");
        }

        var snapshot = await new AgentCurrentViewStore(workspaceRoot).LoadAsync(contextId, cancellationToken);
        if (snapshot is null)
        {
            return McpToolResult.Error($"Current view context '{contextId}' was not found.");
        }

        return JsonSuccess(snapshot);
    }

    private static async Task<McpToolResult> FindCurrentTableUntranslatedCellsAsync(
        PoeWorkspaceResolution workspace,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
        {
            return McpToolResult.Error(error);
        }

        if (!TryGetString(arguments, "contextId", out var contextId))
        {
            return McpToolResult.Error("Argument 'contextId' is required.");
        }

        var limit = Math.Clamp(GetInt32(arguments, "limit") ?? 50, 1, 200);
        var snapshot = await new AgentCurrentViewStore(workspaceRoot).LoadAsync(contextId, cancellationToken);
        var table = snapshot?.View.Table;
        if (table is null)
        {
            return McpToolResult.Error($"Current view context '{contextId}' does not contain a table.");
        }

        if (table.SourceRows is null || table.SourceRows.Count == 0)
        {
            return McpToolResult.Error("Current table has no source/reference rows. Ask the user to open or match a source table first.");
        }

        var sourceRows = table.SourceRows.ToDictionary(row => row.RowNumber);
        var editable = table.EditableColumnIndexes.Count > 0
            ? table.EditableColumnIndexes
            : Enumerable.Range(0, table.Columns.Count).ToArray();
        var results = new List<AgentUntranslatedCellDto>();

        foreach (var targetRow in table.TargetRows)
        {
            if (!sourceRows.TryGetValue(targetRow.RowNumber, out var sourceRow))
            {
                continue;
            }

            foreach (var columnIndex in editable)
            {
                var sourceText = CellAt(sourceRow, columnIndex);
                var targetText = CellAt(targetRow, columnIndex);
                if (string.IsNullOrWhiteSpace(sourceText))
                {
                    continue;
                }

                var reason = MissingTranslationReason(sourceText, targetText);
                if (reason is null)
                {
                    continue;
                }

                results.Add(new AgentUntranslatedCellDto(
                    targetRow.RowNumber,
                    columnIndex,
                    columnIndex >= 0 && columnIndex < table.Columns.Count ? table.Columns[columnIndex] : null,
                    sourceText,
                    targetText,
                    reason));

                if (results.Count >= limit)
                {
                    break;
                }
            }

            if (results.Count >= limit)
            {
                break;
            }
        }

        return JsonSuccess(new
        {
            snapshot!.ContextId,
            table.TargetProfileId,
            table.TargetResourcePath,
            table.SourceProfileId,
            table.SourceResourcePath,
            inspectedRows = table.TargetRows.Count,
            candidates = results.Count,
            items = results
        });
    }

    private static async Task<McpToolResult> FindCurrentTableNonSimplifiedChineseCellsAsync(
        PoeWorkspaceResolution workspace,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
        {
            return McpToolResult.Error(error);
        }

        if (!TryGetString(arguments, "contextId", out var contextId))
        {
            return McpToolResult.Error("Argument 'contextId' is required.");
        }

        var limit = Math.Clamp(GetInt32(arguments, "limit") ?? 50, 1, 200);
        var snapshot = await new AgentCurrentViewStore(workspaceRoot).LoadAsync(contextId, cancellationToken);
        var table = snapshot?.View.Table;
        if (table is null)
        {
            return McpToolResult.Error($"Current view context '{contextId}' does not contain a table.");
        }

        var sourceRows = table.SourceRows?.ToDictionary(row => row.RowNumber) ?? new Dictionary<int, AgentCurrentTableRowDto>();
        var editable = table.EditableColumnIndexes.Count > 0
            ? table.EditableColumnIndexes
            : Enumerable.Range(0, table.Columns.Count).ToArray();
        var results = new List<AgentUntranslatedCellDto>();

        foreach (var targetRow in table.TargetRows)
        {
            sourceRows.TryGetValue(targetRow.RowNumber, out var sourceRow);

            foreach (var columnIndex in editable)
            {
                var targetText = CellAt(targetRow, columnIndex);
                var reason = NonSimplifiedChineseReason(targetText);
                if (reason is null)
                {
                    continue;
                }

                results.Add(new AgentUntranslatedCellDto(
                    targetRow.RowNumber,
                    columnIndex,
                    columnIndex >= 0 && columnIndex < table.Columns.Count ? table.Columns[columnIndex] : null,
                    sourceRow is null ? string.Empty : CellAt(sourceRow, columnIndex),
                    targetText,
                    reason));

                if (results.Count >= limit)
                {
                    break;
                }
            }

            if (results.Count >= limit)
            {
                break;
            }
        }

        return JsonSuccess(new
        {
            snapshot!.ContextId,
            table.TargetProfileId,
            table.TargetResourcePath,
            table.SourceProfileId,
            table.SourceResourcePath,
            inspectedRows = table.TargetRows.Count,
            candidates = results.Count,
            items = results
        });
    }

    private static async Task<McpToolResult> GetAgentRunTraceAsync(
        PoeWorkspaceResolution workspace,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
        {
            return McpToolResult.Error(error);
        }

        if (!TryGetString(arguments, "runId", out var runId))
        {
            return McpToolResult.Error("Argument 'runId' is required.");
        }

        var events = await new AgentRunTraceStore(workspaceRoot).ReadAsync(runId, cancellationToken);
        return JsonSuccess(new
        {
            runId,
            events = events.TakeLast(200)
        });
    }

    private static Task<McpToolResult> GetAgentRecentLogsAsync(
        PoeWorkspaceResolution workspace,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGetWorkspaceRoot(workspace, out var workspaceRoot, out var error))
        {
            return Task.FromResult(McpToolResult.Error(error));
        }

        var maxLines = Math.Clamp(GetInt32(arguments, "maxLines") ?? 80, 1, 300);
        var allowedNames = new[]
        {
            "poe-studio-dev.out.log",
            "poe-studio-dev.err.log",
            "poe-current-view-acceptance.out.log",
            "poe-current-view-acceptance.err.log"
        };
        var rootFullPath = Path.GetFullPath(workspaceRoot);
        var entries = new List<object>();

        foreach (var name in allowedNames)
        {
            var path = Path.Combine(workspaceRoot, name);
            var fullPath = Path.GetFullPath(path);
            if (!IsSameOrChildPath(rootFullPath, fullPath))
            {
                continue;
            }

            if (!File.Exists(fullPath))
            {
                entries.Add(new { name, exists = false, lines = Array.Empty<string>() });
                continue;
            }

            var lines = File.ReadLines(fullPath).TakeLast(maxLines).ToArray();
            entries.Add(new { name, exists = true, lines });
        }

        return Task.FromResult(JsonSuccess(new { maxLines, entries }));
    }

    private static bool IsSameOrChildPath(string rootFullPath, string candidateFullPath)
    {
        var relative = Path.GetRelativePath(rootFullPath, candidateFullPath);
        return relative == "."
            || (!relative.StartsWith("..", StringComparison.Ordinal)
                && !Path.IsPathRooted(relative));
    }

    private static string CellAt(AgentCurrentTableRowDto row, int columnIndex)
    {
        return columnIndex >= 0 && columnIndex < row.Cells.Count ? row.Cells[columnIndex] : string.Empty;
    }

    private static string? MissingTranslationReason(string sourceText, string targetText)
    {
        if (string.IsNullOrWhiteSpace(targetText))
        {
            return "target_empty";
        }

        if (string.Equals(sourceText, targetText, StringComparison.Ordinal))
        {
            return null;
        }

        if (LooksMostlyAscii(targetText) && !LooksMostlyAscii(sourceText))
        {
            return "target_still_english";
        }

        return null;
    }

    private static string? NonSimplifiedChineseReason(string targetText)
    {
        if (string.IsNullOrWhiteSpace(targetText))
        {
            return null;
        }

        return ContainsKnownTraditionalChinese(targetText)
            ? "target_contains_traditional_chinese"
            : null;
    }

    private static bool ContainsKnownTraditionalChinese(string value)
    {
        return value.Any(ch => KnownTraditionalChineseCharacters.Contains(ch));
    }

    private static readonly HashSet<char> KnownTraditionalChineseCharacters =
    [
        '變', '體', '與', '專', '業', '兩', '嚴', '個', '豐', '臨', '為', '舉',
        '麼', '義', '樂', '書', '買', '亂', '爭', '於', '雲', '亞', '產', '親',
        '億', '僅', '從', '倉', '儀', '們', '價', '眾', '優', '會', '傘', '偉',
        '傳', '傷', '倫', '偽', '餘', '俠', '侶', '偵', '側', '僑', '兒', '兇',
        '黨', '蘭', '關', '興', '養', '獸', '內', '寫', '軍', '農', '馮', '決',
        '況', '凍', '淨', '涼', '減', '幾', '鳳', '憑', '凱', '擊', '劃', '劇',
        '劉', '則', '剛', '創', '刪', '別', '劑', '劍', '勸', '辦', '務', '動',
        '勵', '勞', '勢', '匯', '區', '協', '單', '賣', '盧', '卻', '廠', '廳',
        '歷', '厲', '壓', '厭', '縣', '參', '雙', '發', '號', '後', '嚇', '嗎',
        '啟', '員', '聽', '問', '喚', '嘗', '噴', '團', '園', '圓', '圖', '聖',
        '場', '壞', '塊', '堅', '壇', '墜', '壘', '壯', '聲', '殼', '處', '備',
        '複', '夠', '頭', '奪', '奮', '獎', '婦', '媽', '嬌', '孫', '學', '寶',
        '實', '寵', '審', '寬', '對', '尋', '導', '將', '爾', '塵', '層', '屬',
        '歲', '島', '嶺', '幣', '師', '帳', '帶', '幫', '幹', '並', '廣', '莊',
        '慶', '庫', '應', '廟', '廢', '開', '異', '棄', '張', '彈', '強', '歸',
        '錄', '徹', '徑', '憶', '憂', '懷', '態', '慫', '憐', '總', '戀', '惡',
        '愛', '慣', '慘', '慮', '憤', '戰', '戲', '戶', '捨', '掃', '搶', '護',
        '報', '擔', '擬', '擁', '撥', '擇', '擋', '揮', '據', '捲', '攜', '攝',
        '擺', '搖', '敗', '敵', '數', '齊', '斷', '時', '暫', '會', '術', '機',
        '殺', '雜', '權', '條', '來', '極', '構', '欄', '樹', '樣', '橋', '檢',
        '檔', '槍', '樓', '標', '橫', '歡', '歐', '殘', '毀', '氣', '漢', '湯',
        '滅', '淚', '澤', '潔', '灑', '測', '濟', '濃', '濤', '湧', '灣', '濕',
        '滿', '滾', '漲', '潛', '潤', '濾', '燈', '靈', '災', '爐', '點', '煉',
        '燒', '營', '牆', '狀', '獨', '獅', '獵', '獻', '瑪', '環', '現', '電',
        '畫', '療', '發', '皺', '盜', '盤', '眾', '矯', '礦', '碼', '確', '禍',
        '禮', '離', '種', '積', '稱', '穩', '窮', '竊', '競', '筆', '築', '節',
        '範', '簡', '籃', '籌', '糾', '紀', '約', '紅', '紋', '納', '紐', '純',
        '紙', '級', '紛', '細', '紹', '組', '結', '絕', '給', '統', '絲', '綁',
        '經', '綠', '維', '網', '緊', '緒', '線', '緣', '編', '練', '縣', '縫',
        '縮', '總', '績', '織', '繞', '繼', '續', '纖', '罰', '罵', '羅', '聖',
        '聞', '聯', '聰', '聲', '職', '聽', '膽', '勝', '臉', '臨', '舉', '藝',
        '節', '藍', '虛', '蟲', '雖', '蠻', '術', '補', '裝', '裏', '製', '褲',
        '見', '觀', '規', '視', '覺', '覽', '觸', '訂', '計', '訊', '討', '訓',
        '記', '訣', '訪', '設', '許', '訴', '該', '詳', '認', '語', '誤', '說',
        '誰', '課', '調', '談', '請', '論', '諸', '諾', '謂', '講', '謝', '謹',
        '證', '識', '譯', '議', '護', '讀', '讓', '讚', '貝', '負', '財', '責',
        '賢', '敗', '貨', '質', '貪', '貧', '貴', '貸', '費', '貼', '資', '賊',
        '賞', '賠', '賽', '贈', '贏', '趙', '趕', '躍', '車', '軟', '較', '載',
        '輔', '輝', '輪', '輯', '輸', '轉', '辭', '農', '這', '連', '進', '運',
        '過', '達', '違', '遙', '遠', '適', '遲', '遷', '選', '遺', '還', '邊',
        '郵', '鄰', '釋', '針', '釘', '釣', '鈴', '鉛', '銀', '銅', '銘', '鋼',
        '錄', '錢', '錯', '鍛', '鎖', '鎮', '鏡', '鐵', '鑄', '長', '門', '閉',
        '間', '閣', '關', '闖', '陣', '陰', '陳', '陽', '隊', '階', '隨', '險',
        '隱', '隸', '雙', '難', '雲', '電', '靜', '韋', '頁', '頂', '項', '順',
        '須', '預', '頓', '領', '顏', '願', '類', '顧', '顯', '驚', '驅', '驗',
        '騎', '騙', '騰', '驟', '魚', '鳥', '鹽', '麥', '黃', '龍'
    ];

    private static bool LooksMostlyAscii(string value)
    {
        var letters = value.Where(char.IsLetter).ToArray();
        if (letters.Length == 0)
        {
            return false;
        }

        var asciiLetters = letters.Count(ch => ch <= 0x7f);
        return asciiLetters >= Math.Ceiling(letters.Length * 0.8);
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

    private static string ResolveKnowledgeRoot(string? workspaceRoot)
    {
        if (!string.IsNullOrWhiteSpace(workspaceRoot)
            && File.Exists(Path.Combine(workspaceRoot, "docs", "agent", "knowledge", "index.json")))
        {
            return workspaceRoot;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs", "agent", "knowledge", "index.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static IReadOnlyList<object> ReadKnowledgeIndexSummary(string knowledgeRoot)
    {
        try
        {
            var index = new AgentKnowledgeStore(knowledgeRoot).ReadIndexAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return index.Sections
                .Select(section => new
                {
                    section.SectionId,
                    section.Title,
                    section.Summary,
                    section.Keywords,
                    section.AppliesWhen,
                    section.Priority
                })
                .Cast<object>()
                .ToArray();
        }
        catch (Exception)
        {
            return [];
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
                StringComparer.Ordinal),
            ["required"] = properties.Select(p => p.Name).ToArray()
        };

        return JsonSerializer.SerializeToElement(schema);
    }

    private static McpToolResult JsonSuccess(object payload)
    {
        return McpToolResult.Success(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static string TruncateText(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..Math.Max(0, maxLength - 14)].TrimEnd() + " [truncated]";
    }
}
