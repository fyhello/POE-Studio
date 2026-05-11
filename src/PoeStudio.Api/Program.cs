using PoeStudio.Contracts;
using PoeStudio.Core.ClientDetection;
using PoeStudio.Core.Patching;
using PoeStudio.Core.Preview;
using PoeStudio.Core.Resources;
using PoeStudio.Storage.Overlay;
using PoeStudio.Storage.Profiles;
using PoeStudio.Storage.Resources;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors();
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
builder.Services.AddSingleton<ResourcePreviewService>();
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
    ResourceIndexStore resourceIndex,
    ResourcePreviewService preview,
    CancellationToken cancellationToken) =>
{
    var resource = await resourceIndex.GetByPathAsync(request.ProfileId, request.VirtualPath, cancellationToken);
    if (resource is null)
    {
        return Results.NotFound(ApiResponse<ResourcePreviewResponse>.Failure("resource_not_found", "未找到资源，请先建立索引。"));
    }

    var response = await preview.BuildPreviewAsync(resource, request.Limit, cancellationToken);
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

    var response = await patchBuild.BuildAsync(request, profile, cancellationToken);
    return Results.Ok(ApiResponse<PatchBuildResponse>.Success(response));
});

app.Run();

public partial class Program
{
}
