using PoeStudio.Api;
using PoeStudio.Contracts;
using PoeStudio.Api.Jobs;
using System.Security.Cryptography;
using System.Text;
using PoeStudio.Core.Batch;
using PoeStudio.Core.ClientDetection;
using PoeStudio.Core.Native;
using PoeStudio.Core.Overlay;
using PoeStudio.Core.Patching;
using PoeStudio.Core.Preview;
using PoeStudio.Core.Resources;
using PoeStudio.Core.Tables;
using PoeStudio.Core.Translation;
using PoeStudio.Core.Workspace;
using PoeStudio.Core.Agent;
using PoeStudio.Storage.Agent;
using PoeStudio.Storage.Overlay;
using PoeStudio.Storage.Profiles;
using PoeStudio.Storage.Batch;
using PoeStudio.Storage.Migration;
using PoeStudio.Storage.Resources;
using PoeStudio.Storage.Tables;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors();
builder.Services.AddSingleton<InMemoryJobStore>();
builder.Services.AddSingleton<WorkspaceRootProvider>();
builder.Services.AddScoped(sp => new ProfileStore(sp.GetRequiredService<WorkspaceRootProvider>().CurrentRoot));
builder.Services.AddSingleton(sp => new ResourceIndexStore(() => sp.GetRequiredService<WorkspaceRootProvider>().CurrentRoot));
builder.Services.AddScoped(sp => new TableSchemaStore(sp.GetRequiredService<WorkspaceRootProvider>().CurrentRoot));
builder.Services.AddScoped(sp => new BatchScriptTemplateStore(sp.GetRequiredService<WorkspaceRootProvider>().CurrentRoot));
builder.Services.AddScoped(sp => new MigrationPlanStore(sp.GetRequiredService<WorkspaceRootProvider>().CurrentRoot));
builder.Services.AddSingleton<FileSystemResourceIndexer>();
builder.Services.AddSingleton<NativeBundles2IndexReader>();
builder.Services.AddSingleton<NativeIndexRecordParser>();
builder.Services.AddSingleton<NativeBundles2ResourceIndexer>();
builder.Services.AddSingleton<GgpkResourceIndexer>();
builder.Services.AddSingleton<IOodleCodec, MissingOodleCodec>();
builder.Services.AddScoped(sp => new NativeIndexCacheService(sp.GetRequiredService<WorkspaceRootProvider>().CurrentRoot, sp.GetRequiredService<IOodleCodec>()));
builder.Services.AddSingleton<OodleCodecFactory>(_ => path => new NativeOodleCodec(path));
builder.Services.AddScoped<NativeIndexPathService>();
builder.Services.AddSingleton(sp => new NativeBundleResourceContentResolver(sp.GetRequiredService<IOodleCodec>()));
builder.Services.AddSingleton(sp => new ResourcePreviewService(sp.GetRequiredService<NativeBundleResourceContentResolver>()));
builder.Services.AddScoped(sp => new OverlayStore(sp.GetRequiredService<WorkspaceRootProvider>().CurrentRoot));
builder.Services.AddScoped(sp =>
{
    var workspaceRoot = sp.GetRequiredService<WorkspaceRootProvider>().CurrentRoot;
    return new PatchBuildService(workspaceRoot, sp.GetRequiredService<OverlayStore>(), sp.GetRequiredService<ResourceIndexStore>());
});
builder.Services.AddSingleton<PatchImportAnalyzer>();
builder.Services.AddScoped(sp => new PatchZipImportService(sp.GetRequiredService<WorkspaceRootProvider>().CurrentRoot, sp.GetRequiredService<PatchImportAnalyzer>()));
builder.Services.AddSingleton(sp => new PatchZipInstallPreviewService(sp.GetRequiredService<PatchImportAnalyzer>()));
builder.Services.AddScoped(sp => new OverlayReviewService(sp.GetRequiredService<OverlayStore>(), sp.GetRequiredService<ResourceIndexStore>()));
builder.Services.AddScoped(sp => new PatchOverlayDraftService(
    sp.GetRequiredService<WorkspaceRootProvider>().CurrentRoot,
    sp.GetRequiredService<OverlayStore>(),
    sp.GetRequiredService<ResourceIndexStore>()));
builder.Services.AddScoped(sp => new AgentStore(sp.GetRequiredService<WorkspaceRootProvider>().CurrentRoot));
builder.Services.AddSingleton<CodexJsonEventParser>();
builder.Services.AddSingleton<AgentPromptBuilder>();
builder.Services.AddSingleton<Datc64TranslationDraftParser>();
builder.Services.AddScoped<CodexProcessRunner>();
builder.Services.AddScoped<ICodexProcessRunner>(sp => sp.GetRequiredService<CodexProcessRunner>());
builder.Services.AddScoped<AgentOrchestrator>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapAgentRoutes();

app.MapGet("/api/health", () => ApiResponse<object>.Success(new
{
    status = "ok",
    utcTime = DateTimeOffset.UtcNow
}));

app.MapGet("/api/workspace", (WorkspaceRootProvider workspace) =>
{
    var result = CheckWorkspace(workspace.CurrentRoot);
    return Results.Ok(ApiResponse<WorkspaceSettingsDto>.Success(new WorkspaceSettingsDto(
        workspace.CurrentRoot,
        result.Writable,
        result.Warnings)));
});

app.MapPost("/api/workspace", (
    WorkspaceSettingsUpdateRequest request,
    WorkspaceRootProvider workspace) =>
{
    try
    {
        var workspaceRoot = workspace.SetRoot(request.WorkspaceRoot);
        var result = CheckWorkspace(workspaceRoot);
        return Results.Ok(ApiResponse<WorkspaceSettingsDto>.Success(new WorkspaceSettingsDto(
            workspaceRoot,
            result.Writable,
            result.Warnings)));
    }
    catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
    {
        return Results.BadRequest(ApiResponse<WorkspaceSettingsDto>.Failure("invalid_workspace", ex.Message));
    }
});

app.MapGet("/api/diagnostics", async (
    WorkspaceRootProvider workspace,
    ProfileStore profiles,
    CancellationToken cancellationToken) =>
{
    var workspaceRoot = workspace.CurrentRoot;
    var result = await CheckWorkspaceAsync(workspaceRoot, cancellationToken);

    var profileCount = (await profiles.ListAsync(cancellationToken)).Count;
    return ApiResponse<AppDiagnosticsDto>.Success(new AppDiagnosticsDto(
        result.Writable ? "ok" : "warning",
        workspaceRoot,
        result.Writable,
        profileCount,
        DateTimeOffset.UtcNow,
        result.Warnings));
});

app.MapGet("/api/profiles", async (ProfileStore store, CancellationToken cancellationToken) =>
{
    var profiles = await store.ListAsync(cancellationToken);
    return ApiResponse<IReadOnlyList<ClientProfileDto>>.Success(profiles);
});

app.MapPost("/api/profiles/detect", (DetectClientRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.RootPath))
    {
        return Results.BadRequest(ApiResponse<DetectClientResponse>.Failure("invalid_root_path", "客户端目录不能为空。"));
    }

    var result = ClientDetector.Detect(request.RootPath, request.OodleSearchPath);
    var response = new DetectClientResponse(
        result.Detected,
        result.Platform,
        result.EntryKind,
        result.RootPath,
        result.ContentGgpkPath,
        result.Bundles2Path,
        result.IndexPath,
        result.OodleStatus,
        result.OodlePath,
        result.ClientFingerprint,
        result.Warnings);

    return Results.Ok(ApiResponse<DetectClientResponse>.Success(response));
});

app.MapPost("/api/profiles/detect-and-save", async (
    DetectClientRequest request,
    ProfileStore store,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.RootPath))
    {
        return Results.BadRequest(ApiResponse<ClientProfileDto>.Failure("invalid_root_path", "客户端目录不能为空。"));
    }

    var result = ClientDetector.Detect(request.RootPath, request.OodleSearchPath);
    if (!result.Detected)
    {
        return Results.BadRequest(ApiResponse<ClientProfileDto>.Failure("client_not_detected", "未检测到支持的 POE 客户端。"));
    }

    var now = DateTimeOffset.UtcNow;
    var profile = new ClientProfileDto(
        Id: Guid.NewGuid().ToString("N"),
        DisplayName: $"{result.Platform} POE2",
        Platform: result.Platform,
        EntryKind: result.EntryKind,
        RootPath: result.RootPath,
        ContentGgpkPath: result.ContentGgpkPath,
        Bundles2Path: result.Bundles2Path,
        IndexPath: result.IndexPath,
        OodleStatus: result.OodleStatus,
        ClientFingerprint: result.ClientFingerprint,
        CreatedAt: now,
        UpdatedAt: now);

    await store.SaveAsync(profile, cancellationToken);
    return Results.Ok(ApiResponse<ClientProfileDto>.Success(profile));
});

app.MapPost("/api/profiles", async (CreateProfileRequest request, ProfileStore store, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.RootPath))
    {
        return Results.BadRequest(ApiResponse<ClientProfileDto>.Failure("invalid_profile", "名称和客户端目录不能为空。"));
    }

    var now = DateTimeOffset.UtcNow;
    var profile = new ClientProfileDto(
        Id: Guid.NewGuid().ToString("N"),
        DisplayName: request.DisplayName,
        Platform: request.Platform,
        EntryKind: request.EntryKind,
        RootPath: request.RootPath,
        ContentGgpkPath: request.ContentGgpkPath,
        Bundles2Path: request.Bundles2Path,
        IndexPath: request.IndexPath,
        OodleStatus: request.OodleStatus,
        ClientFingerprint: request.ClientFingerprint,
        CreatedAt: now,
        UpdatedAt: now);

    await store.SaveAsync(profile, cancellationToken);
    return Results.Ok(ApiResponse<ClientProfileDto>.Success(profile));
});

app.MapPost("/api/profiles/delete", async (
    DeleteProfileRequest request,
    ProfileStore store,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.ProfileId))
    {
        return Results.BadRequest(ApiResponse<DeleteProfileResponse>.Failure("invalid_profile", "配置 ID 不能为空。"));
    }

    try
    {
        var removed = await store.DeleteAsync(request.ProfileId, cancellationToken);
        return Results.Ok(ApiResponse<DeleteProfileResponse>.Success(new DeleteProfileResponse(request.ProfileId, removed)));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<DeleteProfileResponse>.Failure("invalid_profile", ex.Message));
    }
});

app.MapPost("/api/index/build", async (
    ResourceIndexBuildRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    FileSystemResourceIndexer indexer,
    CancellationToken cancellationToken) =>
{
    var profile = await profiles.GetAsync(request.ProfileId, cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(ApiResponse<ResourceIndexBuildResponse>.Failure("profile_not_found", "未找到客户端配置。"));
    }

    var result = await indexer.IndexAsync(profile, cancellationToken);
    await resourceIndex.SaveAsync(profile.Id, result.Resources, result.Warnings, cancellationToken);
    var response = new ResourceIndexBuildResponse(profile.Id, result.Resources.Count, DateTimeOffset.UtcNow, result.Warnings);
    return Results.Ok(ApiResponse<ResourceIndexBuildResponse>.Success(response));
});

app.MapPost("/api/resources/search", async (
    ResourceSearchRequest request,
    ResourceIndexStore resourceIndex,
    CancellationToken cancellationToken) =>
{
    var response = await resourceIndex.SearchAsync(request, cancellationToken);
    return Results.Ok(ApiResponse<ResourceSearchResponse>.Success(response));
});

app.MapPost("/api/resources/by-path", async (
    ResourcePathLookupRequest request,
    ResourceIndexStore resourceIndex,
    CancellationToken cancellationToken) =>
{
    var resource = await resourceIndex.GetByPathAsync(request.ProfileId, request.VirtualPath, cancellationToken);
    return resource is null
        ? Results.NotFound(ApiResponse<ResourceSummaryDto>.Failure("resource_not_found", "未找到该路径资源。"))
        : Results.Ok(ApiResponse<ResourceSummaryDto>.Success(resource));
});

app.MapPost("/api/resources/format-scan", async (
    ResourceFormatScanRequest request,
    ResourceIndexStore resourceIndex,
    CancellationToken cancellationToken) =>
{
    var take = Math.Clamp(request.Take, 1, 50000);
    var search = await resourceIndex.SearchAsync(new ResourceSearchRequest(
        request.ProfileId,
        Skip: 0,
        Take: take), cancellationToken);
    var warnings = new List<string>();
    if (search.Total > search.Items.Count)
    {
        warnings.Add($"索引资源较多，本次只扫描前 {search.Items.Count}/{search.Total} 个。");
    }

    var items = search.Items
        .GroupBy(resource => string.IsNullOrWhiteSpace(resource.Extension) ? "(none)" : resource.Extension.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase)
        .Select(group => BuildFormatScanItem(group.Key, group))
        .OrderByDescending(item => item.Total)
        .ThenBy(item => item.Extension, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    var response = new ResourceFormatScanResponse(
        request.ProfileId,
        search.Total,
        search.Items.Count,
        items.Length,
        items,
        warnings);
    return Results.Ok(ApiResponse<ResourceFormatScanResponse>.Success(response));
});

app.MapPost("/api/resources/export", async (
    ResourceExportRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var resource = await resourceIndex.GetByPathAsync(request.ProfileId, request.VirtualPath, cancellationToken);
    if (resource is null)
    {
        return Results.NotFound(ApiResponse<ResourceExportResponse>.Failure("resource_not_found", "未找到资源，请先建立索引。"));
    }

    var warnings = new List<string>();
    var read = request.UseOverlay
        ? await ReadResourceBytesPreferOverlayAsync(
            request.ProfileId,
            request.OodlePath,
            resource,
            profiles,
            nativeContentResolver,
            overlay,
            cancellationToken)
        : await ReadResourceBytesAsync(
            request.ProfileId,
            request.OodlePath,
            resource,
            profiles,
            nativeContentResolver,
            cancellationToken);
    if (!read.Ok)
    {
        return read.StatusCode == StatusCodes.Status404NotFound
            ? Results.NotFound(ApiResponse<ResourceExportResponse>.Failure(read.ErrorCode, read.Message))
            : Results.BadRequest(ApiResponse<ResourceExportResponse>.Failure(read.ErrorCode, read.Message));
    }

    var response = new ResourceExportResponse(
        request.ProfileId,
        resource.VirtualPath,
        Path.GetFileName(resource.VirtualPath),
        GuessContentType(resource.Extension),
        Convert.ToBase64String(read.Data),
        read.Data.LongLength,
        warnings);
    return Results.Ok(ApiResponse<ResourceExportResponse>.Success(response));
});

app.MapPost("/api/resources/signature", async (
    ResourceSignatureRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var resource = await resourceIndex.GetByPathAsync(request.ProfileId, request.VirtualPath, cancellationToken);
    if (resource is null)
    {
        return Results.NotFound(ApiResponse<ResourceSignatureResponse>.Failure("resource_not_found", "未找到资源，请先建立索引。"));
    }

    var read = request.UseOverlay
        ? await ReadResourceBytesPreferOverlayAsync(
            request.ProfileId,
            request.OodlePath,
            resource,
            profiles,
            nativeContentResolver,
            overlay,
            cancellationToken)
        : await ReadResourceBytesAsync(
            request.ProfileId,
            request.OodlePath,
            resource,
            profiles,
            nativeContentResolver,
            cancellationToken);
    if (!read.Ok)
    {
        return read.StatusCode == StatusCodes.Status404NotFound
            ? Results.NotFound(ApiResponse<ResourceSignatureResponse>.Failure(read.ErrorCode, read.Message))
            : Results.BadRequest(ApiResponse<ResourceSignatureResponse>.Failure(read.ErrorCode, read.Message));
    }

    var response = BuildSignatureResponse(request.ProfileId, resource, read.Data, []);
    return Results.Ok(ApiResponse<ResourceSignatureResponse>.Success(response));
});

app.MapPost("/api/resources/bulk-signature", async (
    ResourceBulkSignatureRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var search = await resourceIndex.SearchAsync(new ResourceSearchRequest(
        request.ProfileId,
        Query: request.Query,
        Kind: request.Kind,
        Extension: request.Extension,
        Skip: 0,
        Take: Math.Clamp(request.Take, 1, 500)), cancellationToken);
    var items = new List<ResourceSignatureResponse>();
    var warnings = new List<string>();

    foreach (var resource in search.Items)
    {
        var read = request.UseOverlay
            ? await ReadResourceBytesPreferOverlayAsync(
                request.ProfileId,
                request.OodlePath,
                resource,
                profiles,
                nativeContentResolver,
                overlay,
                cancellationToken)
            : await ReadResourceBytesAsync(
                request.ProfileId,
                request.OodlePath,
                resource,
                profiles,
                nativeContentResolver,
                cancellationToken);
        if (!read.Ok)
        {
            warnings.Add($"{resource.VirtualPath}: {read.Message}");
            continue;
        }

        items.Add(BuildSignatureResponse(request.ProfileId, resource, read.Data, []));
    }

    var response = new ResourceBulkSignatureResponse(
        request.ProfileId,
        search.Total,
        items.Count,
        items,
        warnings);
    return Results.Ok(ApiResponse<ResourceBulkSignatureResponse>.Success(response));
});

app.MapPost("/api/resources/match", async (
    ResourceMatchRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var source = await BuildSignatureSetAsync(
        request.SourceProfileId,
        request.Query,
        request.Kind,
        request.Extension,
        request.Take,
        request.SourceOodlePath,
        profiles,
        resourceIndex,
        nativeContentResolver,
        overlay,
        request.UseOverlay,
        cancellationToken);
    var target = await BuildSignatureSetAsync(
        request.TargetProfileId,
        request.Query,
        request.Kind,
        request.Extension,
        request.Take,
        request.TargetOodlePath,
        profiles,
        resourceIndex,
        nativeContentResolver,
        overlay,
        request.UseOverlay,
        cancellationToken);

    var targetByPath = target.Items.ToDictionary(item => item.VirtualPath, StringComparer.OrdinalIgnoreCase);
    var targetByHash = target.Items.GroupBy(item => item.Sha256, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
    var matches = new List<ResourceMatchItemDto>();

    foreach (var sourceItem in source.Items)
    {
        var candidates = new List<ResourceSignatureResponse>();
        if (targetByPath.TryGetValue(sourceItem.VirtualPath, out var pathMatch))
        {
            candidates.Add(pathMatch);
        }

        if (targetByHash.TryGetValue(sourceItem.Sha256, out var hashMatches))
        {
            candidates.AddRange(hashMatches);
        }

        var best = candidates
            .DistinctBy(item => item.VirtualPath, StringComparer.OrdinalIgnoreCase)
            .Select(targetItem => BuildResourceMatch(sourceItem, targetItem))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.TargetPath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (best is not null)
        {
            matches.Add(best);
        }
    }

    var response = new ResourceMatchResponse(
        request.SourceProfileId,
        request.TargetProfileId,
        source.Matched,
        target.Matched,
        matches.Count,
        matches.OrderByDescending(item => item.Score).ThenBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray(),
        source.Warnings.Concat(target.Warnings).ToArray());
    return Results.Ok(ApiResponse<ResourceMatchResponse>.Success(response));
});

app.MapPost("/api/resources/migration-plan", async (
    ResourceMigrationPlanRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var source = await BuildSignatureSetAsync(
        request.SourceProfileId,
        request.Query ?? string.Empty,
        request.Kind,
        request.Extension,
        request.Take,
        request.SourceOodlePath,
        profiles,
        resourceIndex,
        nativeContentResolver,
        overlay,
        request.UseOverlay,
        cancellationToken);
    var target = await BuildSignatureSetAsync(
        request.TargetProfileId,
        request.Query ?? string.Empty,
        request.Kind,
        request.Extension,
        request.Take,
        request.TargetOodlePath,
        profiles,
        resourceIndex,
        nativeContentResolver,
        overlay,
        request.UseOverlay,
        cancellationToken);

    var response = BuildMigrationPlan(request, source, target);
    return Results.Ok(ApiResponse<ResourceMigrationPlanResponse>.Success(response));
});

app.MapPost("/api/resources/migration-draft", async (
    ResourceMigrationDraftRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var response = await BuildMigrationDraftAsync(
        request,
        profiles,
        resourceIndex,
        nativeContentResolver,
        overlay,
        cancellationToken);
    return Results.Ok(ApiResponse<ResourceMigrationDraftResponse>.Success(response));
});

app.MapPost("/api/resources/migration-apply-item", async (
    ResourceMigrationApplyItemRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var response = await ApplyMigrationItemAsync(
        request,
        profiles,
        resourceIndex,
        nativeContentResolver,
        overlay,
        cancellationToken);
    if (response.ErrorCode is not null)
    {
        return response.StatusCode == StatusCodes.Status404NotFound
            ? Results.NotFound(ApiResponse<ResourceMigrationApplyItemResponse>.Failure(response.ErrorCode, response.Message))
            : Results.BadRequest(ApiResponse<ResourceMigrationApplyItemResponse>.Failure(response.ErrorCode, response.Message));
    }

    return Results.Ok(ApiResponse<ResourceMigrationApplyItemResponse>.Success(response.Data!));
});

app.MapPost("/api/resources/migration-plans/save", async (
    ResourceMigrationPlanSaveRequest request,
    MigrationPlanStore migrationPlans,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await migrationPlans.SaveAsync(request, cancellationToken);
        return Results.Ok(ApiResponse<ResourceMigrationPlanEntryDto>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<ResourceMigrationPlanEntryDto>.Failure("invalid_migration_plan", ex.Message));
    }
});

app.MapPost("/api/resources/migration-plans/list", async (
    ResourceMigrationPlanListRequest request,
    MigrationPlanStore migrationPlans,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await migrationPlans.ListAsync(request, cancellationToken);
        return Results.Ok(ApiResponse<ResourceMigrationPlanListResponse>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<ResourceMigrationPlanListResponse>.Failure("invalid_migration_plan", ex.Message));
    }
});

