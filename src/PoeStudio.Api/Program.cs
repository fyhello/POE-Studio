using PoeStudio.Contracts;
using PoeStudio.Api.Jobs;
using PoeStudio.Core.ClientDetection;
using PoeStudio.Core.Native;
using PoeStudio.Core.Patching;
using PoeStudio.Core.Preview;
using PoeStudio.Core.Resources;
using PoeStudio.Core.Workspace;
using PoeStudio.Storage.Overlay;
using PoeStudio.Storage.Profiles;
using PoeStudio.Storage.Resources;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors();
builder.Services.AddSingleton<InMemoryJobStore>();
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var workspaceRoot = config["PoeStudio:WorkspaceRoot"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoeStudio");
    return new ProfileStore(workspaceRoot);
});
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var workspaceRoot = config["PoeStudio:WorkspaceRoot"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoeStudio");
    return new ResourceIndexStore(workspaceRoot);
});
builder.Services.AddSingleton<FileSystemResourceIndexer>();
builder.Services.AddSingleton<NativeBundles2IndexReader>();
builder.Services.AddSingleton<NativeIndexRecordParser>();
builder.Services.AddSingleton<NativeBundles2ResourceIndexer>();
builder.Services.AddSingleton<IOodleCodec, MissingOodleCodec>();
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var workspaceRoot = config["PoeStudio:WorkspaceRoot"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoeStudio");
    return new NativeIndexCacheService(workspaceRoot, sp.GetRequiredService<IOodleCodec>());
});
builder.Services.AddSingleton<OodleCodecFactory>(_ => path => new NativeOodleCodec(path));
builder.Services.AddSingleton<NativeIndexPathService>();
builder.Services.AddSingleton(sp => new NativeBundleResourceContentResolver(sp.GetRequiredService<IOodleCodec>()));
builder.Services.AddSingleton(sp => new ResourcePreviewService(sp.GetRequiredService<NativeBundleResourceContentResolver>()));
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var workspaceRoot = config["PoeStudio:WorkspaceRoot"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoeStudio");
    return new OverlayStore(workspaceRoot);
});
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var workspaceRoot = config["PoeStudio:WorkspaceRoot"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoeStudio");
    var overlay = sp.GetRequiredService<OverlayStore>();
    return new PatchBuildService(workspaceRoot, overlay);
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => ApiResponse<object>.Success(new
{
    status = "ok",
    utcTime = DateTimeOffset.UtcNow
}));

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

app.MapPost("/api/preview", async (
    ResourcePreviewRequest request,
    ProfileStore profiles,
    ResourceIndexStore resourceIndex,
    ResourcePreviewService preview,
    CancellationToken cancellationToken) =>
{
    var resource = await resourceIndex.GetByPathAsync(request.ProfileId, request.VirtualPath, cancellationToken);
    if (resource is null)
    {
        return Results.NotFound(ApiResponse<ResourcePreviewResponse>.Failure("resource_not_found", "未找到资源，请先建立索引。"));
    }

    var profile = NativeBundleResourceContentResolver.IsNativeResource(resource)
        ? await profiles.GetAsync(request.ProfileId, cancellationToken)
        : null;
    var response = await preview.BuildPreviewAsync(resource, profile, request.Limit, request.OodlePath, cancellationToken);
    return Results.Ok(ApiResponse<ResourcePreviewResponse>.Success(response));
});

app.MapPost("/api/overlay/save-text", async (
    SaveTextOverlayRequest request,
    ResourceIndexStore resourceIndex,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var resource = await resourceIndex.GetByPathAsync(request.ProfileId, request.VirtualPath, cancellationToken);
    var basePath = request.BasePhysicalPath ?? resource?.PhysicalPath;
    var saveRequest = request with { BasePhysicalPath = basePath, HasBasePhysicalPath = basePath is not null };
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

app.MapPost("/api/overlay/list", async (
    OverlayListRequest request,
    OverlayStore overlay,
    CancellationToken cancellationToken) =>
{
    var response = await overlay.ListAsync(request.ProfileId, cancellationToken);
    return Results.Ok(ApiResponse<OverlayListResponse>.Success(response));
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
    IConfiguration config,
    CancellationToken cancellationToken) =>
{
    var profile = await profiles.GetAsync(request.ProfileId, cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(ApiResponse<PatchBuildHistoryResponse>.Failure("profile_not_found", "未找到客户端配置。"));
    }

    var workspaceRoot = config["PoeStudio:WorkspaceRoot"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoeStudio");
    var layout = WorkspaceLayout.ForProfile(workspaceRoot, request.ProfileId);
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
            return new PatchBuildHistoryItemDto(
                buildId,
                outputDirectory,
                zip.FullName,
                File.Exists(manifestPath) ? manifestPath : null,
                File.Exists(rollbackPath) ? rollbackPath : null,
                zip.CreationTimeUtc <= DateTime.MinValue ? DateTimeOffset.UtcNow : new DateTimeOffset(zip.CreationTimeUtc, TimeSpan.Zero),
                zip.Length);
        })
        .OrderByDescending(item => item.CreatedAt)
        .Take(20)
        .ToArray();

    return Results.Ok(ApiResponse<PatchBuildHistoryResponse>.Success(new PatchBuildHistoryResponse(request.ProfileId, items)));
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

static IDisposableOodleCodec CreateDisposableCodec(string? oodlePath, OodleCodecFactory factory)
{
    if (string.IsNullOrWhiteSpace(oodlePath))
    {
        return new IDisposableOodleCodec(new MissingOodleCodec());
    }

    return new IDisposableOodleCodec(factory(oodlePath));
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