app.MapPost("/api/resources/migration-plans/load", async (
    ResourceMigrationPlanLoadRequest request,
    MigrationPlanStore migrationPlans,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await migrationPlans.GetAsync(request.SourceProfileId, request.PlanId, cancellationToken);
        return response is null
            ? Results.NotFound(ApiResponse<ResourceMigrationPlanEntryDto>.Failure("migration_plan_not_found", "未找到迁移方案。"))
            : Results.Ok(ApiResponse<ResourceMigrationPlanEntryDto>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<ResourceMigrationPlanEntryDto>.Failure("invalid_migration_plan", ex.Message));
    }
});

app.MapPost("/api/resources/migration-plans/delete", async (
    ResourceMigrationPlanDeleteRequest request,
    MigrationPlanStore migrationPlans,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await migrationPlans.DeleteAsync(request, cancellationToken);
        return Results.Ok(ApiResponse<ResourceMigrationPlanDeleteResponse>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<ResourceMigrationPlanDeleteResponse>.Failure("invalid_migration_plan", ex.Message));
    }
});

app.MapPost("/api/resources/migration-plans/validate", async (
    ResourceMigrationPlanValidateRequest request,
    MigrationPlanStore migrationPlans,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    try
    {
        var plan = await migrationPlans.GetAsync(request.SourceProfileId, request.PlanId, cancellationToken);
        if (plan is null)
        {
            return Results.NotFound(ApiResponse<ResourceMigrationPlanValidateResponse>.Failure("migration_plan_not_found", "未找到迁移方案。"));
        }

        var response = await ValidateSavedMigrationPlanAsync(
            request,
            plan,
            profiles,
            resourceIndex,
            nativeContentResolver,
            overlay,
            cancellationToken);
        return Results.Ok(ApiResponse<ResourceMigrationPlanValidateResponse>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<ResourceMigrationPlanValidateResponse>.Failure("invalid_migration_plan", ex.Message));
    }
});

app.MapPost("/api/resources/migration-plans/apply", async (
    ResourceMigrationPlanApplyRequest request,
    MigrationPlanStore migrationPlans,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    try
    {
        var plan = await migrationPlans.GetAsync(request.SourceProfileId, request.PlanId, cancellationToken);
        if (plan is null)
        {
            return Results.NotFound(ApiResponse<ResourceMigrationDraftResponse>.Failure("migration_plan_not_found", "未找到迁移方案。"));
        }

        var response = await ApplySavedMigrationPlanAsync(
            request,
            plan,
            profiles,
            resourceIndex,
            nativeContentResolver,
            overlay,
            cancellationToken);
        return Results.Ok(ApiResponse<ResourceMigrationDraftResponse>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<ResourceMigrationDraftResponse>.Failure("invalid_migration_plan", ex.Message));
    }
});

app.MapPost("/api/resources/bulk-export", async (
    ResourceBulkExportRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    WorkspaceRootProvider workspace,
    CancellationToken cancellationToken) =>
{
    var search = await resourceIndex.SearchAsync(new ResourceSearchRequest(
        request.ProfileId,
        Query: request.Query,
        Kind: request.Kind,
        Extension: request.Extension,
        Skip: 0,
        Take: Math.Clamp(request.Take, 1, 500)), cancellationToken);
    var layout = WorkspaceLayout.ForProfile(workspace.CurrentRoot, request.ProfileId);
    layout.EnsureDirectories();
    var exportRoot = Path.Combine(layout.RawCacheRoot, $"export-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
    Directory.CreateDirectory(exportRoot);
    var warnings = new List<string>();
    var exported = new List<ResourceBulkExportItemDto>();

    foreach (var resource in search.Items)
    {
        var read = request.UseOverlay
            ? await ReadResourceBytesPreferOverlayAsync(
                request.ProfileId,
                request.OodlePath,
                resource,
                profiles,
                nativeContentResolver,
                overlay,
                cancellationToken)
            : await ReadResourceBytesAsync(
                request.ProfileId,
                request.OodlePath,
                resource,
                profiles,
                nativeContentResolver,
                cancellationToken);
        if (!read.Ok)
        {
            warnings.Add($"跳过资源：{resource.VirtualPath}：{read.Message}");
            continue;
        }

        var target = SafeExportPath(exportRoot, resource.NormalizedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, read.Data, cancellationToken);
        exported.Add(new ResourceBulkExportItemDto(resource.VirtualPath, target, read.Data.LongLength));
    }

    var response = new ResourceBulkExportResponse(
        request.ProfileId,
        search.Total,
        exported.Count,
        exportRoot,
        exported,
        warnings);
    return Results.Ok(ApiResponse<ResourceBulkExportResponse>.Success(response));
});

app.MapPost("/api/resources/bulk-import-overlay", async (
    ResourceBulkImportOverlayRequest request,
    ResourceIndexStore resourceIndex,
    OverlayStore overlay,
    WorkspaceRootProvider workspace,
    CancellationToken cancellationToken) =>
{
    var layout = WorkspaceLayout.ForProfile(workspace.CurrentRoot, request.ProfileId);
    var exportRoot = Path.GetFullPath(request.ExportRoot);
    var rawRoot = Path.GetFullPath(layout.RawCacheRoot);
    if (!IsSubPath(rawRoot, exportRoot) || !Directory.Exists(exportRoot))
    {
        return Results.BadRequest(ApiResponse<ResourceBulkImportOverlayResponse>.Failure("invalid_export_root", "导入目录必须来自当前配置的批量导出工作区。"));
    }

    var imported = new List<string>();
    var warnings = new List<string>();
    var files = Directory.EnumerateFiles(exportRoot, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .Take(Math.Clamp(request.Take, 1, 2000))
        .ToArray();

    foreach (var file in files)
    {
        var relative = Path.GetRelativePath(exportRoot, file).Replace('\\', '/');
        try
        {
            var resource = await resourceIndex.GetByPathAsync(request.ProfileId, relative, cancellationToken);
            if (resource is null)
            {
                warnings.Add($"跳过未索引资源：{relative}");
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
            await overlay.SaveBytesAsync(
                request.ProfileId,
                resource.VirtualPath,
                bytes,
                resource.PhysicalPath,
                HasBasePhysicalPath: resource.PhysicalPath is not null && File.Exists(resource.PhysicalPath),
                cancellationToken);
            imported.Add(resource.VirtualPath);
        }
        catch (ArgumentException ex)
        {
            warnings.Add($"跳过路径不合法资源：{relative}：{ex.Message}");
        }
    }

    var response = new ResourceBulkImportOverlayResponse(
        request.ProfileId,
        exportRoot,
        imported.Count,
        imported,
        warnings);
    return Results.Ok(ApiResponse<ResourceBulkImportOverlayResponse>.Success(response));
});

app.MapPost("/api/preview", async (
    ResourcePreviewRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    ResourcePreviewService preview,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var resource = await resourceIndex.GetByPathAsync(request.ProfileId, request.VirtualPath, cancellationToken);
    if (resource is null)
    {
        return Results.NotFound(ApiResponse<ResourcePreviewResponse>.Failure("resource_not_found", "未找到资源，请先建立索引。"));
    }

    var previewResource = request.UseOverlay
        ? await BuildOverlayResourceAsync(resource, overlay, cancellationToken) ?? resource
        : resource;
    var profile = NativeBundleResourceContentResolver.IsNativeResource(previewResource)
        ? await profiles.GetAsync(request.ProfileId, cancellationToken)
        : null;
    var response = await preview.BuildPreviewAsync(previewResource, profile, request.Limit, request.OodlePath, cancellationToken);
    return Results.Ok(ApiResponse<ResourcePreviewResponse>.Success(response));
});

app.MapPost("/api/resources/preview", async (
    ResourcePreviewRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    ResourcePreviewService preview,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var resource = await resourceIndex.GetByPathAsync(request.ProfileId, request.VirtualPath, cancellationToken);
    if (resource is null)
    {
        return Results.NotFound(ApiResponse<ResourcePreviewResponse>.Failure("resource_not_found", "未找到资源，请先建立索引。"));
    }

    var previewResource = request.UseOverlay
        ? await BuildOverlayResourceAsync(resource, overlay, cancellationToken) ?? resource
        : resource;
    var profile = NativeBundleResourceContentResolver.IsNativeResource(previewResource)
        ? await profiles.GetAsync(request.ProfileId, cancellationToken)
        : null;
    var response = await preview.BuildPreviewAsync(previewResource, profile, request.Limit, request.OodlePath, cancellationToken);
    return Results.Ok(ApiResponse<ResourcePreviewResponse>.Success(response));
});

app.MapPost("/api/text/chunk", async (
    TextChunkRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var resource = await resourceIndex.GetByPathAsync(request.ProfileId, request.VirtualPath, cancellationToken);
    if (resource is null)
    {
        return Results.NotFound(ApiResponse<TextChunkResponse>.Failure("resource_not_found", "未找到资源，请先建立索引。"));
    }

    if (!CanTextChunkResource(resource))
    {
        return Results.BadRequest(ApiResponse<TextChunkResponse>.Failure("not_text_resource", "该资源不是可分块编辑的文本资源。"));
    }

    var read = request.UseOverlay
        ? await ReadResourceBytesPreferOverlayWithSourceAsync(request.ProfileId, request.OodlePath, resource, profiles, nativeContentResolver, overlay, cancellationToken)
        : await ReadResourceBytesWithSourceAsync(request.ProfileId, request.OodlePath, resource, profiles, nativeContentResolver, FromOverlay: false, cancellationToken);
    if (!read.Ok)
    {
        return read.StatusCode == StatusCodes.Status404NotFound
            ? Results.NotFound(ApiResponse<TextChunkResponse>.Failure(read.ErrorCode, read.Message))
            : Results.BadRequest(ApiResponse<TextChunkResponse>.Failure(read.ErrorCode, read.Message));
    }

    var textDocument = DecodeTextDocument(read.Data);
    var chunk = BuildTextChunkResponse(request, resource.VirtualPath, textDocument, read.FromOverlay);
    return Results.Ok(ApiResponse<TextChunkResponse>.Success(chunk));
});

app.MapPost("/api/text/chunk/save", async (
    TextChunkSaveRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var resource = await resourceIndex.GetByPathAsync(request.ProfileId, request.VirtualPath, cancellationToken);
    if (resource is null)
    {
        return Results.NotFound(ApiResponse<TextChunkSaveResponse>.Failure("resource_not_found", "未找到资源，请先建立索引。"));
    }

    if (!CanTextChunkResource(resource))
    {
        return Results.BadRequest(ApiResponse<TextChunkSaveResponse>.Failure("not_text_resource", "该资源不是可分块编辑的文本资源。"));
    }

    var read = request.UseOverlay
        ? await ReadResourceBytesPreferOverlayWithSourceAsync(request.ProfileId, request.OodlePath, resource, profiles, nativeContentResolver, overlay, cancellationToken)
        : await ReadResourceBytesWithSourceAsync(request.ProfileId, request.OodlePath, resource, profiles, nativeContentResolver, FromOverlay: false, cancellationToken);
    if (!read.Ok)
    {
        return read.StatusCode == StatusCodes.Status404NotFound
            ? Results.NotFound(ApiResponse<TextChunkSaveResponse>.Failure(read.ErrorCode, read.Message))
            : Results.BadRequest(ApiResponse<TextChunkSaveResponse>.Failure(read.ErrorCode, read.Message));
    }

    try
    {
        var document = DecodeTextDocument(read.Data);
        var updated = ReplaceTextChunk(document, request);
        var textEncoding = request.TextEncoding ?? document.EncodingName;
        var entry = await overlay.SaveTextAsync(new SaveTextOverlayRequest(
            request.ProfileId,
            resource.VirtualPath,
            updated.Text,
            resource.PhysicalPath,
            HasBasePhysicalPath: !NativeBundleResourceContentResolver.IsNativeResource(resource) && resource.PhysicalPath is not null,
            request.OodlePath,
            textEncoding), cancellationToken);
        var response = new TextChunkSaveResponse(
            request.ProfileId,
            resource.VirtualPath,
            updated.StartLine,
            updated.EndLine,
            updated.LineCount,
            updated.TotalLines,
            updated.NewLine,
            textEncoding,
            entry);
        return Results.Ok(ApiResponse<TextChunkSaveResponse>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<TextChunkSaveResponse>.Failure("invalid_text_chunk", ex.Message));
    }
});

app.MapPost("/api/resources/structured-inspect", async (
    StructuredTextInspectRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var resource = await resourceIndex.GetByPathAsync(request.ProfileId, request.VirtualPath, cancellationToken);
    if (resource is null)
    {
        return Results.NotFound(ApiResponse<StructuredTextInspectResponse>.Failure("resource_not_found", "未找到资源，请先建立索引。"));
    }

    if (!CanStructuredEditResource(resource))
    {
        return Results.BadRequest(ApiResponse<StructuredTextInspectResponse>.Failure("not_structured_text", "该资源不是可结构编辑的文本资源。"));
    }

    var read = request.UseOverlay
        ? await ReadResourceBytesPreferOverlayAsync(request.ProfileId, request.OodlePath, resource, profiles, nativeContentResolver, overlay, cancellationToken)
        : await ReadResourceBytesAsync(request.ProfileId, request.OodlePath, resource, profiles, nativeContentResolver, cancellationToken);
    if (!read.Ok)
    {
        return read.StatusCode == StatusCodes.Status404NotFound
            ? Results.NotFound(ApiResponse<StructuredTextInspectResponse>.Failure(read.ErrorCode, read.Message))
            : Results.BadRequest(ApiResponse<StructuredTextInspectResponse>.Failure(read.ErrorCode, read.Message));
    }

    var text = Encoding.UTF8.GetString(read.Data);
    var inspected = InspectStructuredText(resource, text);
    var response = new StructuredTextInspectResponse(
        request.ProfileId,
        resource.VirtualPath,
        resource.Extension.TrimStart('.').ToLowerInvariant(),
        inspected.Nodes.Count,
        inspected.Nodes,
        inspected.Warnings);
    return Results.Ok(ApiResponse<StructuredTextInspectResponse>.Success(response));
});

app.MapPost("/api/resources/structured-save", async (
    StructuredTextSaveRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var resource = await resourceIndex.GetByPathAsync(request.ProfileId, request.VirtualPath, cancellationToken);
    if (resource is null)
    {
        return Results.NotFound(ApiResponse<StructuredTextSaveResponse>.Failure("resource_not_found", "未找到资源，请先建立索引。"));
    }

    if (!CanStructuredEditResource(resource))
    {
        return Results.BadRequest(ApiResponse<StructuredTextSaveResponse>.Failure("not_structured_text", "该资源不是可结构编辑的文本资源。"));
    }

    var read = request.UseOverlay
        ? await ReadResourceBytesPreferOverlayAsync(request.ProfileId, request.OodlePath, resource, profiles, nativeContentResolver, overlay, cancellationToken)
        : await ReadResourceBytesAsync(request.ProfileId, request.OodlePath, resource, profiles, nativeContentResolver, cancellationToken);
    if (!read.Ok)
    {
        return read.StatusCode == StatusCodes.Status404NotFound
            ? Results.NotFound(ApiResponse<StructuredTextSaveResponse>.Failure(read.ErrorCode, read.Message))
            : Results.BadRequest(ApiResponse<StructuredTextSaveResponse>.Failure(read.ErrorCode, read.Message));
    }

    try
    {
        var text = Encoding.UTF8.GetString(read.Data);
        var edited = ApplyStructuredTextEdits(resource, text, request.Edits);
        var entry = await overlay.SaveTextAsync(new SaveTextOverlayRequest(
            request.ProfileId,
            resource.VirtualPath,
            edited.Text,
            resource.PhysicalPath,
            HasBasePhysicalPath: !NativeBundleResourceContentResolver.IsNativeResource(resource) && resource.PhysicalPath is not null),
            cancellationToken);
        var response = new StructuredTextSaveResponse(
            request.ProfileId,
            resource.VirtualPath,
            edited.Edited,
            entry,
            edited.Warnings);
        return Results.Ok(ApiResponse<StructuredTextSaveResponse>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<StructuredTextSaveResponse>.Failure("invalid_structured_edit", ex.Message));
    }
});

app.MapPost("/api/overlay/save-text", async (
    SaveTextOverlayRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var resource = await resourceIndex.GetByPathAsync(request.ProfileId, request.VirtualPath, cancellationToken);
    var basePath = request.BasePhysicalPath ?? resource?.PhysicalPath;
    var textEncoding = request.TextEncoding;
    if (textEncoding is null && resource is not null)
    {
        textEncoding = await DetectResourceTextEncodingAsync(
            request.ProfileId,
            request.OodlePath,
            resource,
            profiles,
            nativeContentResolver,
            cancellationToken);
    }

    var saveRequest = request with { BasePhysicalPath = basePath, HasBasePhysicalPath = basePath is not null, TextEncoding = textEncoding };
    try
    {
        var response = await overlay.SaveTextAsync(saveRequest, cancellationToken);
        return Results.Ok(ApiResponse<OverlayEntryDto>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<OverlayEntryDto>.Failure("invalid_virtual_path", ex.Message));
    }
});

app.MapPost("/api/overlay/save-binary", async (
    SaveBinaryOverlayRequest request,
    ResourceIndexStore resourceIndex,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var resource = await resourceIndex.GetByPathAsync(request.ProfileId, request.VirtualPath, cancellationToken);
    var basePath = request.BasePhysicalPath ?? resource?.PhysicalPath;
    byte[] content;
    try
    {
        content = Convert.FromBase64String(request.Base64Content);
    }
    catch (FormatException)
    {
        return Results.BadRequest(ApiResponse<OverlayEntryDto>.Failure("invalid_base64", "二进制内容不是合法 Base64。"));
    }

    try
    {
        var response = await overlay.SaveBytesAsync(
            request.ProfileId,
            request.VirtualPath,
            content,
            basePath,
            HasBasePhysicalPath: basePath is not null,
            cancellationToken);
        return Results.Ok(ApiResponse<OverlayEntryDto>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<OverlayEntryDto>.Failure("invalid_virtual_path", ex.Message));
    }
});

app.MapPost("/api/overlay/save-file", async (
    HttpRequest httpRequest,
    ResourceIndexStore resourceIndex,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    if (!httpRequest.HasFormContentType)
    {
        return Results.BadRequest(ApiResponse<OverlayEntryDto>.Failure("invalid_form", "资源替换需要 multipart 表单。"));
    }

    var form = await httpRequest.ReadFormAsync(cancellationToken);
    var profileId = form["profileId"].ToString();
    var virtualPath = form["virtualPath"].ToString();
    if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(virtualPath))
    {
        return Results.BadRequest(ApiResponse<OverlayEntryDto>.Failure("invalid_form", "缺少 profileId 或 virtualPath。"));
    }

    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(ApiResponse<OverlayEntryDto>.Failure("empty_file", "请选择替换文件。"));
    }

    var resource = await resourceIndex.GetByPathAsync(profileId, virtualPath, cancellationToken);
    var basePath = resource?.PhysicalPath;
    await using var stream = file.OpenReadStream();
    using var memory = new MemoryStream(file.Length > int.MaxValue ? 0 : (int)file.Length);
    await stream.CopyToAsync(memory, cancellationToken);

    try
    {
        var response = await overlay.SaveBytesAsync(
            profileId,
            virtualPath,
            memory.ToArray(),
            basePath,
            HasBasePhysicalPath: basePath is not null,
            cancellationToken);
        return Results.Ok(ApiResponse<OverlayEntryDto>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<OverlayEntryDto>.Failure("invalid_virtual_path", ex.Message));
    }
}).DisableAntiforgery();

app.MapPost("/api/overlay/list", async (
    OverlayListRequest request,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var response = await overlay.ListAsync(request.ProfileId, cancellationToken);
    return Results.Ok(ApiResponse<OverlayListResponse>.Success(response));
});

app.MapPost("/api/overlay/sync-external", async (
    OverlaySyncExternalRequest request,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await overlay.SyncExternalAsync(request, cancellationToken);
        return Results.Ok(ApiResponse<OverlaySyncExternalResponse>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<OverlaySyncExternalResponse>.Failure("invalid_overlay_sync", ex.Message));
    }
});

app.MapPost("/api/overlay/audit", async (
    OverlayAuditRequest request,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var response = await overlay.AuditAsync(request, cancellationToken);
    return Results.Ok(ApiResponse<OverlayAuditResponse>.Success(response));
});

app.MapPost("/api/overlay/diff", async (
    OverlayDiffRequest request,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await overlay.DiffAsync(request, cancellationToken);
        return Results.Ok(ApiResponse<OverlayDiffResponse>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<OverlayDiffResponse>.Failure("invalid_virtual_path", ex.Message));
    }
});

app.MapPost("/api/overlay/review", async (
    OverlayReviewRequest request,
    OverlayReviewService review,
    CancellationToken cancellationToken) =>
{
    var response = await review.ReviewAsync(request, cancellationToken);
    return Results.Ok(ApiResponse<OverlayReviewResponse>.Success(response));
});

app.MapPost("/api/overlay/revert", async (
    RevertOverlayRequest request,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await overlay.RevertAsync(request, cancellationToken);
        return Results.Ok(ApiResponse<RevertOverlayResponse>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<RevertOverlayResponse>.Failure("invalid_virtual_path", ex.Message));
    }
});

app.MapPost("/api/overlay/bulk-revert", async (
    OverlayBulkRevertRequest request,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var list = await overlay.ListAsync(request.ProfileId, cancellationToken);
    var matched = list.Items
        .Where(item => request.RiskLevel is null || PatchRiskClassifier.Classify(item.VirtualPath) == request.RiskLevel.Value)
        .Take(Math.Clamp(request.Take, 1, 5000))
        .ToArray();
    var removed = new List<string>();
    var warnings = new List<string>();

    foreach (var item in matched)
    {
        try
        {
            var result = await overlay.RevertAsync(new RevertOverlayRequest(request.ProfileId, item.VirtualPath), cancellationToken);
            if (result.Removed)
            {
                removed.Add(item.VirtualPath);
            }
        }
        catch (ArgumentException ex)
        {
            warnings.Add($"{item.VirtualPath}: {ex.Message}");
        }
    }

    return Results.Ok(ApiResponse<OverlayBulkRevertResponse>.Success(new OverlayBulkRevertResponse(
        request.ProfileId,
        matched.Length,
        removed.Count,
        removed,
        warnings)));
});

app.MapPost("/api/overlay/batch-save-text", async (
    BatchSaveTextOverlayRequest request,
    ResourceIndexStore resourceIndex,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var search = await resourceIndex.SearchAsync(new ResourceSearchRequest(
        request.ProfileId,
        Query: request.Query,
        Skip: 0,
        Take: Math.Clamp(request.Take, 1, 200)), cancellationToken);
    var saved = new List<string>();
    var warnings = new List<string>();

    foreach (var resource in search.Items)
    {
        if (resource.Kind is not (ResourceKind.Text or ResourceKind.Ui))
        {
            warnings.Add($"跳过非文本资源：{resource.VirtualPath}");
            continue;
        }

        await overlay.SaveTextAsync(new SaveTextOverlayRequest(
            request.ProfileId,
            resource.VirtualPath,
            request.Text,
            resource.PhysicalPath,
            HasBasePhysicalPath: resource.PhysicalPath is not null), cancellationToken);
        saved.Add(resource.VirtualPath);
    }

    var response = new BatchSaveTextOverlayResponse(
        request.ProfileId,
        search.Total,
        saved.Count,
        saved,
        warnings);
    return Results.Ok(ApiResponse<BatchSaveTextOverlayResponse>.Success(response));
});

app.MapPost("/api/overlay/batch-replace-text", async (
    BatchReplaceTextOverlayRequest request,
    ResourceIndexStore resourceIndex,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrEmpty(request.Find))
    {
        return Results.BadRequest(ApiResponse<BatchReplaceTextOverlayResponse>.Failure("empty_find", "查找内容不能为空。"));
    }

    var search = await resourceIndex.SearchAsync(new ResourceSearchRequest(
        request.ProfileId,
        Query: request.Query,
        Skip: 0,
        Take: Math.Clamp(request.Take, 1, 200)), cancellationToken);
    var changed = new List<string>();
    var warnings = new List<string>();

    foreach (var resource in search.Items)
    {
        if (resource.Kind is not (ResourceKind.Text or ResourceKind.Ui))
        {
            warnings.Add($"跳过非文本资源：{resource.VirtualPath}");
            continue;
        }

        if (string.IsNullOrWhiteSpace(resource.PhysicalPath) || !File.Exists(resource.PhysicalPath))
        {
            warnings.Add($"跳过尚不可批量读取的资源：{resource.VirtualPath}");
            continue;
        }

        var text = await File.ReadAllTextAsync(resource.PhysicalPath, cancellationToken);
        if (!text.Contains(request.Find, StringComparison.Ordinal))
        {
            continue;
        }

        var replaced = text.Replace(request.Find, request.Replace, StringComparison.Ordinal);
        await overlay.SaveTextAsync(new SaveTextOverlayRequest(
            request.ProfileId,
            resource.VirtualPath,
            replaced,
            resource.PhysicalPath,
            HasBasePhysicalPath: true), cancellationToken);
        changed.Add(resource.VirtualPath);
    }

    var response = new BatchReplaceTextOverlayResponse(
        request.ProfileId,
        search.Total,
        changed.Count,
        changed,
        warnings);
    return Results.Ok(ApiResponse<BatchReplaceTextOverlayResponse>.Success(response));
});

app.MapPost("/api/translation/export-csv", async (
    TranslationExportRequest request,
    ResourceIndexStore resourceIndex,
    CancellationToken cancellationToken) =>
{
    var search = await resourceIndex.SearchAsync(new ResourceSearchRequest(
        request.ProfileId,
        Query: request.Query,
        Skip: 0,
        Take: Math.Clamp(request.Take, 1, 500)), cancellationToken);
    var warnings = new List<string>();
    var entries = new List<TranslationEntryDto>();

    foreach (var resource in search.Items)
    {
        if (resource.Kind is not (ResourceKind.Text or ResourceKind.Ui))
        {
            warnings.Add($"跳过非文本资源：{resource.VirtualPath}");
            continue;
        }

        if (string.IsNullOrWhiteSpace(resource.PhysicalPath) || !File.Exists(resource.PhysicalPath))
        {
            warnings.Add($"跳过尚不可导出的资源：{resource.VirtualPath}");
            continue;
        }

        var text = await File.ReadAllTextAsync(resource.PhysicalPath, cancellationToken);
        entries.Add(new TranslationEntryDto(resource.VirtualPath, text, string.Empty, "new"));
    }

    var response = new TranslationExportResponse(
        request.ProfileId,
        search.Total,
        entries.Count,
        TranslationCsv.Write(entries),
        warnings);
    return Results.Ok(ApiResponse<TranslationExportResponse>.Success(response));
});

app.MapPost("/api/translation/import-csv", async (
    TranslationImportRequest request,
    ResourceIndexStore resourceIndex,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var entries = TranslationCsv.Read(request.Csv);
    var applied = new List<string>();
    var warnings = new List<string>();

    foreach (var entry in entries)
    {
        if (string.IsNullOrWhiteSpace(entry.TargetText))
        {
            continue;
        }

        if (string.Equals(entry.SourceText, entry.TargetText, StringComparison.Ordinal))
        {
            continue;
        }

        var resource = await resourceIndex.GetByPathAsync(request.ProfileId, entry.VirtualPath, cancellationToken);
        if (resource is null)
        {
            warnings.Add($"资源不存在：{entry.VirtualPath}");
            continue;
        }

        if (resource.Kind is not (ResourceKind.Text or ResourceKind.Ui))
        {
            warnings.Add($"跳过非文本资源：{entry.VirtualPath}");
            continue;
        }

        await overlay.SaveTextAsync(new SaveTextOverlayRequest(
            request.ProfileId,
            resource.VirtualPath,
            entry.TargetText,
            resource.PhysicalPath,
            HasBasePhysicalPath: resource.PhysicalPath is not null), cancellationToken);
        applied.Add(resource.VirtualPath);
    }

    var response = new TranslationImportResponse(
        request.ProfileId,
        entries.Count,
        applied.Count,
        applied,
        warnings);
    return Results.Ok(ApiResponse<TranslationImportResponse>.Success(response));
});

app.MapPost("/api/translation/apply-glossary", (
    TranslationApplyGlossaryRequest request) =>
{
    var response = TranslationCsv.ApplyGlossary(request);
    return Results.Ok(ApiResponse<TranslationApplyGlossaryResponse>.Success(response));
});

app.MapPost("/api/batch/run-script", async (
    BatchScriptRunRequest request,
    ResourceIndexStore resourceIndex,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var result = await RunBatchScriptAsync(request, resourceIndex, overlay, cancellationToken);
    if (!result.Ok)
    {
        return Results.BadRequest(ApiResponse<BatchScriptRunResponse>.Failure(result.ErrorCode, result.Message));
    }

    return Results.Ok(ApiResponse<BatchScriptRunResponse>.Success(result.Response!));
});

app.MapPost("/api/batch/templates/save", async (
    BatchScriptTemplateSaveRequest request,
    BatchScriptTemplateStore templates,
    CancellationToken cancellationToken) =>
{
    try
    {
        var item = await templates.SaveAsync(request, cancellationToken);
        return Results.Ok(ApiResponse<BatchScriptTemplateDto>.Success(item));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<BatchScriptTemplateDto>.Failure("invalid_batch_template", ex.Message));
    }
});

app.MapPost("/api/batch/templates/list", async (
    BatchScriptTemplateListRequest request,
    BatchScriptTemplateStore templates,
    CancellationToken cancellationToken) =>
{
    var response = await templates.ListAsync(request, cancellationToken);
    return Results.Ok(ApiResponse<BatchScriptTemplateListResponse>.Success(response));
});

app.MapPost("/api/batch/templates/delete", async (
    BatchScriptTemplateDeleteRequest request,
    BatchScriptTemplateStore templates,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await templates.DeleteAsync(request, cancellationToken);
        return Results.Ok(ApiResponse<BatchScriptTemplateDeleteResponse>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<BatchScriptTemplateDeleteResponse>.Failure("invalid_batch_template", ex.Message));
    }
});

app.MapPost("/api/batch/run-template", async (
    BatchScriptTemplateRunRequest request,
    BatchScriptTemplateStore templates,
    ResourceIndexStore resourceIndex,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    BatchScriptTemplateDto? template;
    try
    {
        template = await templates.GetAsync(request.ProfileId, request.TemplateId, cancellationToken);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<BatchScriptRunResponse>.Failure("invalid_batch_template", ex.Message));
    }

    if (template is null)
    {
        return Results.NotFound(ApiResponse<BatchScriptRunResponse>.Failure("batch_template_not_found", "未找到批处理模板。"));
    }

    var result = await RunBatchScriptAsync(
        new BatchScriptRunRequest(request.ProfileId, template.Operations, request.Apply, request.UseOverlay),
        resourceIndex,
        overlay,
        cancellationToken);
    return result.Ok
        ? Results.Ok(ApiResponse<BatchScriptRunResponse>.Success(result.Response!))
        : Results.BadRequest(ApiResponse<BatchScriptRunResponse>.Failure(result.ErrorCode, result.Message));
});

app.MapPost("/api/tables/inspect", async (
    TableInspectRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    TableSchemaStore tableSchemas,
    CancellationToken cancellationToken) =>
{
    var resource = await resourceIndex.GetByPathAsync(request.ProfileId, request.VirtualPath, cancellationToken);
    if (resource is null)
    {
        return Results.NotFound(ApiResponse<TableInspectResponse>.Failure("resource_not_found", "未找到资源，请先建立索引。"));
    }

    if (resource.Kind != ResourceKind.Table)
    {
        return Results.BadRequest(ApiResponse<TableInspectResponse>.Failure("not_table_resource", "该资源不是表格/数据文件。"));
    }

    var read = await ReadResourceBytesPreferOverlayAsync(
        request.ProfileId,
        request.OodlePath,
        resource,
        profiles,
        nativeContentResolver,
        overlay,
        cancellationToken);
    if (!read.Ok)
    {
        return read.StatusCode == StatusCodes.Status404NotFound
            ? Results.NotFound(ApiResponse<TableInspectResponse>.Failure(read.ErrorCode, read.Message))
            : Results.BadRequest(ApiResponse<TableInspectResponse>.Failure(read.ErrorCode, read.Message));
    }

    var schema = request.Schema;
    if (schema is null && !string.IsNullOrWhiteSpace(request.SchemaId))
    {
        var entry = await tableSchemas.GetAsync(request.ProfileId, request.SchemaId, cancellationToken);
        if (entry is null)
        {
            return Results.NotFound(ApiResponse<TableInspectResponse>.Failure("table_schema_not_found", "未找到表结构。"));
        }

        schema = entry.Schema;
    }

    var response = new TableInspector().Inspect(resource, read.Data, request.Limit, schema);
    return Results.Ok(ApiResponse<TableInspectResponse>.Success(response));
});

app.MapPost("/api/tables/schemas/save", async (
    TableSchemaSaveRequest request,
    TableSchemaStore tableSchemas,
    CancellationToken cancellationToken) =>
{
    try
    {
        var entry = await tableSchemas.SaveAsync(request, cancellationToken);
        return Results.Ok(ApiResponse<TableSchemaEntryDto>.Success(entry));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<TableSchemaEntryDto>.Failure("invalid_table_schema", ex.Message));
    }
});

app.MapPost("/api/tables/schemas/list", async (
    TableSchemaListRequest request,
    TableSchemaStore tableSchemas,
    CancellationToken cancellationToken) =>
{
    var response = await tableSchemas.ListAsync(request, cancellationToken);
    return Results.Ok(ApiResponse<TableSchemaListResponse>.Success(response));
});

app.MapPost("/api/tables/schemas/infer", async (
    TableSchemaInferRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var resource = await resourceIndex.GetByPathAsync(request.ProfileId, request.VirtualPath, cancellationToken);
    if (resource is null)
    {
        return Results.NotFound(ApiResponse<TableSchemaInferResponse>.Failure("resource_not_found", "未找到资源，请先建立索引。"));
    }

    if (resource.Kind != ResourceKind.Table || resource.Extension.Equals(".fmt", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(ApiResponse<TableSchemaInferResponse>.Failure("not_data_table", "请选择 .dat 或 .datc64 数据文件。"));
    }

    var fmtPath = Path.ChangeExtension(resource.NormalizedPath, ".fmt")?.Replace('\\', '/');
    if (string.IsNullOrWhiteSpace(fmtPath))
    {
        return Results.BadRequest(ApiResponse<TableSchemaInferResponse>.Failure("fmt_not_found", "无法推断 fmt 路径。"));
    }

    var fmtResource = await resourceIndex.GetByPathAsync(request.ProfileId, fmtPath, cancellationToken);
    if (fmtResource is null)
    {
        return Results.NotFound(ApiResponse<TableSchemaInferResponse>.Failure("fmt_not_found", "未找到同名 .fmt 文件。"));
    }

    var read = await ReadResourceBytesPreferOverlayAsync(
        request.ProfileId,
        request.OodlePath,
        fmtResource,
        profiles,
        nativeContentResolver,
        overlay,
        cancellationToken);
    if (!read.Ok)
    {
        return read.StatusCode == StatusCodes.Status404NotFound
            ? Results.NotFound(ApiResponse<TableSchemaInferResponse>.Failure(read.ErrorCode, read.Message))
            : Results.BadRequest(ApiResponse<TableSchemaInferResponse>.Failure(read.ErrorCode, read.Message));
    }

    var text = System.Text.Encoding.UTF8.GetString(read.Data);
    var inferred = new TableSchemaInferer().Infer(resource.NormalizedPath, fmtResource.NormalizedPath, text);
    var response = new TableSchemaInferResponse(
        request.ProfileId,
        resource.NormalizedPath,
        inferred.FormatPath,
        inferred.Inferred,
        inferred.Schema,
        inferred.Warnings);
    return Results.Ok(ApiResponse<TableSchemaInferResponse>.Success(response));
});

app.MapPost("/api/tables/schemas/delete", async (
    TableSchemaDeleteRequest request,
    TableSchemaStore tableSchemas,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await tableSchemas.DeleteAsync(request, cancellationToken);
        return Results.Ok(ApiResponse<TableSchemaDeleteResponse>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<TableSchemaDeleteResponse>.Failure("invalid_table_schema", ex.Message));
    }
});

app.MapPost("/api/tables/export-csv", async (
    TableCsvExportRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    TableSchemaStore tableSchemas,
    CancellationToken cancellationToken) =>
{
    var table = await ReadTableForEditAsync(
        request.ProfileId,
        request.VirtualPath,
        request.OodlePath,
        request.Schema,
        request.SchemaId,
        profiles,
        resourceIndex,
        nativeContentResolver,
        overlay,
        tableSchemas,
        cancellationToken,
        requireTable: true,
        maxRows: 1024 * 1024);
    if (!table.Ok)
    {
        return TableReadFailure<TableCsvExportResponse>(table);
    }

    var inspect = table.Inspect!;
    var columns = BuildTableColumns(inspect);
    var csv = BuildCsv(columns, inspect.Rows);
    var response = new TableCsvExportResponse(
        request.ProfileId,
        table.Resource!.VirtualPath,
        inspect.Rows.Count,
        columns,
        csv,
        inspect.Warnings);
    return Results.Ok(ApiResponse<TableCsvExportResponse>.Success(response));
});

app.MapPost("/api/tables/import-csv", async (
    TableCsvImportRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    TableSchemaStore tableSchemas,
    CancellationToken cancellationToken) =>
{
    return await ImportTableCsvAsync(
        request,
        profiles,
        resourceIndex,
        nativeContentResolver,
        overlay,
        tableSchemas,
        cancellationToken);
});

app.MapPost("/api/tables/import-csv-file", async (
    HttpRequest httpRequest,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    TableSchemaStore tableSchemas,
    CancellationToken cancellationToken) =>
{
    if (!httpRequest.HasFormContentType)
    {
        return Results.BadRequest(ApiResponse<TableCsvImportResponse>.Failure("invalid_form", "CSV 导入需要 multipart 表单。"));
    }

    var form = await httpRequest.ReadFormAsync(cancellationToken);
    var profileId = form["profileId"].ToString();
    var virtualPath = form["virtualPath"].ToString();
    var oodlePath = EmptyToNull(form["oodlePath"].ToString());
    var schemaId = EmptyToNull(form["schemaId"].ToString());
    var schemaJson = EmptyToNull(form["schema"].ToString());
    TableSchemaDto? schema = null;
    if (!string.IsNullOrWhiteSpace(schemaJson))
    {
        try
        {
            schema = System.Text.Json.JsonSerializer.Deserialize<TableSchemaDto>(
                schemaJson,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        }
        catch (System.Text.Json.JsonException ex)
        {
            return Results.BadRequest(ApiResponse<TableCsvImportResponse>.Failure("invalid_table_schema", ex.Message));
        }
    }

    var csvFile = form.Files.GetFile("csvFile");
    if (csvFile is null || csvFile.Length == 0)
    {
        return Results.BadRequest(ApiResponse<TableCsvImportResponse>.Failure("empty_csv", "请选择 CSV 文件。"));
    }

    using var reader = new StreamReader(csvFile.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    var csv = await reader.ReadToEndAsync(cancellationToken);
    var request = new TableCsvImportRequest(profileId, virtualPath, csv, oodlePath, schema, schemaId);
    return await ImportTableCsvAsync(
        request,
        profiles,
        resourceIndex,
        nativeContentResolver,
        overlay,
        tableSchemas,
        cancellationToken);
}).DisableAntiforgery();

app.MapPost("/api/tables/reference-scan", async (
    TableReferenceScanRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    TableSchemaStore tableSchemas,
    CancellationToken cancellationToken) =>
{
    var source = await ReadTableForEditAsync(
        request.ProfileId,
        request.VirtualPath,
        request.OodlePath,
        request.Schema,
        request.SchemaId,
        profiles,
        resourceIndex,
        nativeContentResolver,
        overlay,
        tableSchemas,
        cancellationToken,
        requireTable: true,
        maxRows: 1024 * 1024);
    if (!source.Ok)
    {
        return TableReadFailure<TableReferenceScanResponse>(source);
    }

    var target = await ReadTableForEditAsync(
        request.ProfileId,
        request.TargetVirtualPath,
        request.OodlePath,
        request.TargetSchema,
        request.TargetSchemaId,
        profiles,
        resourceIndex,
        nativeContentResolver,
        overlay,
        tableSchemas,
        cancellationToken,
        requireTable: true,
        maxRows: 1024 * 1024);
    if (!target.Ok)
    {
        return TableReadFailure<TableReferenceScanResponse>(target);
    }

    if (request.ColumnIndex < 0 || request.TargetColumnIndex < 0)
    {
        return Results.BadRequest(ApiResponse<TableReferenceScanResponse>.Failure("invalid_column", "列号不能小于 0。"));
    }

    var targetValues = target.Inspect!.Rows
        .Where(row => request.TargetColumnIndex < row.Cells.Count)
        .Select(row => row.Cells[request.TargetColumnIndex])
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var items = new List<TableReferenceItemDto>();
    foreach (var row in source.Inspect!.Rows)
    {
        if (request.ColumnIndex >= row.Cells.Count)
        {
            continue;
        }

        var value = row.Cells[request.ColumnIndex];
        items.Add(new TableReferenceItemDto(row.RowNumber, value, targetValues.Contains(value)));
    }

    var matched = items.Count(item => item.Matched);
    var warnings = source.Inspect.Warnings.Concat(target.Inspect.Warnings).Distinct(StringComparer.Ordinal).ToArray();
    var response = new TableReferenceScanResponse(
        request.ProfileId,
        source.Resource!.VirtualPath,
        target.Resource!.VirtualPath,
        items.Count,
        matched,
        items.Count - matched,
        items,
        warnings);
    return Results.Ok(ApiResponse<TableReferenceScanResponse>.Success(response));
});

app.MapPost("/api/tables/save", async (
    TableSaveRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    TableSchemaStore tableSchemas,
    CancellationToken cancellationToken) =>
{
    var resource = await resourceIndex.GetByPathAsync(request.ProfileId, request.VirtualPath, cancellationToken);
    if (resource is null)
    {
        return Results.NotFound(ApiResponse<TableSaveResponse>.Failure("resource_not_found", "未找到资源，请先建立索引。"));
    }

    if (resource.Kind != ResourceKind.Table)
    {
        return Results.BadRequest(ApiResponse<TableSaveResponse>.Failure("not_table_resource", "该资源不是表格/数据文件。"));
    }

    var read = await ReadResourceBytesPreferOverlayAsync(
        request.ProfileId,
        request.OodlePath,
        resource,
        profiles,
        nativeContentResolver,
        overlay,
        cancellationToken);
    if (!read.Ok)
    {
        return read.StatusCode == StatusCodes.Status404NotFound
            ? Results.NotFound(ApiResponse<TableSaveResponse>.Failure(read.ErrorCode, read.Message))
            : Results.BadRequest(ApiResponse<TableSaveResponse>.Failure(read.ErrorCode, read.Message));
    }

    try
    {
        var inspector = new TableInspector();
        OverlayEntryDto entry;
        var schema = request.Schema;
        if (schema is null && !string.IsNullOrWhiteSpace(request.SchemaId))
        {
            var schemaEntry = await tableSchemas.GetAsync(request.ProfileId, request.SchemaId, cancellationToken);
            if (schemaEntry is null)
            {
                return Results.NotFound(ApiResponse<TableSaveResponse>.Failure("table_schema_not_found", "未找到表结构。"));
            }

            schema = schemaEntry.Schema;
        }

        if (schema is not null)
        {
            var editedBytes = inspector.ApplyCellEdits(resource, read.Data, request.Edits, schema);
            entry = await overlay.SaveBytesAsync(
                request.ProfileId,
                resource.VirtualPath,
                editedBytes,
                resource.PhysicalPath,
                HasBasePhysicalPath: !NativeBundleResourceContentResolver.IsNativeResource(resource) && resource.PhysicalPath is not null,
                cancellationToken);
        }
        else if (resource.Extension.Equals(".datc64", StringComparison.OrdinalIgnoreCase))
        {
            var editResult = inspector.ApplyDatc64CatalogCellEditsWithReport(resource, read.Data, request.Edits);
            if (editResult.Skipped.Count > 0)
            {
                return Results.BadRequest(ApiResponse<TableSaveResponse>.Failure(
                    "table_edit_skipped",
                    $"有 {editResult.Skipped.Count} 个单元格未写入：{string.Join("；", editResult.Skipped.Take(5))}"));
            }

            entry = await overlay.SaveBytesAsync(
                request.ProfileId,
                resource.VirtualPath,
                editResult.Data,
                resource.PhysicalPath,
                HasBasePhysicalPath: !NativeBundleResourceContentResolver.IsNativeResource(resource) && resource.PhysicalPath is not null,
                cancellationToken);
            var datc64Response = new TableSaveResponse(request.ProfileId, resource.VirtualPath, editResult.Applied, entry);
            return Results.Ok(ApiResponse<TableSaveResponse>.Success(datc64Response));
        }
        else
        {
            var inspection = inspector.Inspect(resource, read.Data, request.Edits.Count == 0 ? 65536 : Math.Max(65536, read.Data.Length));
            if (inspection.Delimiter == "legacy-dat-schema")
            {
                var editedBytes = inspector.ApplyLegacyDatCatalogCellEdits(resource, read.Data, request.Edits);
                entry = await overlay.SaveBytesAsync(
                    request.ProfileId,
                    resource.VirtualPath,
                    editedBytes,
                    resource.PhysicalPath,
                    HasBasePhysicalPath: !NativeBundleResourceContentResolver.IsNativeResource(resource) && resource.PhysicalPath is not null,
                    cancellationToken);
            }
            else
            {
                var edited = inspector.ApplyCellEdits(resource, read.Data, request.Edits);
                entry = await overlay.SaveTextAsync(new SaveTextOverlayRequest(
                    request.ProfileId,
                    resource.VirtualPath,
                    edited,
                    resource.PhysicalPath,
                    HasBasePhysicalPath: !NativeBundleResourceContentResolver.IsNativeResource(resource) && resource.PhysicalPath is not null),
                    cancellationToken);
            }
        }

        var response = new TableSaveResponse(request.ProfileId, resource.VirtualPath, request.Edits.Count, entry);
        return Results.Ok(ApiResponse<TableSaveResponse>.Success(response));
    }
    catch (ArgumentOutOfRangeException ex)
    {
        return Results.BadRequest(ApiResponse<TableSaveResponse>.Failure("table_edit_out_of_range", ex.Message));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(ApiResponse<TableSaveResponse>.Failure("table_edit_unsupported", ex.Message));
    }
});

app.MapPost("/api/patch/dry-run", async (
    PatchDryRunRequest request,
    ProfileStore profiles,
    PatchBuildService patchBuild,
    CancellationToken cancellationToken) =>
{
    var profile = await profiles.GetAsync(request.ProfileId, cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(ApiResponse<PatchDryRunResponse>.Failure("profile_not_found", "未找到客户端配置。"));
    }

    var response = await patchBuild.DryRunAsync(request, profile, cancellationToken);
    return Results.Ok(ApiResponse<PatchDryRunResponse>.Success(response));
});

app.MapPost("/api/patch/readiness", async (
    PatchReadinessRequest request,
    ProfileStore profiles,
    PatchBuildService patchBuild,
    CancellationToken cancellationToken) =>
{
    var profile = await profiles.GetAsync(request.ProfileId, cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(ApiResponse<PatchReadinessResponse>.Failure("profile_not_found", "未找到客户端配置。"));
    }

    var response = await patchBuild.CheckReadinessAsync(request, profile, cancellationToken);
    return Results.Ok(ApiResponse<PatchReadinessResponse>.Success(response));
});

app.MapPost("/api/patch/native-plan", async (
    NativePatchPlanRequest request,
    ProfileStore profiles,
    PatchBuildService patchBuild,
    CancellationToken cancellationToken) =>
{
    var profile = await profiles.GetAsync(request.ProfileId, cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(ApiResponse<NativePatchPlanResponse>.Failure("profile_not_found", "未找到客户端配置。"));
    }

    var response = await patchBuild.PlanNativePatchAsync(request, cancellationToken);
    return Results.Ok(ApiResponse<NativePatchPlanResponse>.Success(response));
});

app.MapPost("/api/patch/native-dry-bundle", async (
    NativeDryBundleBuildRequest request,
    ProfileStore profiles,
    PatchBuildService patchBuild,
    CancellationToken cancellationToken) =>
{
    var profile = await profiles.GetAsync(request.ProfileId, cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(ApiResponse<NativeDryBundleBuildResponse>.Failure("profile_not_found", "未找到客户端配置。"));
    }

    try
    {
        var response = await patchBuild.BuildNativeDryBundleAsync(request, cancellationToken);
        return Results.Ok(ApiResponse<NativeDryBundleBuildResponse>.Success(response));
    }
    catch (PatchBuildException ex)
    {
        return Results.BadRequest(ApiResponse<NativeDryBundleBuildResponse>.Failure(ex.ErrorCode, ex.Message));
    }
});

app.MapPost("/api/patch/native-index-plan", async (
    NativeIndexRewritePlanRequest request,
    ProfileStore profiles,
    PatchBuildService patchBuild,
    CancellationToken cancellationToken) =>
{
    var profile = await profiles.GetAsync(request.ProfileId, cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(ApiResponse<NativeIndexRewritePlanResponse>.Failure("profile_not_found", "未找到客户端配置。"));
    }

    var response = await patchBuild.PlanNativeIndexRewriteAsync(request, cancellationToken);
    return Results.Ok(ApiResponse<NativeIndexRewritePlanResponse>.Success(response));
});

app.MapPost("/api/patch/build", async (
    PatchBuildRequest request,
    ProfileStore profiles,
    PatchBuildService patchBuild,
    CancellationToken cancellationToken) =>
{
    var profile = await profiles.GetAsync(request.ProfileId, cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(ApiResponse<PatchBuildResponse>.Failure("profile_not_found", "未找到客户端配置。"));
    }

    try
    {
        var response = await patchBuild.BuildAsync(request, profile, cancellationToken);
        return Results.Ok(ApiResponse<PatchBuildResponse>.Success(response));
    }
    catch (PatchBuildException ex)
    {
        return Results.BadRequest(ApiResponse<PatchBuildResponse>.Failure(ex.ErrorCode, ex.Message));
    }
});

app.MapPost("/api/patch/build-history", async (
    PatchBuildHistoryRequest request,
    ProfileStore profiles,
    WorkspaceRootProvider workspace,
    CancellationToken cancellationToken) =>
{
    var profile = await profiles.GetAsync(request.ProfileId, cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(ApiResponse<PatchBuildHistoryResponse>.Failure("profile_not_found", "未找到客户端配置。"));
    }

    var layout = WorkspaceLayout.ForProfile(workspace.CurrentRoot, request.ProfileId);
    if (!Directory.Exists(layout.BuildsRoot))
    {
        return Results.Ok(ApiResponse<PatchBuildHistoryResponse>.Success(new PatchBuildHistoryResponse(request.ProfileId, [])));
    }

    var items = Directory.EnumerateFiles(layout.BuildsRoot, "*-patch.zip", SearchOption.TopDirectoryOnly)
        .Select(zipPath =>
        {
            var zip = new FileInfo(zipPath);
            var buildId = zip.Name.Split('-', 2)[0];
            var outputDirectory = Path.Combine(layout.BuildsRoot, buildId);
            var manifestPath = Path.Combine(outputDirectory, "patch_manifest.json");
            var rollbackPath = Path.Combine(outputDirectory, "rollback_manifest.json");
            var importManifestPath = Path.Combine(outputDirectory, "import_manifest.json");
            return new PatchBuildHistoryItemDto(
                buildId,
                outputDirectory,
                zip.FullName,
                $"/api/patch/download/{request.ProfileId}/{buildId}",
                File.Exists(manifestPath) ? manifestPath : null,
                File.Exists(rollbackPath) ? rollbackPath : null,
                File.Exists(importManifestPath) ? importManifestPath : null,
                zip.CreationTimeUtc <= DateTime.MinValue ? DateTimeOffset.UtcNow : new DateTimeOffset(zip.CreationTimeUtc, TimeSpan.Zero),
                zip.Length);
        })
        .OrderByDescending(item => item.CreatedAt)
        .Take(20)
        .ToArray();

    return Results.Ok(ApiResponse<PatchBuildHistoryResponse>.Success(new PatchBuildHistoryResponse(request.ProfileId, items)));
});

app.MapPost("/api/patch/import-manifest", async (
    PatchImportManifestRequest request,
    ProfileStore profiles,
    WorkspaceRootProvider workspace,
    CancellationToken cancellationToken) =>
{
    var profile = await profiles.GetAsync(request.ProfileId, cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(ApiResponse<PatchZipImportManifestDto>.Failure("profile_not_found", "未找到客户端配置。"));
    }

    if (request.BuildId.Any(ch => !char.IsDigit(ch)))
    {
        return Results.BadRequest(ApiResponse<PatchZipImportManifestDto>.Failure("invalid_build_id", "构建编号不合法。"));
    }

    var layout = WorkspaceLayout.ForProfile(workspace.CurrentRoot, request.ProfileId);
    var manifestPath = Path.Combine(layout.BuildsRoot, request.BuildId, "import_manifest.json");
    if (!File.Exists(manifestPath))
    {
        return Results.NotFound(ApiResponse<PatchZipImportManifestDto>.Failure("import_manifest_not_found", "未找到导入清单。"));
    }

    await using var stream = File.OpenRead(manifestPath);
    var manifest = await System.Text.Json.JsonSerializer.DeserializeAsync<PatchZipImportManifestDto>(
        stream,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web),
        cancellationToken);
    return manifest is null
        ? Results.BadRequest(ApiResponse<PatchZipImportManifestDto>.Failure("import_manifest_invalid", "导入清单无法解析。"))
        : Results.Ok(ApiResponse<PatchZipImportManifestDto>.Success(manifest));
});

app.MapPost("/api/patch/verify", async (
    PatchVerifyRequest request,
    ProfileStore profiles,
    PatchBuildService patchBuild,
    CancellationToken cancellationToken) =>
{
    var profile = await profiles.GetAsync(request.ProfileId, cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(ApiResponse<PatchVerifyResponse>.Failure("profile_not_found", "未找到客户端配置。"));
    }

    try
    {
        var response = await patchBuild.VerifyBuildAsync(request, cancellationToken);
        return Results.Ok(ApiResponse<PatchVerifyResponse>.Success(response));
    }
    catch (PatchBuildException ex)
    {
        return Results.BadRequest(ApiResponse<PatchVerifyResponse>.Failure(ex.ErrorCode, ex.Message));
    }
});

app.MapPost("/api/patch/analyze-zip", async (
    PatchZipAnalyzeRequest request,
    PatchImportAnalyzer analyzer,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await analyzer.AnalyzeZipAsync(request, cancellationToken);
        return Results.Ok(ApiResponse<PatchZipAnalyzeResponse>.Success(response));
    }
    catch (PatchBuildException ex)
    {
        return Results.BadRequest(ApiResponse<PatchZipAnalyzeResponse>.Failure(ex.ErrorCode, ex.Message));
    }
});

app.MapPost("/api/jobs/patch/analyze-zip", (
    PatchZipAnalyzeRequest request,
    InMemoryJobStore jobs,
    IServiceScopeFactory scopeFactory) =>
{
    var job = jobs.Create("patch-analyze-zip", "任务已创建，等待开始。");
    _ = Task.Run(async () =>
    {
        using var scope = scopeFactory.CreateScope();
        var scopedJobs = scope.ServiceProvider.GetRequiredService<InMemoryJobStore>();
        try
        {
            scopedJobs.Update(job.Id, JobStatus.Running, 10, "正在打开补丁包。");
            var analyzer = scope.ServiceProvider.GetRequiredService<PatchImportAnalyzer>();
            scopedJobs.Update(job.Id, JobStatus.Running, 45, "正在识别 Bundles2 文件。");
            var response = await analyzer.AnalyzeZipAsync(request, CancellationToken.None);
            scopedJobs.Succeed(job.Id, response.Ok ? "补丁分析通过。" : "补丁分析完成，存在警告。", System.Text.Json.JsonSerializer.Serialize(response));
        }
        catch (PatchBuildException ex)
        {
            scopedJobs.Fail(job.Id, ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            scopedJobs.Fail(job.Id, "job_failed", ex.Message);
        }
    });

    return Results.Ok(ApiResponse<JobSnapshotDto>.Success(job));
});

app.MapPost("/api/jobs/patch/import-zip", (
    PatchZipImportRequest request,
    InMemoryJobStore jobs,
    IServiceScopeFactory scopeFactory) =>
{
    var job = jobs.Create("patch-import-zip", "任务已创建，等待开始。");
    _ = Task.Run(async () =>
    {
        using var scope = scopeFactory.CreateScope();
        var scopedJobs = scope.ServiceProvider.GetRequiredService<InMemoryJobStore>();
        try
        {
            scopedJobs.Update(job.Id, JobStatus.Running, 10, "正在检查客户端配置。");
            var profiles = scope.ServiceProvider.GetRequiredService<ProfileStore>();
            var profile = await profiles.GetAsync(request.ProfileId, CancellationToken.None);
            if (profile is null)
            {
                scopedJobs.Fail(job.Id, "profile_not_found", "未找到客户端配置。");
                return;
            }

            scopedJobs.Update(job.Id, JobStatus.Running, 35, "正在分析外部补丁。");
            var importer = scope.ServiceProvider.GetRequiredService<PatchZipImportService>();
            var response = await importer.ImportAsync(request, CancellationToken.None);
            scopedJobs.Succeed(job.Id, "外部补丁已导入。", System.Text.Json.JsonSerializer.Serialize(response));
        }
        catch (PatchBuildException ex)
        {
            scopedJobs.Fail(job.Id, ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            scopedJobs.Fail(job.Id, "job_failed", ex.Message);
        }
    });

    return Results.Ok(ApiResponse<JobSnapshotDto>.Success(job));
});

app.MapPost("/api/jobs/patch/preview-zip-install", (
    PatchZipInstallPreviewRequest request,
    InMemoryJobStore jobs,
    IServiceScopeFactory scopeFactory) =>
{
    var job = jobs.Create("patch-preview-zip-install", "任务已创建，等待开始。");
    _ = Task.Run(async () =>
    {
        using var scope = scopeFactory.CreateScope();
        var scopedJobs = scope.ServiceProvider.GetRequiredService<InMemoryJobStore>();
        try
        {
            scopedJobs.Update(job.Id, JobStatus.Running, 10, "正在读取客户端配置。");
            var profiles = scope.ServiceProvider.GetRequiredService<ProfileStore>();
            var profile = await profiles.GetAsync(request.ProfileId, CancellationToken.None);
            if (profile is null)
            {
                scopedJobs.Fail(job.Id, "profile_not_found", "未找到客户端配置。");
                return;
            }

            scopedJobs.Update(job.Id, JobStatus.Running, 40, "正在比较补丁与客户端文件。");
            var preview = scope.ServiceProvider.GetRequiredService<PatchZipInstallPreviewService>();
            var response = await preview.PreviewAsync(request, profile, CancellationToken.None);
            scopedJobs.Succeed(job.Id, "补丁影响预检已完成。", System.Text.Json.JsonSerializer.Serialize(response));
        }
        catch (PatchBuildException ex)
        {
            scopedJobs.Fail(job.Id, ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            scopedJobs.Fail(job.Id, "job_failed", ex.Message);
        }
    });

    return Results.Ok(ApiResponse<JobSnapshotDto>.Success(job));
});

app.MapPost("/api/jobs/patch/import-overlay-draft", (
    PatchOverlayDraftRequest request,
    InMemoryJobStore jobs,
    IServiceScopeFactory scopeFactory) =>
{
    var job = jobs.Create("patch-import-overlay-draft", "任务已创建，等待开始。");
    _ = Task.Run(async () =>
    {
        using var scope = scopeFactory.CreateScope();
        var scopedJobs = scope.ServiceProvider.GetRequiredService<InMemoryJobStore>();
        try
        {
            scopedJobs.Update(job.Id, JobStatus.Running, 10, "正在读取客户端配置。");
            var profiles = scope.ServiceProvider.GetRequiredService<ProfileStore>();
            var profile = await profiles.GetAsync(request.ProfileId, CancellationToken.None);
            if (profile is null)
            {
                scopedJobs.Fail(job.Id, "profile_not_found", "未找到客户端配置。");
                return;
            }

            scopedJobs.Update(job.Id, JobStatus.Running, 40, "正在提取补丁 bundle。");
            var service = scope.ServiceProvider.GetRequiredService<PatchOverlayDraftService>();
            var response = await service.ImportDraftAsync(request, CancellationToken.None);
            scopedJobs.Succeed(job.Id, "外部补丁已转成 overlay 草稿。", System.Text.Json.JsonSerializer.Serialize(response));
        }
        catch (PatchBuildException ex)
        {
            scopedJobs.Fail(job.Id, ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            scopedJobs.Fail(job.Id, "job_failed", ex.Message);
        }
    });

    return Results.Ok(ApiResponse<JobSnapshotDto>.Success(job));
});

app.MapPost("/api/jobs/patch/sandbox-prepare", (
    PatchSandboxPrepareRequest request,
    InMemoryJobStore jobs,
    IServiceScopeFactory scopeFactory) =>
{
    var job = jobs.Create("patch-sandbox-prepare", "任务已创建，等待开始。");
    _ = Task.Run(async () =>
    {
        using var scope = scopeFactory.CreateScope();
        var scopedJobs = scope.ServiceProvider.GetRequiredService<InMemoryJobStore>();
        try
        {
            scopedJobs.Update(job.Id, JobStatus.Running, 10, "正在读取客户端配置。");
            var profiles = scope.ServiceProvider.GetRequiredService<ProfileStore>();
            var profile = await profiles.GetAsync(request.ProfileId, CancellationToken.None);
            if (profile is null)
            {
                scopedJobs.Fail(job.Id, "profile_not_found", "未找到客户端配置。");
                return;
            }

            scopedJobs.Update(job.Id, JobStatus.Running, 35, "正在复制客户端沙盒骨架。");
            var service = scope.ServiceProvider.GetRequiredService<PatchBuildService>();
            var response = await service.PrepareSandboxAsync(request, profile, CancellationToken.None);
            scopedJobs.Update(job.Id, JobStatus.Running, 85, "正在校验沙盒补丁文件。");
            scopedJobs.Succeed(job.Id, response.Ok ? "沙盒已准备完成。" : "沙盒准备完成，但存在警告。", System.Text.Json.JsonSerializer.Serialize(response));
        }
        catch (ArgumentException ex)
        {
            scopedJobs.Fail(job.Id, "invalid_sandbox_target", ex.Message);
        }
        catch (Exception ex)
        {
            scopedJobs.Fail(job.Id, "job_failed", ex.Message);
        }
    });

    return Results.Ok(ApiResponse<JobSnapshotDto>.Success(job));
});

app.MapPost("/api/jobs/patch/pipeline-run", (
    PatchPipelineRunRequest request,
    InMemoryJobStore jobs,
    IServiceScopeFactory scopeFactory) =>
{
    var job = jobs.Create("patch-pipeline-run", "任务已创建，等待开始。");
    _ = Task.Run(async () =>
    {
        using var scope = scopeFactory.CreateScope();
        var scopedJobs = scope.ServiceProvider.GetRequiredService<InMemoryJobStore>();
        try
        {
            scopedJobs.Update(job.Id, JobStatus.Running, 8, "正在读取迁移方案。");
            var profiles = scope.ServiceProvider.GetRequiredService<ProfileStore>();
            var migrationPlans = scope.ServiceProvider.GetRequiredService<MigrationPlanStore>();
            var resourceIndex = scope.ServiceProvider.GetRequiredService<ResourceIndexStore>();
            var nativeContentResolver = scope.ServiceProvider.GetRequiredService<NativeBundleResourceContentResolver>();
            var overlay = scope.ServiceProvider.GetRequiredService<OverlayStore>();
            var patchBuild = scope.ServiceProvider.GetRequiredService<PatchBuildService>();
            var plan = await migrationPlans.GetAsync(request.SourceProfileId, request.MigrationPlanId, CancellationToken.None);
            if (plan is null)
            {
                scopedJobs.Fail(job.Id, "migration_plan_not_found", "未找到迁移方案。");
                return;
            }

            if (!string.Equals(plan.Criteria.TargetProfileId, request.TargetProfileId, StringComparison.OrdinalIgnoreCase))
            {
                scopedJobs.Fail(job.Id, "target_profile_mismatch", "迁移方案目标配置与流水线目标配置不一致。");
                return;
            }

            var targetProfile = await profiles.GetAsync(request.TargetProfileId, CancellationToken.None);
            if (targetProfile is null)
            {
                scopedJobs.Fail(job.Id, "profile_not_found", "未找到目标客户端配置。");
                return;
            }

            scopedJobs.Update(job.Id, JobStatus.Running, 20, "正在校验迁移方案。");
            var validation = await ValidateSavedMigrationPlanAsync(
                new ResourceMigrationPlanValidateRequest(request.SourceProfileId, request.MigrationPlanId, SourceOodlePath: request.OodlePath, TargetOodlePath: request.OodlePath),
                plan,
                profiles,
                resourceIndex,
                nativeContentResolver,
                overlay,
                CancellationToken.None);

            scopedJobs.Update(job.Id, JobStatus.Running, 40, "正在写入目标覆盖层。");
            var migration = await ApplySavedMigrationPlanAsync(
                new ResourceMigrationPlanApplyRequest(
                    request.SourceProfileId,
                    request.MigrationPlanId,
                    IncludeHashMatches: true,
                    IncludeCandidates: request.IncludeCandidates,
                    MaxRiskLevel: request.MaxRiskLevel,
                    UseOverlay: true,
                    SourceOodlePath: request.OodlePath),
                plan,
                profiles,
                resourceIndex,
                nativeContentResolver,
                overlay,
                CancellationToken.None);

            scopedJobs.Update(job.Id, JobStatus.Running, 65, "正在生成补丁包。");
            var build = await patchBuild.BuildAsync(
                new PatchBuildRequest(request.TargetProfileId, request.Template, request.BundleName, request.WriterKind, request.OodlePath),
                targetProfile,
                CancellationToken.None);

            PatchSandboxPrepareResponse? sandbox = null;
            if (!string.IsNullOrWhiteSpace(request.SandboxRootPath))
            {
                scopedJobs.Update(job.Id, JobStatus.Running, 85, "正在准备沙盒验证。");
                var buildId = new DirectoryInfo(build.OutputDirectory).Name;
                sandbox = await patchBuild.PrepareSandboxAsync(
                    new PatchSandboxPrepareRequest(request.TargetProfileId, buildId, request.SandboxRootPath),
                    targetProfile,
                    CancellationToken.None);
            }

            var warnings = validation.Warnings
                .Concat(migration.Warnings)
                .Concat(build.Warnings)
                .Concat(sandbox?.Warnings ?? [])
                .ToArray();
            var response = new PatchPipelineRunResponse(
                request.SourceProfileId,
                request.TargetProfileId,
                request.MigrationPlanId,
                Ok: validation.Blocked == 0 && migration.Drafted > 0 && build.TotalChanges > 0 && (sandbox is null || sandbox.Ok),
                validation,
                migration,
                build,
                sandbox,
                warnings);
            scopedJobs.Succeed(job.Id, $"流水线完成：写入 {migration.Drafted}，补丁 {build.TotalChanges} 项。", System.Text.Json.JsonSerializer.Serialize(response));
        }
        catch (PatchBuildException ex)
        {
            scopedJobs.Fail(job.Id, ex.ErrorCode, ex.Message);
        }
        catch (ArgumentException ex)
        {
            scopedJobs.Fail(job.Id, "invalid_pipeline_request", ex.Message);
        }
        catch (Exception ex)
        {
            scopedJobs.Fail(job.Id, "job_failed", ex.Message);
        }
    });

    return Results.Ok(ApiResponse<JobSnapshotDto>.Success(job));
});

app.MapPost("/api/patch/install", async (
    PatchInstallRequest request,
    ProfileStore profiles,
    PatchBuildService patchBuild,
    CancellationToken cancellationToken) =>
{
    var profile = await profiles.GetAsync(request.ProfileId, cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(ApiResponse<PatchInstallResponse>.Failure("profile_not_found", "未找到客户端配置。"));
    }

    try
    {
        var response = await patchBuild.InstallAsync(request, profile, cancellationToken);
        return Results.Ok(ApiResponse<PatchInstallResponse>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<PatchInstallResponse>.Failure("invalid_install_target", ex.Message));
    }
});

app.MapPost("/api/patch/uninstall", async (
    PatchUninstallRequest request,
    ProfileStore profiles,
    PatchBuildService patchBuild,
    CancellationToken cancellationToken) =>
{
    var profile = await profiles.GetAsync(request.ProfileId, cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(ApiResponse<PatchUninstallResponse>.Failure("profile_not_found", "未找到客户端配置。"));
    }

    var response = await patchBuild.UninstallAsync(request, profile, cancellationToken);
    return Results.Ok(ApiResponse<PatchUninstallResponse>.Success(response));
});

app.MapPost("/api/patch/sandbox-validate", async (
    PatchSandboxValidateRequest request,
    ProfileStore profiles,
    PatchBuildService patchBuild,
    CancellationToken cancellationToken) =>
{
    var profile = await profiles.GetAsync(request.ProfileId, cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(ApiResponse<PatchSandboxValidateResponse>.Failure("profile_not_found", "未找到客户端配置。"));
    }

    try
    {
        var response = await patchBuild.ValidateInSandboxAsync(request, cancellationToken);
        return Results.Ok(ApiResponse<PatchSandboxValidateResponse>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<PatchSandboxValidateResponse>.Failure("invalid_sandbox_target", ex.Message));
    }
});

app.MapPost("/api/patch/sandbox-prepare", async (
    PatchSandboxPrepareRequest request,
    ProfileStore profiles,
    PatchBuildService patchBuild,
    CancellationToken cancellationToken) =>
{
    var profile = await profiles.GetAsync(request.ProfileId, cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(ApiResponse<PatchSandboxPrepareResponse>.Failure("profile_not_found", "未找到客户端配置。"));
    }

    try
    {
        var response = await patchBuild.PrepareSandboxAsync(request, profile, cancellationToken);
        return Results.Ok(ApiResponse<PatchSandboxPrepareResponse>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<PatchSandboxPrepareResponse>.Failure("invalid_sandbox_target", ex.Message));
    }
});

app.MapPost("/api/jobs/resources/migration-draft", (
    ResourceMigrationDraftRequest request,
    InMemoryJobStore jobs,
    IServiceScopeFactory scopeFactory) =>
{
    var job = jobs.Create("resources-migration-draft", "任务已创建，等待开始。");
    _ = Task.Run(async () =>
    {
        using var scope = scopeFactory.CreateScope();
        var scopedJobs = scope.ServiceProvider.GetRequiredService<InMemoryJobStore>();
        try
        {
            scopedJobs.Update(job.Id, JobStatus.Running, 10, "正在读取迁移特征。");
            var profiles = scope.ServiceProvider.GetRequiredService<ProfileStore>();
            var resourceIndex = scope.ServiceProvider.GetRequiredService<ResourceIndexStore>();
            var nativeContentResolver = scope.ServiceProvider.GetRequiredService<NativeBundleResourceContentResolver>();
            var overlay = scope.ServiceProvider.GetRequiredService<OverlayStore>();
            scopedJobs.Update(job.Id, JobStatus.Running, 35, "正在生成迁移建议。");
            var response = await BuildMigrationDraftAsync(
                request,
                profiles,
                resourceIndex,
                nativeContentResolver,
                overlay,
                CancellationToken.None);
            scopedJobs.Succeed(job.Id, $"迁移草稿完成：写入 {response.Drafted}，跳过 {response.Skipped}。", System.Text.Json.JsonSerializer.Serialize(response));
        }
        catch (Exception ex)
        {
            scopedJobs.Fail(job.Id, "job_failed", ex.Message);
        }
    });

    return Results.Ok(ApiResponse<JobSnapshotDto>.Success(job));
});

app.MapPost("/api/jobs/resources/migration-plan-apply", (
    ResourceMigrationPlanApplyRequest request,
    InMemoryJobStore jobs,
    IServiceScopeFactory scopeFactory) =>
{
    var job = jobs.Create("resources-migration-plan-apply", "任务已创建，等待开始。");
    _ = Task.Run(async () =>
    {
        using var scope = scopeFactory.CreateScope();
        var scopedJobs = scope.ServiceProvider.GetRequiredService<InMemoryJobStore>();
        try
        {
            scopedJobs.Update(job.Id, JobStatus.Running, 10, "正在加载迁移方案。");
            var migrationPlans = scope.ServiceProvider.GetRequiredService<MigrationPlanStore>();
            var plan = await migrationPlans.GetAsync(request.SourceProfileId, request.PlanId, CancellationToken.None);
            if (plan is null)
            {
                scopedJobs.Fail(job.Id, "migration_plan_not_found", "未找到迁移方案。");
                return;
            }

            var profiles = scope.ServiceProvider.GetRequiredService<ProfileStore>();
            var resourceIndex = scope.ServiceProvider.GetRequiredService<ResourceIndexStore>();
            var nativeContentResolver = scope.ServiceProvider.GetRequiredService<NativeBundleResourceContentResolver>();
            var overlay = scope.ServiceProvider.GetRequiredService<OverlayStore>();
            scopedJobs.Update(job.Id, JobStatus.Running, 40, "正在写入目标覆盖层。");
            var response = await ApplySavedMigrationPlanAsync(
                request,
                plan,
                profiles,
                resourceIndex,
                nativeContentResolver,
                overlay,
                CancellationToken.None);
            scopedJobs.Succeed(job.Id, $"迁移方案完成：写入 {response.Drafted}，跳过 {response.Skipped}。", System.Text.Json.JsonSerializer.Serialize(response));
        }
        catch (Exception ex)
        {
            scopedJobs.Fail(job.Id, "job_failed", ex.Message);
        }
    });

    return Results.Ok(ApiResponse<JobSnapshotDto>.Success(job));
});

app.MapGet("/api/patch/download/{profileId}/{buildId}", (
    string profileId,
    string buildId,
    WorkspaceRootProvider workspace) =>
{
    if (buildId.Any(ch => !char.IsDigit(ch)))
    {
        return Results.BadRequest(ApiResponse<object>.Failure("invalid_build_id", "构建编号不合法。"));
    }

    var layout = WorkspaceLayout.ForProfile(workspace.CurrentRoot, profileId);
    if (!Directory.Exists(layout.BuildsRoot))
    {
        return Results.NotFound(ApiResponse<object>.Failure("build_not_found", "未找到补丁输出。"));
    }

    var zipPath = Directory.EnumerateFiles(layout.BuildsRoot, $"{buildId}-*-patch.zip", SearchOption.TopDirectoryOnly)
        .FirstOrDefault();
    if (zipPath is null || !File.Exists(zipPath))
    {
        return Results.NotFound(ApiResponse<object>.Failure("build_not_found", "未找到补丁 zip。"));
    }

    return Results.File(zipPath, "application/zip", Path.GetFileName(zipPath));
});

app.MapPost("/api/native/bundles2/probe-index", async (
    NativeIndexProbeRequest request,
    NativeBundles2IndexReader reader,
    CancellationToken cancellationToken) =>
{
    var response = await reader.ProbeAsync(request.IndexPath, request.OodleAvailable, cancellationToken);
    return Results.Ok(ApiResponse<NativeIndexProbeResponse>.Success(response));
});

app.MapPost("/api/native/bundles2/decompress-index", async (
    NativeIndexDecompressRequest request,
    NativeIndexCacheService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await service.DecompressIndexAsync(request, cancellationToken);
        return Results.Ok(ApiResponse<NativeIndexDecompressResponse>.Success(response));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<NativeIndexDecompressResponse>.Failure("invalid_profile_id", ex.Message));
    }
});

app.MapPost("/api/native/bundles2/parse-index-cache", async (
    NativeIndexParseRequest request,
    NativeIndexRecordParser parser,
    CancellationToken cancellationToken) =>
{
    var result = await parser.ParseAsync(request.DecompressedIndexPath, cancellationToken);
    var response = new NativeIndexParseResponse(
        result.Ok,
        request.DecompressedIndexPath,
        result.BundleCount,
        result.FileCount,
        result.DirectoryCount,
        result.DirectoryBundleDataOffset,
        result.DirectoryBundleDataSize,
        result.Warnings);
    return Results.Ok(ApiResponse<NativeIndexParseResponse>.Success(response));
});

app.MapPost("/api/native/bundles2/resolve-paths", async (
    NativeIndexResolvePathsRequest request,
    NativeIndexPathService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await service.ResolveAsync(request, cancellationToken);
        return Results.Ok(ApiResponse<NativeIndexResolvePathsResponse>.Success(response));
    }
    catch (Exception ex) when (ex is FileNotFoundException or EntryPointNotFoundException or BadImageFormatException or DllNotFoundException)
    {
        return Results.Ok(ApiResponse<NativeIndexResolvePathsResponse>.Success(new NativeIndexResolvePathsResponse(
            Ok: false,
            request.ProfileId,
            FileCount: 0,
            ResolvedCount: 0,
            FailedCount: 0,
            BundleCount: 0,
            DirectoryCount: 0,
            SamplePaths: [],
            Warnings: [$"无法加载 oo2core.dll：{ex.Message}"])));
    }
});

app.MapPost("/api/native/bundles2/build-resource-index", async (
    NativeResourceIndexBuildRequest request,
    ProfileStore profiles,
    NativeIndexCacheService cacheService,
    NativeIndexRecordParser parser,
    OodleCodecFactory oodleCodecFactory,
    NativeBundles2ResourceIndexer nativeIndexer,
    ResourceIndexStore resourceIndex,
    CancellationToken cancellationToken) =>
{
    var profile = await profiles.GetAsync(request.ProfileId, cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(ApiResponse<NativeResourceIndexBuildResponse>.Failure("profile_not_found", "未找到客户端配置。"));
    }

    var indexPath = string.IsNullOrWhiteSpace(request.IndexPath) ? profile.IndexPath : request.IndexPath;
    if (string.IsNullOrWhiteSpace(indexPath))
    {
        return Results.BadRequest(ApiResponse<NativeResourceIndexBuildResponse>.Failure("missing_index_path", "客户端配置缺少 Bundles2/_.index.bin 路径。"));
    }

    try
    {
        var decompressed = await cacheService.DecompressIndexAsync(
            new NativeIndexDecompressRequest(profile.Id, indexPath, request.OodlePath),
            cancellationToken);
        if (!decompressed.Ok)
        {
            return Results.Ok(ApiResponse<NativeResourceIndexBuildResponse>.Success(new NativeResourceIndexBuildResponse(
                Ok: false,
                profile.Id,
                TotalFiles: 0,
                ResolvedResources: 0,
                FailedPaths: 0,
                BundleCount: 0,
                DirectoryCount: 0,
                DateTimeOffset.UtcNow,
                decompressed.Warnings)));
        }

        var parsed = await parser.ParseAsync(decompressed.CachePath, cancellationToken);
        if (!parsed.Ok)
        {
            return Results.Ok(ApiResponse<NativeResourceIndexBuildResponse>.Success(new NativeResourceIndexBuildResponse(
                Ok: false,
                profile.Id,
                TotalFiles: 0,
                ResolvedResources: 0,
                FailedPaths: 0,
                BundleCount: 0,
                DirectoryCount: 0,
                DateTimeOffset.UtcNow,
                parsed.Warnings)));
        }

        using var oodle = CreateDisposableCodec(request.OodlePath, oodleCodecFactory);
        var bytes = await File.ReadAllBytesAsync(decompressed.CachePath, cancellationToken);
        var directoryBundle = bytes.AsSpan((int)parsed.DirectoryBundleDataOffset, (int)parsed.DirectoryBundleDataSize).ToArray();
        var directoryData = new NativeBundleDecompressor(oodle).Decompress(directoryBundle);
        if (!directoryData.Ok)
        {
            return Results.Ok(ApiResponse<NativeResourceIndexBuildResponse>.Success(new NativeResourceIndexBuildResponse(
                Ok: false,
                profile.Id,
                parsed.FileCount,
                ResolvedResources: 0,
                FailedPaths: 0,
                parsed.BundleCount,
                parsed.DirectoryCount,
                DateTimeOffset.UtcNow,
                directoryData.Warnings)));
        }

        var paths = new NativeIndexPathResolver().Resolve(parsed.Files, parsed.Directories, directoryData.Data);
        var result = nativeIndexer.Index(profile, parsed, paths);
        await resourceIndex.SaveAsync(profile.Id, result.Resources, result.Warnings, cancellationToken);
        var response = new NativeResourceIndexBuildResponse(
            Ok: true,
            profile.Id,
            result.TotalFiles,
            result.Resources.Count,
            result.FailedPaths,
            parsed.BundleCount,
            parsed.DirectoryCount,
            DateTimeOffset.UtcNow,
            result.Warnings);
        return Results.Ok(ApiResponse<NativeResourceIndexBuildResponse>.Success(response));
    }
    catch (Exception ex) when (ex is FileNotFoundException or EntryPointNotFoundException or BadImageFormatException or DllNotFoundException)
    {
        return Results.Ok(ApiResponse<NativeResourceIndexBuildResponse>.Success(new NativeResourceIndexBuildResponse(
            Ok: false,
            profile.Id,
            TotalFiles: 0,
            ResolvedResources: 0,
            FailedPaths: 0,
            BundleCount: 0,
            DirectoryCount: 0,
            DateTimeOffset.UtcNow,
            Warnings: [$"无法加载 oo2core.dll：{ex.Message}"])));
    }
});

app.MapPost("/api/native/ggpk/build-resource-index", async (
    GgpkResourceIndexBuildRequest request,
    WorkspaceRootProvider workspace,
    ProfileStore profiles,
    OodleCodecFactory oodleCodecFactory,
    ResourceIndexStore resourceIndex,
    CancellationToken cancellationToken) =>
{
    var profile = await profiles.GetAsync(request.ProfileId, cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(ApiResponse<GgpkResourceIndexBuildResponse>.Failure("profile_not_found", "未找到客户端配置。"));
    }

    if (profile.EntryKind != ClientEntryKind.Ggpk)
    {
        return Results.BadRequest(ApiResponse<GgpkResourceIndexBuildResponse>.Failure("not_ggpk_client", "该客户端不是 Content.ggpk 结构。"));
    }

    using var oodle = CreateDisposableCodec(request.OodlePath, oodleCodecFactory);
    var result = await new GgpkResourceIndexer(oodle).IndexAsync(profile, cancellationToken);
    WriteGgpkDecompressedIndexCache(workspace.CurrentRoot, profile.Id, result.DecompressedIndex);
    await resourceIndex.SaveAsync(profile.Id, result.Resources, result.Warnings, cancellationToken);
    var response = new GgpkResourceIndexBuildResponse(
        Ok: result.Resources.Count > 0,
        profile.Id,
        profile.ContentGgpkPath ?? string.Empty,
        result.TotalFiles,
        result.Resources.Count,
        result.DirectoryCount,
        DateTimeOffset.UtcNow,
        result.Warnings,
        result.Bundles2Coverage);
    return Results.Ok(ApiResponse<GgpkResourceIndexBuildResponse>.Success(response));
});

app.MapPost("/api/jobs/native/bundles2/build-resource-index", (
    NativeResourceIndexBuildRequest request,
    InMemoryJobStore jobs,
    IServiceScopeFactory scopeFactory) =>
{
    var job = jobs.Create("native-bundles2-resource-index", "任务已创建，等待开始。");
    _ = Task.Run(async () =>
    {
        using var scope = scopeFactory.CreateScope();
        var scopedJobs = scope.ServiceProvider.GetRequiredService<InMemoryJobStore>();
        try
        {
            scopedJobs.Update(job.Id, JobStatus.Running, 5, "正在读取客户端配置。");
            var profiles = scope.ServiceProvider.GetRequiredService<ProfileStore>();
            var profile = await profiles.GetAsync(request.ProfileId, CancellationToken.None);
            if (profile is null)
            {
                scopedJobs.Fail(job.Id, "profile_not_found", "未找到客户端配置。");
                return;
            }

            var indexPath = string.IsNullOrWhiteSpace(request.IndexPath) ? profile.IndexPath : request.IndexPath;
            if (string.IsNullOrWhiteSpace(indexPath))
            {
                scopedJobs.Fail(job.Id, "missing_index_path", "客户端配置缺少 Bundles2/_.index.bin 路径。");
                return;
            }

            scopedJobs.Update(job.Id, JobStatus.Running, 15, "正在解压 _.index.bin。");
            var cacheService = scope.ServiceProvider.GetRequiredService<NativeIndexCacheService>();
            var decompressed = await cacheService.DecompressIndexAsync(
                new NativeIndexDecompressRequest(profile.Id, indexPath, request.OodlePath),
                CancellationToken.None);
            if (!decompressed.Ok)
            {
                scopedJobs.Succeed(job.Id, "索引构建结束，但 index 解压不可用。", System.Text.Json.JsonSerializer.Serialize(new NativeResourceIndexBuildResponse(
                    Ok: false,
                    profile.Id,
                    TotalFiles: 0,
                    ResolvedResources: 0,
                    FailedPaths: 0,
                    BundleCount: 0,
                    DirectoryCount: 0,
                    DateTimeOffset.UtcNow,
                    decompressed.Warnings)));
                return;
            }

            scopedJobs.Update(job.Id, JobStatus.Running, 35, "正在解析 native index 记录。");
            var parser = scope.ServiceProvider.GetRequiredService<NativeIndexRecordParser>();
            var parsed = await parser.ParseAsync(decompressed.CachePath, CancellationToken.None);
            if (!parsed.Ok)
            {
                scopedJobs.Succeed(job.Id, "索引构建结束，但 index 记录解析不可用。", System.Text.Json.JsonSerializer.Serialize(new NativeResourceIndexBuildResponse(
                    Ok: false,
                    profile.Id,
                    TotalFiles: 0,
                    ResolvedResources: 0,
                    FailedPaths: 0,
                    BundleCount: 0,
                    DirectoryCount: 0,
                    DateTimeOffset.UtcNow,
                    parsed.Warnings)));
                return;
            }

            scopedJobs.Update(job.Id, JobStatus.Running, 55, "正在解析虚拟资源路径。");
            var oodleCodecFactory = scope.ServiceProvider.GetRequiredService<OodleCodecFactory>();
            using var oodle = CreateDisposableCodec(request.OodlePath, oodleCodecFactory);
            var bytes = await File.ReadAllBytesAsync(decompressed.CachePath, CancellationToken.None);
            var directoryBundle = bytes.AsSpan((int)parsed.DirectoryBundleDataOffset, (int)parsed.DirectoryBundleDataSize).ToArray();
            var directoryData = new NativeBundleDecompressor(oodle).Decompress(directoryBundle);
            if (!directoryData.Ok)
            {
                scopedJobs.Succeed(job.Id, "索引构建结束，但路径数据解压不可用。", System.Text.Json.JsonSerializer.Serialize(new NativeResourceIndexBuildResponse(
                    Ok: false,
                    profile.Id,
                    parsed.FileCount,
                    ResolvedResources: 0,
                    FailedPaths: 0,
                    parsed.BundleCount,
                    parsed.DirectoryCount,
                    DateTimeOffset.UtcNow,
                    directoryData.Warnings)));
                return;
            }

            scopedJobs.Update(job.Id, JobStatus.Running, 75, "正在写入资源索引缓存。");
            var paths = new NativeIndexPathResolver().Resolve(parsed.Files, parsed.Directories, directoryData.Data);
            var nativeIndexer = scope.ServiceProvider.GetRequiredService<NativeBundles2ResourceIndexer>();
            var result = nativeIndexer.Index(profile, parsed, paths);
            var resourceIndex = scope.ServiceProvider.GetRequiredService<ResourceIndexStore>();
            await resourceIndex.SaveAsync(profile.Id, result.Resources, result.Warnings, CancellationToken.None);
            var response = new NativeResourceIndexBuildResponse(
                Ok: true,
                profile.Id,
                result.TotalFiles,
                result.Resources.Count,
                result.FailedPaths,
                parsed.BundleCount,
                parsed.DirectoryCount,
                DateTimeOffset.UtcNow,
                result.Warnings);
            scopedJobs.Succeed(job.Id, "资源索引已完成。", System.Text.Json.JsonSerializer.Serialize(response));
        }
        catch (Exception ex) when (ex is FileNotFoundException or EntryPointNotFoundException or BadImageFormatException or DllNotFoundException)
        {
            scopedJobs.Fail(job.Id, "oodle_load_failed", $"无法加载 oo2core.dll：{ex.Message}");
        }
        catch (Exception ex)
        {
            scopedJobs.Fail(job.Id, "job_failed", ex.Message);
        }
    });

    return Results.Ok(ApiResponse<JobSnapshotDto>.Success(job));
});

app.MapPost("/api/jobs/native/ggpk/build-resource-index", (
    GgpkResourceIndexBuildRequest request,
    InMemoryJobStore jobs,
    IServiceScopeFactory scopeFactory) =>
{
    var job = jobs.Create("native-ggpk-resource-index", "任务已创建，等待开始。");
    _ = Task.Run(async () =>
    {
        using var scope = scopeFactory.CreateScope();
        var scopedJobs = scope.ServiceProvider.GetRequiredService<InMemoryJobStore>();
        try
        {
            scopedJobs.Update(job.Id, JobStatus.Running, 5, "正在读取客户端配置。");
            var profiles = scope.ServiceProvider.GetRequiredService<ProfileStore>();
            var profile = await profiles.GetAsync(request.ProfileId, CancellationToken.None);
            if (profile is null)
            {
                scopedJobs.Fail(job.Id, "profile_not_found", "未找到客户端配置。");
                return;
            }

            if (profile.EntryKind != ClientEntryKind.Ggpk)
            {
                scopedJobs.Fail(job.Id, "not_ggpk_client", "该客户端不是 Content.ggpk 结构。");
                return;
            }

            scopedJobs.Update(job.Id, JobStatus.Running, 20, "正在扫描 Content.ggpk 文件树。");
            var oodleCodecFactory = scope.ServiceProvider.GetRequiredService<OodleCodecFactory>();
            var workspace = scope.ServiceProvider.GetRequiredService<WorkspaceRootProvider>();
            using var oodle = CreateDisposableCodec(request.OodlePath, oodleCodecFactory);
            var indexer = new GgpkResourceIndexer(oodle);
            var result = await indexer.IndexAsync(profile, CancellationToken.None);
            WriteGgpkDecompressedIndexCache(workspace.CurrentRoot, profile.Id, result.DecompressedIndex);

            scopedJobs.Update(job.Id, JobStatus.Running, 85, "正在写入 GGPK 资源索引缓存。");
            var resourceIndex = scope.ServiceProvider.GetRequiredService<ResourceIndexStore>();
            await resourceIndex.SaveAsync(profile.Id, result.Resources, result.Warnings, CancellationToken.None);
            var response = new GgpkResourceIndexBuildResponse(
                Ok: result.Resources.Count > 0,
                profile.Id,
                profile.ContentGgpkPath ?? string.Empty,
                result.TotalFiles,
                result.Resources.Count,
                result.DirectoryCount,
                DateTimeOffset.UtcNow,
                result.Warnings,
                result.Bundles2Coverage);
            scopedJobs.Succeed(job.Id, response.Ok ? "GGPK 资源索引已完成。" : "GGPK 索引结束，但未解析到资源。", System.Text.Json.JsonSerializer.Serialize(response));
        }
        catch (Exception ex)
        {
            scopedJobs.Fail(job.Id, "ggpk_index_failed", ex.Message);
        }
    });

    return Results.Ok(ApiResponse<JobSnapshotDto>.Success(job));
});

app.MapPost("/api/jobs/patch/build", (
    PatchBuildRequest request,
    InMemoryJobStore jobs,
    IServiceScopeFactory scopeFactory) =>
{
    var job = jobs.Create("patch-build", "任务已创建，等待开始。");
    _ = Task.Run(async () =>
    {
        using var scope = scopeFactory.CreateScope();
        var scopedJobs = scope.ServiceProvider.GetRequiredService<InMemoryJobStore>();
        try
        {
            scopedJobs.Update(job.Id, JobStatus.Running, 10, "正在读取客户端配置。");
            var profiles = scope.ServiceProvider.GetRequiredService<ProfileStore>();
            var profile = await profiles.GetAsync(request.ProfileId, CancellationToken.None);
            if (profile is null)
            {
                scopedJobs.Fail(job.Id, "profile_not_found", "未找到客户端配置。");
                return;
            }

            scopedJobs.Update(job.Id, JobStatus.Running, 35, "正在构建补丁包。");
            var patchBuild = scope.ServiceProvider.GetRequiredService<PatchBuildService>();
            var response = await patchBuild.BuildAsync(request, profile, CancellationToken.None);
            scopedJobs.Succeed(job.Id, "补丁包已生成。", System.Text.Json.JsonSerializer.Serialize(response));
        }
        catch (PatchBuildException ex)
        {
            scopedJobs.Fail(job.Id, ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            scopedJobs.Fail(job.Id, "job_failed", ex.Message);
        }
    });

    return Results.Ok(ApiResponse<JobSnapshotDto>.Success(job));
});

app.MapGet("/api/jobs/{jobId}", (string jobId, InMemoryJobStore jobs) =>
{
    var job = jobs.Get(jobId);
    if (job is null)
    {
        return Results.NotFound(ApiResponse<JobSnapshotDto>.Failure("job_not_found", "未找到任务。"));
    }

    return Results.Ok(ApiResponse<JobSnapshotDto>.Success(job));
});

app.Run();

static async Task<(bool Writable, IReadOnlyList<string> Warnings)> CheckWorkspaceAsync(
    string workspaceRoot,
    CancellationToken cancellationToken)
{
    var warnings = new List<string>();
    try
    {
        Directory.CreateDirectory(workspaceRoot);
        var probe = Path.Combine(workspaceRoot, $".write-test-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(probe, "ok", cancellationToken);
        File.Delete(probe);
        return (true, warnings);
    }
    catch (Exception ex)
    {
        warnings.Add($"工作区不可写：{ex.Message}");
        return (false, warnings);
    }
}

static (bool Writable, IReadOnlyList<string> Warnings) CheckWorkspace(string workspaceRoot)
{
    var warnings = new List<string>();
    try
    {
        Directory.CreateDirectory(workspaceRoot);
        var probe = Path.Combine(workspaceRoot, $".write-test-{Guid.NewGuid():N}");
        File.WriteAllText(probe, "ok");
        File.Delete(probe);
        return (true, warnings);
    }
    catch (Exception ex)
    {
        warnings.Add($"工作区不可写：{ex.Message}");
        return (false, warnings);
    }
}

static void WriteGgpkDecompressedIndexCache(string workspaceRoot, string profileId, byte[]? decompressedIndex)
{
    if (decompressedIndex is null || decompressedIndex.Length == 0)
    {
        return;
    }

    var layout = WorkspaceLayout.ForProfile(workspaceRoot, profileId);
    var cachePath = Path.Combine(layout.RawCacheRoot, "native", "bundles2", "index.decompressed.bin");
    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
    File.WriteAllBytes(cachePath, decompressedIndex);
}

static IDisposableOodleCodec CreateDisposableCodec(string? oodlePath, OodleCodecFactory factory)
{
    if (string.IsNullOrWhiteSpace(oodlePath))
    {
        return new IDisposableOodleCodec(new MissingOodleCodec());
    }

    return new IDisposableOodleCodec(factory(oodlePath));
}

static string GuessContentType(string extension)
{
    return extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".bmp" => "image/bmp",
        ".dds" => "image/vnd-ms.dds",
        ".ogg" => "audio/ogg",
        ".wav" => "audio/wav",
        ".ttf" => "font/ttf",
        ".json" => "application/json",
        ".xml" => "application/xml",
        ".txt" or ".filter" or ".csd" => "text/plain",
        _ => "application/octet-stream"
    };
}

static ResourceFormatScanItemDto BuildFormatScanItem(
    string extension,
    IEnumerable<ResourceSummaryDto> resources)
{
    var items = resources.ToArray();
    var kind = items
        .GroupBy(item => item.Kind)
        .OrderByDescending(group => group.Count())
        .ThenBy(group => group.Key)
        .First().Key;
    var previewable = items.Count(CanPreviewResource);
    var editable = items.Count(CanEditResource);
    var missing = items.Count(item =>
        item.SourceLayer != ResourceSourceLayer.Base
        && !string.IsNullOrWhiteSpace(item.PhysicalPath)
        && !File.Exists(item.PhysicalPath));
    var warnings = new List<string>();
    if (items.Any(item => item.SourceLayer != ResourceSourceLayer.Base))
    {
        warnings.Add("部分资源来自 native 索引，可能需要 oo2core.dll 才能读取内容。");
    }

    return new ResourceFormatScanItemDto(
        extension,
        kind,
        items.Length,
        previewable,
        editable,
        Math.Max(0, items.Length - previewable),
        missing,
        items.Sum(item => item.Size),
        items.Select(item => item.VirtualPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Take(5).ToArray(),
        warnings);
}

static bool CanPreviewResource(ResourceSummaryDto resource)
{
    if (resource.Kind is ResourceKind.Text or ResourceKind.Table or ResourceKind.Image or ResourceKind.Audio or ResourceKind.Font or ResourceKind.Ui)
    {
        return true;
    }

    return resource.Extension.Equals(".atlas", StringComparison.OrdinalIgnoreCase)
        || resource.Extension.Equals(".dds", StringComparison.OrdinalIgnoreCase);
}

static bool CanEditResource(ResourceSummaryDto resource)
{
    return resource.Kind is ResourceKind.Text or ResourceKind.Table or ResourceKind.Ui;
}

static bool CanStructuredEditResource(ResourceSummaryDto resource)
{
    if (resource.Kind is ResourceKind.Text or ResourceKind.Ui)
    {
        return true;
    }

    return resource.Extension.Equals(".ui", StringComparison.OrdinalIgnoreCase)
        || resource.Extension.Equals(".atlas", StringComparison.OrdinalIgnoreCase)
        || resource.Extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
        || resource.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
        || resource.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
        || resource.Extension.Equals(".filter", StringComparison.OrdinalIgnoreCase)
        || resource.Extension.Equals(".csd", StringComparison.OrdinalIgnoreCase);
}

static bool CanTextChunkResource(ResourceSummaryDto resource)
{
    if (resource.Kind is ResourceKind.Text or ResourceKind.Ui)
    {
        return true;
    }

    return resource.Extension.Equals(".ui", StringComparison.OrdinalIgnoreCase)
        || resource.Extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
        || resource.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
        || resource.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
        || resource.Extension.Equals(".filter", StringComparison.OrdinalIgnoreCase)
        || resource.Extension.Equals(".atlas", StringComparison.OrdinalIgnoreCase)
        || resource.Extension.Equals(".csd", StringComparison.OrdinalIgnoreCase);
}

static StructuredTextInspection InspectStructuredText(ResourceSummaryDto resource, string text)
{
    var nodes = new List<StructuredTextNodeDto>();
    var warnings = new List<string>();
    var lines = NormalizeLines(text);
    for (var index = 0; index < lines.Length; index++)
    {
        var line = lines[index];
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            continue;
        }

        var parsed = TryParseStructuredLine(resource, line);
        if (parsed is null)
        {
            continue;
        }

        nodes.Add(new StructuredTextNodeDto(parsed.Value.Key, parsed.Value.Value, index + 1));
    }

    if (nodes.Count == 0)
    {
        warnings.Add("未识别到可按键值编辑的结构节点，仍可使用原始文本预览。");
    }

    return new StructuredTextInspection(nodes, warnings);
}

static StructuredTextApplyResult ApplyStructuredTextEdits(
    ResourceSummaryDto resource,
    string text,
    IReadOnlyList<StructuredTextEditDto> edits)
{
    if (edits.Count == 0)
    {
        throw new ArgumentException("至少需要一条结构编辑。", nameof(edits));
    }

    var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    var hadTrailingNewline = text.EndsWith("\n", StringComparison.Ordinal) || text.EndsWith("\r", StringComparison.Ordinal);
    var lines = NormalizeLines(text).ToList();
    var warnings = new List<string>();
    var edited = 0;

    foreach (var edit in edits)
    {
        if (string.IsNullOrWhiteSpace(edit.Key))
        {
            throw new ArgumentException("结构编辑 key 不能为空。", nameof(edits));
        }

        var index = -1;
        if (edit.LineNumber is not null)
        {
            var candidate = edit.LineNumber.Value - 1;
            if (candidate < 0 || candidate >= lines.Count)
            {
                throw new ArgumentException($"行号超出范围：{edit.LineNumber.Value}", nameof(edits));
            }

            var parsed = TryParseStructuredLine(resource, lines[candidate]);
            if (parsed is not null && string.Equals(parsed.Value.Key, edit.Key, StringComparison.OrdinalIgnoreCase))
            {
                index = candidate;
            }
        }

        if (index < 0)
        {
            index = lines.FindIndex(line =>
            {
                var parsed = TryParseStructuredLine(resource, line);
                return parsed is not null && string.Equals(parsed.Value.Key, edit.Key, StringComparison.OrdinalIgnoreCase);
            });
        }

        if (index >= 0)
        {
            lines[index] = FormatStructuredLine(resource, lines[index], edit.Key.Trim(), edit.Value);
            edited++;
        }
        else
        {
            lines.Add(FormatStructuredLine(resource, string.Empty, edit.Key.Trim(), edit.Value));
            edited++;
            warnings.Add($"新增结构节点：{edit.Key.Trim()}");
        }
    }

    var output = string.Join(newline, lines);
    if (hadTrailingNewline || text.Length == 0)
    {
        output += newline;
    }

    return new StructuredTextApplyResult(output, edited, warnings);
}

static (string Key, string Value)? TryParseStructuredLine(ResourceSummaryDto resource, string line)
{
    var trimmed = line.Trim();
    if (trimmed.Length == 0)
    {
        return null;
    }

    if (resource.Extension.Equals(".atlas", StringComparison.OrdinalIgnoreCase))
    {
        var firstSpace = trimmed.IndexOfAny([' ', '\t']);
        if (firstSpace <= 0)
        {
            return null;
        }

        var key = trimmed[..firstSpace].Trim();
        var value = trimmed[firstSpace..].Trim();
        return key.Length == 0 ? null : (key, value);
    }

    var equals = line.IndexOf('=');
    var colon = line.IndexOf(':');
    var separator = equals >= 0 && (colon < 0 || equals < colon) ? equals : colon;
    if (separator <= 0)
    {
        return null;
    }

    var parsedKey = line[..separator].Trim();
    var parsedValue = line[(separator + 1)..].Trim();
    return parsedKey.Length == 0 ? null : (parsedKey, parsedValue);
}

static string FormatStructuredLine(ResourceSummaryDto resource, string originalLine, string key, string value)
{
    if (resource.Extension.Equals(".atlas", StringComparison.OrdinalIgnoreCase))
    {
        return $"{key} {value}".TrimEnd();
    }

    if (originalLine.Contains(':', StringComparison.Ordinal) && !originalLine.Contains('=', StringComparison.Ordinal))
    {
        return $"{key}: {value}";
    }

    return $"{key} = {value}";
}

static string[] NormalizeLines(string text)
{
    return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n', StringSplitOptions.None);
}

static TextDocument DecodeTextDocument(byte[] data)
{
    var encodingName = OverlayStore.DetectTextEncoding(data.AsSpan(0, Math.Min(data.Length, 512)));
    string text;
    switch (encodingName)
    {
        case "utf-16le-bom":
            text = Encoding.Unicode.GetString(data.AsSpan(2));
            break;
        case "utf-16le":
            text = Encoding.Unicode.GetString(data);
            break;
        case "utf-8-bom":
            text = Encoding.UTF8.GetString(data.AsSpan(3));
            break;
        default:
            text = Encoding.UTF8.GetString(data);
            encodingName ??= "utf-8";
            break;
    }

    return new TextDocument(text, DetectNewLine(text), encodingName);
}

static string DetectNewLine(string text)
{
    var lf = text.IndexOf('\n', StringComparison.Ordinal);
    if (lf > 0 && text[lf - 1] == '\r')
    {
        return "\r\n";
    }

    if (lf >= 0)
    {
        return "\n";
    }

    return text.Contains('\r', StringComparison.Ordinal) ? "\r" : "\r\n";
}

static TextChunkResponse BuildTextChunkResponse(
    TextChunkRequest request,
    string virtualPath,
    TextDocument document,
    bool fromOverlay)
{
    var lines = NormalizeLines(document.Text);
    var totalLines = CountLogicalLines(lines);
    var startLine = Math.Clamp(request.StartLine <= 0 ? 1 : request.StartLine, 1, Math.Max(1, totalLines));
    var lineCount = Math.Clamp(request.LineCount <= 0 ? 400 : request.LineCount, 1, 5000);
    var available = Math.Max(0, totalLines - startLine + 1);
    var actualLineCount = Math.Min(lineCount, available);
    var chunkLines = lines
        .Skip(startLine - 1)
        .Take(actualLineCount)
        .ToArray();
    var endLine = actualLineCount == 0 ? startLine : startLine + actualLineCount - 1;
    return new TextChunkResponse(
        request.ProfileId,
        virtualPath,
        string.Join(document.NewLine, chunkLines),
        startLine,
        endLine,
        actualLineCount,
        totalLines,
        startLine > 1,
        endLine < totalLines,
        document.NewLine,
        document.EncodingName,
        fromOverlay);
}

static TextChunkReplaceResult ReplaceTextChunk(TextDocument document, TextChunkSaveRequest request)
{
    var lines = NormalizeLines(document.Text).ToList();
    var totalLines = CountLogicalLines(lines);
    if (request.StartLine < 1 || request.StartLine > Math.Max(1, totalLines))
    {
        throw new ArgumentException("起始行超出文件范围。");
    }

    var originalLineCount = Math.Clamp(request.OriginalLineCount, 0, Math.Max(0, totalLines - request.StartLine + 1));
    var replacement = NormalizeLines(request.Text).ToList();
    if (replacement.Count > 0 && replacement[^1].Length == 0 && !request.Text.EndsWith("\n", StringComparison.Ordinal) && !request.Text.EndsWith("\r", StringComparison.Ordinal))
    {
        replacement.RemoveAt(replacement.Count - 1);
    }

    lines.RemoveRange(request.StartLine - 1, originalLineCount);
    lines.InsertRange(request.StartLine - 1, replacement);
    var logicalLines = CountLogicalLines(lines);
    var lineCount = CountLogicalLines(replacement);
    var endLine = lineCount == 0 ? request.StartLine : request.StartLine + lineCount - 1;
    return new TextChunkReplaceResult(
        string.Join(document.NewLine, lines),
        request.StartLine,
        endLine,
        lineCount,
        logicalLines,
        document.NewLine);
}

static int CountLogicalLines(IReadOnlyList<string> lines)
{
    return lines.Count == 0 ? 0 : lines[^1].Length == 0 ? Math.Max(1, lines.Count - 1) : lines.Count;
}

static async Task<ResourceBytesReadResult> ReadResourceBytesAsync(
    string profileId,
    string? oodlePath,
    ResourceSummaryDto resource,
    ProfileStore profiles,
    NativeBundleResourceContentResolver nativeContentResolver,
    CancellationToken cancellationToken)
{
    if (NativeBundleResourceContentResolver.IsNativeResource(resource))
    {
        var profile = await profiles.GetAsync(profileId, cancellationToken);
        if (profile is null)
        {
            return ResourceBytesReadResult.Fail(StatusCodes.Status404NotFound, "profile_not_found", "未找到客户端配置。");
        }

        var content = await nativeContentResolver.ReadAsync(profile, resource, oodlePath, cancellationToken);
        if (!content.Ok)
        {
            return ResourceBytesReadResult.Fail(
                StatusCodes.Status400BadRequest,
                content.ErrorCode ?? "native_resource_read_failed",
                content.Message ?? "native 资源读取失败。");
        }

        return ResourceBytesReadResult.Success(content.Data);
    }

    if (string.IsNullOrWhiteSpace(resource.PhysicalPath) || !File.Exists(resource.PhysicalPath))
    {
        return ResourceBytesReadResult.Fail(StatusCodes.Status404NotFound, "resource_file_missing", "资源文件不存在，可能尚未提取或索引已过期。");
    }

    return ResourceBytesReadResult.Success(await File.ReadAllBytesAsync(resource.PhysicalPath, cancellationToken));
}

static async Task<string?> DetectResourceTextEncodingAsync(
    string profileId,
    string? oodlePath,
    ResourceSummaryDto resource,
    ProfileStore profiles,
    NativeBundleResourceContentResolver nativeContentResolver,
    CancellationToken cancellationToken)
{
    if (!NativeBundleResourceContentResolver.IsNativeResource(resource)
        && !string.IsNullOrWhiteSpace(resource.PhysicalPath)
        && File.Exists(resource.PhysicalPath))
    {
        var bytes = await File.ReadAllBytesAsync(resource.PhysicalPath, cancellationToken);
        return OverlayStore.DetectTextEncoding(bytes.AsSpan(0, Math.Min(bytes.Length, 512)));
    }

    if (!NativeBundleResourceContentResolver.IsNativeResource(resource))
    {
        return null;
    }

    var read = await ReadResourceBytesAsync(profileId, oodlePath, resource, profiles, nativeContentResolver, cancellationToken);
    return read.Ok
        ? OverlayStore.DetectTextEncoding(read.Data.AsSpan(0, Math.Min(read.Data.Length, 512)))
        : null;
}

static async Task<BatchScriptExecutionResult> RunBatchScriptAsync(
    BatchScriptRunRequest request,
    ResourceIndexStore resourceIndex,
    OverlayStore overlay,
    CancellationToken cancellationToken)
{
    if (request.Operations.Count == 0)
    {
        return BatchScriptExecutionResult.Fail("empty_batch_script", "批处理脚本至少需要一条规则。");
    }

    var warnings = new List<string>();
    var candidates = new List<(BatchScriptOperationDto Operation, ResourceSummaryDto Resource, string Text)>();
    foreach (var operation in request.Operations)
    {
        if (string.IsNullOrWhiteSpace(operation.Query))
        {
            warnings.Add($"跳过空搜索规则：{operation.Name}");
            continue;
        }

        var search = await resourceIndex.SearchAsync(new ResourceSearchRequest(
            request.ProfileId,
            Query: operation.Query,
            Kind: operation.Kind,
            Extension: operation.Extension,
            Skip: 0,
            Take: Math.Clamp(operation.Take, 1, 500)), cancellationToken);

        foreach (var resource in search.Items)
        {
            if (resource.Kind is not (ResourceKind.Text or ResourceKind.Ui))
            {
                warnings.Add($"跳过非文本资源：{resource.VirtualPath}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(resource.PhysicalPath) || !File.Exists(resource.PhysicalPath))
            {
                warnings.Add($"跳过尚不可读取的资源：{resource.VirtualPath}");
                continue;
            }

            var readResource = request.UseOverlay
                ? await BuildOverlayResourceAsync(resource, overlay, cancellationToken) ?? resource
                : resource;
            if (string.IsNullOrWhiteSpace(readResource.PhysicalPath) || !File.Exists(readResource.PhysicalPath))
            {
                warnings.Add($"跳过尚不可读取的资源：{resource.VirtualPath}");
                continue;
            }

            var text = await File.ReadAllTextAsync(readResource.PhysicalPath, cancellationToken);
            candidates.Add((operation, resource, text));
        }
    }

    var runner = new BatchScriptRunner();
    var result = runner.Run(request.ProfileId, request.Operations, candidates, request.Apply);
    if (request.Apply)
    {
        foreach (var change in result.Changes)
        {
            var candidate = candidates.First(item =>
                item.Operation.Name == change.OperationName
                && item.Resource.VirtualPath == change.VirtualPath);
            var replaced = candidate.Text.Replace(candidate.Operation.Find, candidate.Operation.Replace, StringComparison.Ordinal);
            await overlay.SaveTextAsync(new SaveTextOverlayRequest(
                request.ProfileId,
                candidate.Resource.VirtualPath,
                replaced,
                candidate.Resource.PhysicalPath,
                HasBasePhysicalPath: true), cancellationToken);
        }
    }

    return BatchScriptExecutionResult.Success(result with
    {
        Warnings = result.Warnings.Concat(warnings).ToArray()
    });
}

static async Task<ResourceBytesReadResult> ReadResourceBytesPreferOverlayAsync(
    string profileId,
    string? oodlePath,
    ResourceSummaryDto resource,
    ProfileStore profiles,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken)
{
    var entries = await overlay.GetEntriesAsync(profileId, cancellationToken);
    var existing = entries.FirstOrDefault(item => string.Equals(item.NormalizedPath, resource.NormalizedPath, StringComparison.OrdinalIgnoreCase));
    if (existing is not null && File.Exists(existing.OverlayPath))
    {
        return ResourceBytesReadResult.Success(await File.ReadAllBytesAsync(existing.OverlayPath, cancellationToken));
    }

    return await ReadResourceBytesAsync(profileId, oodlePath, resource, profiles, nativeContentResolver, cancellationToken);
}

static async Task<ResourceBytesReadResult> ReadResourceBytesWithSourceAsync(
    string profileId,
    string? oodlePath,
    ResourceSummaryDto resource,
    ProfileStore profiles,
    NativeBundleResourceContentResolver nativeContentResolver,
    bool FromOverlay,
    CancellationToken cancellationToken)
{
    var read = await ReadResourceBytesAsync(profileId, oodlePath, resource, profiles, nativeContentResolver, cancellationToken);
    return read.Ok ? read with { FromOverlay = FromOverlay } : read;
}

static async Task<ResourceBytesReadResult> ReadResourceBytesPreferOverlayWithSourceAsync(
    string profileId,
    string? oodlePath,
    ResourceSummaryDto resource,
    ProfileStore profiles,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken)
{
    var entries = await overlay.GetEntriesAsync(profileId, cancellationToken);
    var existing = entries.FirstOrDefault(item => string.Equals(item.NormalizedPath, resource.NormalizedPath, StringComparison.OrdinalIgnoreCase));
    if (existing is not null && File.Exists(existing.OverlayPath))
    {
        return ResourceBytesReadResult.Success(await File.ReadAllBytesAsync(existing.OverlayPath, cancellationToken), FromOverlay: true);
    }

    return await ReadResourceBytesWithSourceAsync(profileId, oodlePath, resource, profiles, nativeContentResolver, FromOverlay: false, cancellationToken);
}

static async Task<TableReadResult> ReadTableForEditAsync(
    string profileId,
    string virtualPath,
    string? oodlePath,
    TableSchemaDto? schema,
    string? schemaId,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    TableSchemaStore tableSchemas,
    CancellationToken cancellationToken,
    bool requireTable,
    int maxRows)
{
    var resource = await resourceIndex.GetByPathAsync(profileId, virtualPath, cancellationToken);
    if (resource is null)
    {
        return TableReadResult.Fail(StatusCodes.Status404NotFound, "resource_not_found", "未找到资源，请先建立索引。");
    }

    if (requireTable && resource.Kind != ResourceKind.Table)
    {
        return TableReadResult.Fail(StatusCodes.Status400BadRequest, "not_table_resource", "该资源不是表格/数据文件。");
    }

    var read = await ReadResourceBytesPreferOverlayAsync(
        profileId,
        oodlePath,
        resource,
        profiles,
        nativeContentResolver,
        overlay,
        cancellationToken);
    if (!read.Ok)
    {
        return TableReadResult.Fail(read.StatusCode, read.ErrorCode, read.Message);
    }

    var resolvedSchema = schema;
    if (resolvedSchema is null && !string.IsNullOrWhiteSpace(schemaId))
    {
        var entry = await tableSchemas.GetAsync(profileId, schemaId, cancellationToken);
        if (entry is null)
        {
            return TableReadResult.Fail(StatusCodes.Status404NotFound, "table_schema_not_found", "未找到表结构。");
        }

        resolvedSchema = entry.Schema;
    }

    var inspect = new TableInspector().Inspect(resource, read.Data, read.Data.Length, resolvedSchema, maxRows);
    return TableReadResult.Success(resource, read.Data, resolvedSchema, inspect);
}

static IResult TableReadFailure<T>(TableReadResult result)
{
    var payload = ApiResponse<T>.Failure(result.ErrorCode, result.Message);
    return result.StatusCode == StatusCodes.Status404NotFound
        ? Results.NotFound(payload)
        : Results.BadRequest(payload);
}

static string? EmptyToNull(string? value)
{
    return string.IsNullOrWhiteSpace(value) ? null : value;
}

static async Task<IResult> ImportTableCsvAsync(
    TableCsvImportRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    TableSchemaStore tableSchemas,
    CancellationToken cancellationToken)
{
    var table = await ReadTableForEditAsync(
        request.ProfileId,
        request.VirtualPath,
        request.OodlePath,
        request.Schema,
        request.SchemaId,
        profiles,
        resourceIndex,
        nativeContentResolver,
        overlay,
        tableSchemas,
        cancellationToken,
        requireTable: true,
        maxRows: 1024 * 1024);
    if (!table.Ok)
    {
        return TableReadFailure<TableCsvImportResponse>(table);
    }

    try
    {
        var parsed = ParseCsv(request.Csv);
        if (parsed.Count == 0)
        {
            return Results.BadRequest(ApiResponse<TableCsvImportResponse>.Failure("empty_csv", "CSV 至少需要表头。"));
        }

        var edits = BuildCsvImportEdits(table.Inspect!, parsed);
        var inspector = new TableInspector();
        OverlayEntryDto entry;
        if (table.Schema is not null)
        {
            var editedBytes = inspector.ApplyCellEdits(table.Resource!, table.Data!, edits, table.Schema);
            entry = await overlay.SaveBytesAsync(
                request.ProfileId,
                table.Resource!.VirtualPath,
                editedBytes,
                table.Resource.PhysicalPath,
                HasBasePhysicalPath: !NativeBundleResourceContentResolver.IsNativeResource(table.Resource) && table.Resource.PhysicalPath is not null,
                cancellationToken);
        }
        else if (table.Resource!.Extension.Equals(".datc64", StringComparison.OrdinalIgnoreCase))
        {
            var editedBytes = inspector.ApplyDatc64CatalogCellEdits(table.Resource!, table.Data!, edits);
            entry = await overlay.SaveBytesAsync(
                request.ProfileId,
                table.Resource!.VirtualPath,
                editedBytes,
                table.Resource.PhysicalPath,
                HasBasePhysicalPath: !NativeBundleResourceContentResolver.IsNativeResource(table.Resource) && table.Resource.PhysicalPath is not null,
                cancellationToken);
        }
        else if (table.Inspect!.Delimiter == "legacy-dat-schema")
        {
            var editedBytes = inspector.ApplyLegacyDatCatalogCellEdits(table.Resource!, table.Data!, edits);
            entry = await overlay.SaveBytesAsync(
                request.ProfileId,
                table.Resource!.VirtualPath,
                editedBytes,
                table.Resource.PhysicalPath,
                HasBasePhysicalPath: !NativeBundleResourceContentResolver.IsNativeResource(table.Resource) && table.Resource.PhysicalPath is not null,
                cancellationToken);
        }
        else
        {
            var editedBytes = inspector.ApplyCellEditsToBytes(table.Resource!, table.Data!, edits);
            entry = await overlay.SaveBytesAsync(
                request.ProfileId,
                table.Resource!.VirtualPath,
                editedBytes,
                table.Resource.PhysicalPath,
                HasBasePhysicalPath: !NativeBundleResourceContentResolver.IsNativeResource(table.Resource) && table.Resource.PhysicalPath is not null,
                cancellationToken);
        }

        var response = new TableCsvImportResponse(
            request.ProfileId,
            table.Resource!.VirtualPath,
            Math.Max(0, parsed.Count - 1),
            edits.Count,
            entry,
            table.Inspect!.Warnings);
        return Results.Ok(ApiResponse<TableCsvImportResponse>.Success(response));
    }
    catch (ArgumentOutOfRangeException ex)
    {
        return Results.BadRequest(ApiResponse<TableCsvImportResponse>.Failure("table_edit_out_of_range", ex.Message));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(ApiResponse<TableCsvImportResponse>.Failure("table_edit_unsupported", ex.Message));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<TableCsvImportResponse>.Failure("invalid_csv", ex.Message));
    }
}

static IReadOnlyList<string> BuildTableColumns(TableInspectResponse inspect)
{
    if (inspect.Columns is { Count: > 0 })
    {
        return inspect.Columns;
    }

    var count = inspect.Rows.Count == 0 ? 0 : inspect.Rows.Max(row => row.Cells.Count);
    return Enumerable.Range(1, count).Select(index => $"Column{index}").ToArray();
}

static string BuildCsv(IReadOnlyList<string> columns, IReadOnlyList<TablePreviewRowDto> rows)
{
    var builder = new StringBuilder();
    builder.AppendLine(string.Join(",", columns.Select(EscapeCsvCell)));
    foreach (var row in rows)
    {
        builder.AppendLine(string.Join(",", columns.Select((_, index) => index < row.Cells.Count ? EscapeCsvCell(row.Cells[index]) : string.Empty)));
    }

    return builder.ToString();
}

static string EscapeCsvCell(string value)
{
    if (value.Contains('"', StringComparison.Ordinal)
        || value.Contains(',', StringComparison.Ordinal)
        || value.Contains('\n', StringComparison.Ordinal)
        || value.Contains('\r', StringComparison.Ordinal))
    {
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    return value;
}

static IReadOnlyList<IReadOnlyList<string>> ParseCsv(string csv)
{
    var rows = new List<IReadOnlyList<string>>();
    var row = new List<string>();
    var cell = new StringBuilder();
    var inQuotes = false;
    if (csv.Length > 0 && csv[0] == '\uFEFF')
    {
        csv = csv[1..];
    }

    for (var index = 0; index < csv.Length; index++)
    {
        var ch = csv[index];
        if (inQuotes)
        {
            if (ch == '"')
            {
                if (index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = false;
                }
            }
            else
            {
                cell.Append(ch);
            }

            continue;
        }

        if (ch == '"')
        {
            inQuotes = true;
        }
        else if (ch == ',')
        {
            row.Add(cell.ToString());
            cell.Clear();
        }
        else if (ch == '\n')
        {
            row.Add(cell.ToString());
            cell.Clear();
            rows.Add(row);
            row = [];
        }
        else if (ch != '\r')
        {
            cell.Append(ch);
        }
    }

    if (inQuotes)
    {
        throw new ArgumentException("CSV 引号没有闭合。", nameof(csv));
    }

    if (cell.Length > 0 || row.Count > 0)
    {
        row.Add(cell.ToString());
        rows.Add(row);
    }

    return rows;
}

static IReadOnlyList<TableCellEditDto> BuildCsvImportEdits(TableInspectResponse inspect, IReadOnlyList<IReadOnlyList<string>> parsed)
{
    var header = parsed[0];
    var columns = BuildTableColumns(inspect);
    if (header.Count > columns.Count)
    {
        throw new ArgumentException("CSV 列数超过当前表格列数。", nameof(parsed));
    }

    var edits = new List<TableCellEditDto>();
    var rowCount = Math.Min(inspect.Rows.Count, parsed.Count - 1);
    var editableColumns = inspect.EditableColumnIndexes is { Count: > 0 }
        ? new HashSet<int>(inspect.EditableColumnIndexes)
        : null;
    for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
    {
        var sourceRow = inspect.Rows[rowIndex];
        var csvRow = parsed[rowIndex + 1];
        var columnCount = Math.Min(csvRow.Count, sourceRow.Cells.Count);
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            if (editableColumns is not null && !editableColumns.Contains(columnIndex))
            {
                continue;
            }

            var value = csvRow[columnIndex];
            if (!string.Equals(sourceRow.Cells[columnIndex], value, StringComparison.Ordinal))
            {
                edits.Add(new TableCellEditDto(sourceRow.RowNumber, columnIndex, value));
            }
        }
    }

    return edits;
}

static async Task<ResourceSummaryDto?> BuildOverlayResourceAsync(
    ResourceSummaryDto resource,
    OverlayStore overlay,
    CancellationToken cancellationToken)
{
    var entries = await overlay.GetEntriesAsync(resource.ProfileId, cancellationToken);
    var existing = entries.FirstOrDefault(item => string.Equals(item.NormalizedPath, resource.NormalizedPath, StringComparison.OrdinalIgnoreCase));
    if (existing is null || !File.Exists(existing.OverlayPath))
    {
        return null;
    }

    return resource with
    {
        Size = existing.OverlaySize,
        PhysicalPath = existing.OverlayPath,
        SourceLayer = ResourceSourceLayer.Overlay,
        IndexedAt = existing.UpdatedAt
    };
}

static IReadOnlyList<string> BuildMatchHints(ResourceSummaryDto resource, long size, byte[] hash)
{
    var hashText = Convert.ToHexString(hash).ToLowerInvariant();
    return
    [
        $"path:{resource.NormalizedPath}",
        $"kind:{resource.Kind}",
        $"ext:{resource.Extension}",
        $"size:{size}",
        $"sha256:{hashText}"
    ];
}

static ResourceSignatureResponse BuildSignatureResponse(
    string profileId,
    ResourceSummaryDto resource,
    byte[] data,
    IReadOnlyList<string> warnings)
{
    var hash = SHA256.HashData(data);
    var header = string.Join(" ", data.Take(32).Select(item => item.ToString("X2")));
    return new ResourceSignatureResponse(
        profileId,
        resource.VirtualPath,
        resource.Kind,
        resource.Extension,
        data.LongLength,
        Convert.ToHexString(hash).ToLowerInvariant(),
        header,
        GuessContentType(resource.Extension),
        resource.SourceLayer.ToString(),
        BuildMatchHints(resource, data.LongLength, hash),
        warnings);
}

static async Task<ResourceBulkSignatureResponse> BuildSignatureSetAsync(
    string profileId,
    string query,
    ResourceKind? kind,
    string? extension,
    int take,
    string? oodlePath,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    bool useOverlay,
    CancellationToken cancellationToken)
{
    var search = await resourceIndex.SearchAsync(new ResourceSearchRequest(
        profileId,
        Query: query,
        Kind: kind,
        Extension: extension,
        Skip: 0,
        Take: Math.Clamp(take, 1, 500)), cancellationToken);
    var items = new List<ResourceSignatureResponse>();
    var warnings = new List<string>();
    foreach (var resource in search.Items)
    {
        var read = useOverlay
            ? await ReadResourceBytesPreferOverlayAsync(profileId, oodlePath, resource, profiles, nativeContentResolver, overlay, cancellationToken)
            : await ReadResourceBytesAsync(profileId, oodlePath, resource, profiles, nativeContentResolver, cancellationToken);
        if (!read.Ok)
        {
            warnings.Add($"{resource.VirtualPath}: {read.Message}");
            continue;
        }

        items.Add(BuildSignatureResponse(profileId, resource, read.Data, []));
    }

    return new ResourceBulkSignatureResponse(profileId, search.Total, items.Count, items, warnings);
}

static ResourceMatchItemDto BuildResourceMatch(ResourceSignatureResponse source, ResourceSignatureResponse target)
{
    var pathMatched = string.Equals(source.VirtualPath, target.VirtualPath, StringComparison.OrdinalIgnoreCase);
    var hashMatched = string.Equals(source.Sha256, target.Sha256, StringComparison.OrdinalIgnoreCase);
    var sizeMatched = source.Size == target.Size;
    var score = (hashMatched ? 70 : 0) + (pathMatched ? 20 : 0) + (sizeMatched ? 10 : 0);
    return new ResourceMatchItemDto(
        source.VirtualPath,
        target.VirtualPath,
        score,
        pathMatched,
        hashMatched,
        sizeMatched,
        source.Sha256,
        target.Sha256,
        source.Size,
        target.Size);
}

static ResourceMigrationPlanResponse BuildMigrationPlan(
    ResourceMigrationPlanRequest request,
    ResourceBulkSignatureResponse source,
    ResourceBulkSignatureResponse target)
{
    var targetByPath = target.Items.ToDictionary(item => item.VirtualPath, StringComparer.OrdinalIgnoreCase);
    var targetByHash = target.Items
        .GroupBy(item => item.Sha256, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
    var targetByName = target.Items
        .GroupBy(item => Path.GetFileName(item.VirtualPath), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
    var items = new List<ResourceMigrationPlanItemDto>();

    foreach (var sourceItem in source.Items)
    {
        ResourceSignatureResponse? targetItem = null;
        var status = ResourceMigrationStatus.Missing;
        var score = 0;
        var pathMatched = false;
        var hashMatched = false;
        var sizeMatched = false;
        var hints = new List<string>();

        if (targetByPath.TryGetValue(sourceItem.VirtualPath, out var samePath))
        {
            targetItem = samePath;
            pathMatched = true;
            hashMatched = string.Equals(sourceItem.Sha256, samePath.Sha256, StringComparison.OrdinalIgnoreCase);
            sizeMatched = sourceItem.Size == samePath.Size;
            status = hashMatched ? ResourceMigrationStatus.Direct : ResourceMigrationStatus.Candidate;
            score = (hashMatched ? 70 : 0) + 20 + (sizeMatched ? 10 : 0);
            hints.Add(hashMatched ? "路径和内容一致，可直接迁移。" : "路径一致但内容不同，建议人工确认后覆盖。");
        }
        else if (targetByHash.TryGetValue(sourceItem.Sha256, out var hashMatches))
        {
            targetItem = hashMatches
                .OrderBy(item => string.Equals(item.Extension, sourceItem.Extension, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(item => item.VirtualPath, StringComparer.OrdinalIgnoreCase)
                .First();
            hashMatched = true;
            sizeMatched = sourceItem.Size == targetItem.Size;
            status = ResourceMigrationStatus.HashMatch;
            score = 70 + (sizeMatched ? 10 : 0);
            hints.Add("内容 hash 一致但路径不同，可考虑重定向目标路径。");
        }
        else if (targetByName.TryGetValue(Path.GetFileName(sourceItem.VirtualPath), out var nameMatches))
        {
            targetItem = nameMatches
                .OrderByDescending(item => string.Equals(item.Extension, sourceItem.Extension, StringComparison.OrdinalIgnoreCase) ? 10 : 0)
                .ThenBy(item => Math.Abs(item.Size - sourceItem.Size))
                .First();
            sizeMatched = sourceItem.Size == targetItem.Size;
            status = ResourceMigrationStatus.Candidate;
            score = 35 + (sizeMatched ? 10 : 0);
            hints.Add("文件名相同但内容不同，只能作为候选。");
        }
        else
        {
            hints.Add("目标配置中未找到明显候选。");
        }

        var risk = PatchRiskClassifier.Classify(sourceItem.VirtualPath);
        if (risk == PatchRiskLevel.High)
        {
            hints.Add("高风险资源，迁移前必须人工确认。");
        }

        items.Add(new ResourceMigrationPlanItemDto(
            sourceItem.VirtualPath,
            targetItem?.VirtualPath,
            status,
            risk,
            sourceItem.Kind,
            sourceItem.Extension,
            score,
            pathMatched,
            hashMatched,
            sizeMatched,
            sourceItem.Sha256,
            targetItem?.Sha256,
            sourceItem.Size,
            targetItem?.Size,
            hints));
    }

    return new ResourceMigrationPlanResponse(
        request.SourceProfileId,
        request.TargetProfileId,
        source.Matched,
        target.Matched,
        items.Count,
        items.GroupBy(item => item.Status).ToDictionary(group => group.Key, group => group.Count()),
        items.GroupBy(item => item.RiskLevel).ToDictionary(group => group.Key, group => group.Count()),
        items.OrderByDescending(item => item.Score).ThenBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray(),
        source.Warnings.Concat(target.Warnings).ToArray());
}

static async Task<ResourceMigrationDraftResponse> BuildMigrationDraftAsync(
    ResourceMigrationDraftRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken)
{
    var source = await BuildSignatureSetAsync(
        request.SourceProfileId,
        request.Query ?? string.Empty,
        request.Kind,
        request.Extension,
        request.Take,
        request.SourceOodlePath,
        profiles,
        resourceIndex,
        nativeContentResolver,
        overlay,
        request.UseOverlay,
        cancellationToken);
    var target = await BuildSignatureSetAsync(
        request.TargetProfileId,
        request.Query ?? string.Empty,
        request.Kind,
        request.Extension,
        request.Take,
        request.TargetOodlePath,
        profiles,
        resourceIndex,
        nativeContentResolver,
        overlay,
        request.UseOverlay,
        cancellationToken);
    var planRequest = new ResourceMigrationPlanRequest(
        request.SourceProfileId,
        request.TargetProfileId,
        request.Query,
        request.Kind,
        request.Extension,
        request.Take,
        request.SourceOodlePath,
        request.TargetOodlePath,
        request.UseOverlay);
    var plan = BuildMigrationPlan(planRequest, source, target);
    var drafted = new List<ResourceMigrationDraftItemDto>();
    var skipped = new List<ResourceMigrationDraftSkippedItemDto>();
    var warnings = new List<string>(plan.Warnings);

    foreach (var item in plan.Items)
    {
        if (!ShouldDraftMigrationItem(request, item, out var reason))
        {
            skipped.Add(new ResourceMigrationDraftSkippedItemDto(item.SourcePath, item.TargetPath, item.Status, item.RiskLevel, reason));
            continue;
        }

        var sourceResource = await resourceIndex.GetByPathAsync(request.SourceProfileId, item.SourcePath, cancellationToken);
        var targetResource = item.TargetPath is null
            ? null
            : await resourceIndex.GetByPathAsync(request.TargetProfileId, item.TargetPath, cancellationToken);
        if (sourceResource is null)
        {
            skipped.Add(new ResourceMigrationDraftSkippedItemDto(item.SourcePath, item.TargetPath, item.Status, item.RiskLevel, "源资源未找到。"));
            continue;
        }

        if (targetResource is null || item.TargetPath is null)
        {
            skipped.Add(new ResourceMigrationDraftSkippedItemDto(item.SourcePath, item.TargetPath, item.Status, item.RiskLevel, "目标资源未找到。"));
            continue;
        }

        var read = request.UseOverlay
            ? await ReadResourceBytesPreferOverlayAsync(
                request.SourceProfileId,
                request.SourceOodlePath,
                sourceResource,
                profiles,
                nativeContentResolver,
                overlay,
                cancellationToken)
            : await ReadResourceBytesAsync(
                request.SourceProfileId,
                request.SourceOodlePath,
                sourceResource,
                profiles,
                nativeContentResolver,
                cancellationToken);
        if (!read.Ok)
        {
            skipped.Add(new ResourceMigrationDraftSkippedItemDto(item.SourcePath, item.TargetPath, item.Status, item.RiskLevel, read.Message));
            continue;
        }

        var entry = await overlay.SaveBytesAsync(
            request.TargetProfileId,
            targetResource.VirtualPath,
            read.Data,
            targetResource.PhysicalPath,
            HasBasePhysicalPath: targetResource.PhysicalPath is not null && File.Exists(targetResource.PhysicalPath),
            cancellationToken);
        drafted.Add(new ResourceMigrationDraftItemDto(
            item.SourcePath,
            targetResource.VirtualPath,
            item.Status,
            item.RiskLevel,
            read.Data.LongLength,
            item.SourceSha256,
            entry.OverlayPath));
    }

    return new ResourceMigrationDraftResponse(
        request.SourceProfileId,
        request.TargetProfileId,
        plan.Planned,
        drafted.Count,
        skipped.Count,
        drafted,
        skipped,
        warnings);
}

static async Task<ResourceMigrationDraftResponse> ApplySavedMigrationPlanAsync(
    ResourceMigrationPlanApplyRequest request,
    ResourceMigrationPlanEntryDto plan,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken)
{
    var draftRequest = new ResourceMigrationDraftRequest(
        plan.Criteria.SourceProfileId,
        plan.Criteria.TargetProfileId,
        plan.Criteria.Query,
        plan.Criteria.Kind,
        plan.Criteria.Extension,
        plan.Criteria.Take,
        request.SourceOodlePath ?? plan.Criteria.SourceOodlePath,
        plan.Criteria.TargetOodlePath,
        request.UseOverlay,
        request.IncludeHashMatches,
        request.IncludeCandidates,
        request.MaxRiskLevel);
    var drafted = new List<ResourceMigrationDraftItemDto>();
    var skipped = new List<ResourceMigrationDraftSkippedItemDto>();
    var warnings = new List<string>();

    foreach (var item in plan.Items)
    {
        if (!ShouldDraftMigrationItem(draftRequest, item, out var reason))
        {
            skipped.Add(new ResourceMigrationDraftSkippedItemDto(item.SourcePath, item.TargetPath, item.Status, item.RiskLevel, reason));
            continue;
        }

        var sourceResource = await resourceIndex.GetByPathAsync(plan.Criteria.SourceProfileId, item.SourcePath, cancellationToken);
        var targetResource = item.TargetPath is null
            ? null
            : await resourceIndex.GetByPathAsync(plan.Criteria.TargetProfileId, item.TargetPath, cancellationToken);
        if (sourceResource is null)
        {
            skipped.Add(new ResourceMigrationDraftSkippedItemDto(item.SourcePath, item.TargetPath, item.Status, item.RiskLevel, "源资源未找到。"));
            continue;
        }

        if (targetResource is null || item.TargetPath is null)
        {
            skipped.Add(new ResourceMigrationDraftSkippedItemDto(item.SourcePath, item.TargetPath, item.Status, item.RiskLevel, "目标资源未找到。"));
            continue;
        }

        var read = request.UseOverlay
            ? await ReadResourceBytesPreferOverlayAsync(
                plan.Criteria.SourceProfileId,
                request.SourceOodlePath ?? plan.Criteria.SourceOodlePath,
                sourceResource,
                profiles,
                nativeContentResolver,
                overlay,
                cancellationToken)
            : await ReadResourceBytesAsync(
                plan.Criteria.SourceProfileId,
                request.SourceOodlePath ?? plan.Criteria.SourceOodlePath,
                sourceResource,
                profiles,
                nativeContentResolver,
                cancellationToken);
        if (!read.Ok)
        {
            skipped.Add(new ResourceMigrationDraftSkippedItemDto(item.SourcePath, item.TargetPath, item.Status, item.RiskLevel, read.Message));
            continue;
        }

        var entry = await overlay.SaveBytesAsync(
            plan.Criteria.TargetProfileId,
            targetResource.VirtualPath,
            read.Data,
            targetResource.PhysicalPath,
            HasBasePhysicalPath: targetResource.PhysicalPath is not null && File.Exists(targetResource.PhysicalPath),
            cancellationToken);
        drafted.Add(new ResourceMigrationDraftItemDto(
            item.SourcePath,
            targetResource.VirtualPath,
            item.Status,
            item.RiskLevel,
            read.Data.LongLength,
            Convert.ToHexString(SHA256.HashData(read.Data)).ToLowerInvariant(),
            entry.OverlayPath));
    }

    return new ResourceMigrationDraftResponse(
        plan.Criteria.SourceProfileId,
        plan.Criteria.TargetProfileId,
        plan.Planned,
        drafted.Count,
        skipped.Count,
        drafted,
        skipped,
        warnings);
}

static async Task<ResourceMigrationPlanValidateResponse> ValidateSavedMigrationPlanAsync(
    ResourceMigrationPlanValidateRequest request,
    ResourceMigrationPlanEntryDto plan,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken)
{
    var items = new List<ResourceMigrationPlanValidationItemDto>();
    var warnings = new List<string>();

    foreach (var item in plan.Items)
    {
        var sourceResource = await resourceIndex.GetByPathAsync(plan.Criteria.SourceProfileId, item.SourcePath, cancellationToken);
        var targetResource = item.TargetPath is null
            ? null
            : await resourceIndex.GetByPathAsync(plan.Criteria.TargetProfileId, item.TargetPath, cancellationToken);
        if (sourceResource is null)
        {
            items.Add(new ResourceMigrationPlanValidationItemDto(
                item.SourcePath,
                item.TargetPath,
                ResourceMigrationPlanValidationState.Missing,
                item.RiskLevel,
                "源资源未找到。",
                item.SourceSha256,
                null,
                item.TargetSha256,
                null));
            continue;
        }

        if (item.TargetPath is not null && targetResource is null)
        {
            items.Add(new ResourceMigrationPlanValidationItemDto(
                item.SourcePath,
                item.TargetPath,
                ResourceMigrationPlanValidationState.Missing,
                item.RiskLevel,
                "目标资源未找到。",
                item.SourceSha256,
                null,
                item.TargetSha256,
                null));
            continue;
        }

        var sourceRead = request.UseOverlay
            ? await ReadResourceBytesPreferOverlayAsync(
                plan.Criteria.SourceProfileId,
                request.SourceOodlePath ?? plan.Criteria.SourceOodlePath,
                sourceResource,
                profiles,
                nativeContentResolver,
                overlay,
                cancellationToken)
            : await ReadResourceBytesAsync(
                plan.Criteria.SourceProfileId,
                request.SourceOodlePath ?? plan.Criteria.SourceOodlePath,
                sourceResource,
                profiles,
                nativeContentResolver,
                cancellationToken);
        if (!sourceRead.Ok)
        {
            var validationState = sourceRead.StatusCode == StatusCodes.Status404NotFound
                ? ResourceMigrationPlanValidationState.Missing
                : ResourceMigrationPlanValidationState.Blocked;
            items.Add(new ResourceMigrationPlanValidationItemDto(
                item.SourcePath,
                item.TargetPath,
                validationState,
                item.RiskLevel,
                sourceRead.Message,
                item.SourceSha256,
                null,
                item.TargetSha256,
                null));
            continue;
        }

        var currentSourceHash = Convert.ToHexString(SHA256.HashData(sourceRead.Data)).ToLowerInvariant();
        string? currentTargetHash = null;
        if (targetResource is not null)
        {
            var targetRead = request.UseOverlay
                ? await ReadResourceBytesPreferOverlayAsync(
                    plan.Criteria.TargetProfileId,
                    request.TargetOodlePath ?? plan.Criteria.TargetOodlePath,
                    targetResource,
                    profiles,
                    nativeContentResolver,
                    overlay,
                    cancellationToken)
                : await ReadResourceBytesAsync(
                    plan.Criteria.TargetProfileId,
                    request.TargetOodlePath ?? plan.Criteria.TargetOodlePath,
                    targetResource,
                    profiles,
                    nativeContentResolver,
                    cancellationToken);
            if (targetRead.Ok)
            {
                currentTargetHash = Convert.ToHexString(SHA256.HashData(targetRead.Data)).ToLowerInvariant();
            }
            else
            {
                warnings.Add($"{item.TargetPath}: {targetRead.Message}");
            }
        }

        var sourceChanged = !string.IsNullOrWhiteSpace(item.SourceSha256)
            && !string.Equals(item.SourceSha256, currentSourceHash, StringComparison.OrdinalIgnoreCase);
        var targetChanged = !string.IsNullOrWhiteSpace(item.TargetSha256)
            && currentTargetHash is not null
            && !string.Equals(item.TargetSha256, currentTargetHash, StringComparison.OrdinalIgnoreCase);
        var state = sourceChanged || targetChanged
            ? ResourceMigrationPlanValidationState.Changed
            : ResourceMigrationPlanValidationState.Ready;
        var reason = state == ResourceMigrationPlanValidationState.Ready
            ? "资源与保存方案一致。"
            : sourceChanged && targetChanged
                ? "源资源和目标资源都已变化。"
                : sourceChanged
                    ? "源资源已变化。"
                    : "目标资源已变化。";
        items.Add(new ResourceMigrationPlanValidationItemDto(
            item.SourcePath,
            item.TargetPath,
            state,
            item.RiskLevel,
            reason,
            item.SourceSha256,
            currentSourceHash,
            item.TargetSha256,
            currentTargetHash));
    }

    return new ResourceMigrationPlanValidateResponse(
        plan.Criteria.SourceProfileId,
        plan.Criteria.TargetProfileId,
        plan.Id,
        items.Count,
        items.Count(item => item.State == ResourceMigrationPlanValidationState.Ready),
        items.Count(item => item.State == ResourceMigrationPlanValidationState.Changed),
        items.Count(item => item.State == ResourceMigrationPlanValidationState.Missing),
        items.Count(item => item.State == ResourceMigrationPlanValidationState.Blocked),
        items,
        warnings);
}

static bool ShouldDraftMigrationItem(
    ResourceMigrationDraftRequest request,
    ResourceMigrationPlanItemDto item,
    out string reason)
{
    if (item.TargetPath is null)
    {
        reason = "没有目标路径。";
        return false;
    }

    if (item.RiskLevel > request.MaxRiskLevel)
    {
        reason = $"风险等级 {item.RiskLevel} 超过自动草稿上限 {request.MaxRiskLevel}。";
        return false;
    }

    if (item.Status == ResourceMigrationStatus.Direct)
    {
        reason = string.Empty;
        return true;
    }

    if (item.Status == ResourceMigrationStatus.HashMatch)
    {
        reason = string.Empty;
        return request.IncludeHashMatches;
    }

    if (item.Status == ResourceMigrationStatus.Candidate)
    {
        reason = request.IncludeCandidates ? string.Empty : "候选项需要人工确认。";
        return request.IncludeCandidates;
    }

    reason = "未找到可迁移目标。";
    return false;
}

static async Task<MigrationApplyResult> ApplyMigrationItemAsync(
    ResourceMigrationApplyItemRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    NativeBundleResourceContentResolver nativeContentResolver,
    OverlayStore overlay,
    CancellationToken cancellationToken)
{
    var sourceResource = await resourceIndex.GetByPathAsync(request.SourceProfileId, request.SourcePath, cancellationToken);
    if (sourceResource is null)
    {
        return MigrationApplyResult.Fail(StatusCodes.Status404NotFound, "source_not_found", "源资源未找到。");
    }

    var targetResource = await resourceIndex.GetByPathAsync(request.TargetProfileId, request.TargetPath, cancellationToken);
    if (targetResource is null)
    {
        return MigrationApplyResult.Fail(StatusCodes.Status404NotFound, "target_not_found", "目标资源未找到。");
    }

    var risk = PatchRiskClassifier.Classify(targetResource.VirtualPath);
    if (risk > request.MaxRiskLevel)
    {
        return MigrationApplyResult.Fail(StatusCodes.Status400BadRequest, "risk_too_high", $"风险等级 {risk} 超过确认上限 {request.MaxRiskLevel}。");
    }

    var read = request.UseOverlay
        ? await ReadResourceBytesPreferOverlayAsync(
            request.SourceProfileId,
            request.SourceOodlePath,
            sourceResource,
            profiles,
            nativeContentResolver,
            overlay,
            cancellationToken)
        : await ReadResourceBytesAsync(
            request.SourceProfileId,
            request.SourceOodlePath,
            sourceResource,
            profiles,
            nativeContentResolver,
            cancellationToken);
    if (!read.Ok)
    {
        return MigrationApplyResult.Fail(read.StatusCode, read.ErrorCode, read.Message);
    }

    var entry = await overlay.SaveBytesAsync(
        request.TargetProfileId,
        targetResource.VirtualPath,
        read.Data,
        targetResource.PhysicalPath,
        HasBasePhysicalPath: targetResource.PhysicalPath is not null && File.Exists(targetResource.PhysicalPath),
        cancellationToken);
    var data = new ResourceMigrationApplyItemResponse(
        request.SourceProfileId,
        request.TargetProfileId,
        sourceResource.VirtualPath,
        targetResource.VirtualPath,
        risk,
        read.Data.LongLength,
        Convert.ToHexString(SHA256.HashData(read.Data)).ToLowerInvariant(),
        entry.OverlayPath);
    return MigrationApplyResult.Success(data);
}

static string SafeExportPath(string root, string virtualPath)
{
    var fullRoot = Path.GetFullPath(root);
    var fullPath = Path.GetFullPath(Path.Combine(fullRoot, virtualPath.Replace('/', Path.DirectorySeparatorChar)));
    var rootWithSeparator = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
    {
        throw new ArgumentException("Export path escaped export root.");
    }

    return fullPath;
}

static bool IsSubPath(string root, string path)
{
    var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    var fullPath = Path.GetFullPath(path);
    return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
}

sealed record ResourceBytesReadResult(
    bool Ok,
    byte[] Data,
    int StatusCode,
    string ErrorCode,
    string Message,
    bool FromOverlay = false)
{
    public static ResourceBytesReadResult Success(byte[] data, bool FromOverlay = false)
    {
        return new ResourceBytesReadResult(true, data, StatusCodes.Status200OK, string.Empty, string.Empty, FromOverlay);
    }

    public static ResourceBytesReadResult Fail(int statusCode, string errorCode, string message)
    {
        return new ResourceBytesReadResult(false, [], statusCode, errorCode, message);
    }
}

sealed record TextDocument(
    string Text,
    string NewLine,
    string? EncodingName);

sealed record TextChunkReplaceResult(
    string Text,
    int StartLine,
    int EndLine,
    int LineCount,
    int TotalLines,
    string NewLine);

sealed record TableReadResult(
    bool Ok,
    ResourceSummaryDto? Resource,
    byte[]? Data,
    TableSchemaDto? Schema,
    TableInspectResponse? Inspect,
    int StatusCode,
    string ErrorCode,
    string Message)
{
    public static TableReadResult Success(
        ResourceSummaryDto resource,
        byte[] data,
        TableSchemaDto? schema,
        TableInspectResponse inspect)
    {
        return new TableReadResult(true, resource, data, schema, inspect, StatusCodes.Status200OK, string.Empty, string.Empty);
    }

    public static TableReadResult Fail(int statusCode, string errorCode, string message)
    {
        return new TableReadResult(false, null, null, null, null, statusCode, errorCode, message);
    }
}

sealed record StructuredTextInspection(
    IReadOnlyList<StructuredTextNodeDto> Nodes,
    IReadOnlyList<string> Warnings);

sealed record StructuredTextApplyResult(
    string Text,
    int Edited,
    IReadOnlyList<string> Warnings);

sealed record MigrationApplyResult(
    ResourceMigrationApplyItemResponse? Data,
    string? ErrorCode,
    string Message,
    int StatusCode)
{
    public static MigrationApplyResult Success(ResourceMigrationApplyItemResponse data)
    {
        return new MigrationApplyResult(data, null, string.Empty, StatusCodes.Status200OK);
    }

    public static MigrationApplyResult Fail(int statusCode, string errorCode, string message)
    {
        return new MigrationApplyResult(null, errorCode, message, statusCode);
    }
}

sealed record BatchScriptExecutionResult(
    bool Ok,
    BatchScriptRunResponse? Response,
    string ErrorCode,
    string Message)
{
    public static BatchScriptExecutionResult Success(BatchScriptRunResponse response)
    {
        return new BatchScriptExecutionResult(true, response, string.Empty, string.Empty);
    }

    public static BatchScriptExecutionResult Fail(string errorCode, string message)
    {
        return new BatchScriptExecutionResult(false, null, errorCode, message);
    }
}

sealed class IDisposableOodleCodec(IOodleCodec inner) : IOodleCodec, IDisposable
{
    public bool IsAvailable => inner.IsAvailable;

    public int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, int compressor)
    {
        return inner.Decompress(compressed, output, compressor);
    }

    public void Dispose()
    {
        if (inner is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

public partial class Program
{
}
