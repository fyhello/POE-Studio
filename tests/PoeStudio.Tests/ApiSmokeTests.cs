using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PoeStudio.Contracts;

namespace PoeStudio.Tests;

public sealed class ApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ApiSmokeTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PoeStudio:WorkspaceRoot", Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N")));
        });
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Workbench_home_page_is_served()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("POE Studio", html);
    }

    [Fact]
    public async Task Detect_returns_bundles_layout()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(bundles);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "_.index.bin"), [1, 2, 3]);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/profiles/detect", new DetectClientRequest(root));
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<DetectClientResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload?.Ok);
        Assert.Equal(ClientEntryKind.Bundles2, payload?.Data?.EntryKind);
    }

    [Fact]
    public async Task Create_then_list_profiles_preserves_oodle_status()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var request = new CreateProfileRequest(
            DisplayName: "Steam client",
            RootPath: root,
            Platform: ClientPlatform.Steam,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: Path.Combine(root, "Bundles2"),
            IndexPath: Path.Combine(root, "Bundles2", "_.index.bin"),
            OodleStatus: OodleStatus.Found,
            ClientFingerprint: "fingerprint");
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/profiles", request);
        var createPayload = await createResponse.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();
        var listPayload = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<ClientProfileDto>>>("/api/profiles");

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.Equal(OodleStatus.Found, createPayload?.Data?.OodleStatus);
        var profile = Assert.Single(listPayload?.Data ?? []);
        Assert.Equal(OodleStatus.Found, profile.OodleStatus);
    }

    [Fact]
    public async Task Build_index_then_search_resources_returns_matches()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "metadata", "items"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "metadata", "items", "amulet.ot"), "item");
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "WeGame",
            RootPath: root,
            Platform: ClientPlatform.WeGame,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: Path.Combine(bundles, "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();

        var build = await client.PostAsJsonAsync("/api/index/build", new ResourceIndexBuildRequest(created!.Data!.Id));
        var buildPayload = await build.Content.ReadFromJsonAsync<ApiResponse<ResourceIndexBuildResponse>>();
        var search = await client.PostAsJsonAsync("/api/resources/search", new ResourceSearchRequest(created.Data.Id, Query: "amulet"));
        var searchPayload = await search.Content.ReadFromJsonAsync<ApiResponse<ResourceSearchResponse>>();

        Assert.Equal(HttpStatusCode.OK, build.StatusCode);
        Assert.Equal(1, buildPayload?.Data?.TotalResources);
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        var item = Assert.Single(searchPayload?.Data?.Items ?? []);
        Assert.Equal("metadata/items/amulet.ot", item.VirtualPath);
    }

    [Fact]
    public async Task Preview_resource_returns_text_content()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "config"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "config", "sample.json"), "{\"ok\":true}");
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "WeGame",
            RootPath: root,
            Platform: ClientPlatform.WeGame,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: Path.Combine(bundles, "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();
        await client.PostAsJsonAsync("/api/index/build", new ResourceIndexBuildRequest(created!.Data!.Id));

        var preview = await client.PostAsJsonAsync("/api/preview", new ResourcePreviewRequest(created.Data.Id, "config/sample.json"));
        var payload = await preview.Content.ReadFromJsonAsync<ApiResponse<ResourcePreviewResponse>>();

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(PreviewKind.Text, payload?.Data?.Kind);
        Assert.Contains("\"ok\"", payload?.Data?.Text);
    }

    [Fact]
    public async Task Overlay_save_list_diff_and_revert_work()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "sample.txt"), "base");
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "WeGame",
            RootPath: root,
            Platform: ClientPlatform.WeGame,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: Path.Combine(bundles, "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();
        await client.PostAsJsonAsync("/api/index/build", new ResourceIndexBuildRequest(created!.Data!.Id));

        var save = await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(created.Data.Id, "text/sample.txt", "overlay"));
        var savePayload = await save.Content.ReadFromJsonAsync<ApiResponse<OverlayEntryDto>>();
        var list = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(created.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();
        var diff = await client.PostAsJsonAsync("/api/overlay/diff", new OverlayDiffRequest(created.Data.Id, "text/sample.txt"));
        var diffPayload = await diff.Content.ReadFromJsonAsync<ApiResponse<OverlayDiffResponse>>();
        var revert = await client.PostAsJsonAsync("/api/overlay/revert", new RevertOverlayRequest(created.Data.Id, "text/sample.txt"));
        var revertPayload = await revert.Content.ReadFromJsonAsync<ApiResponse<RevertOverlayResponse>>();

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal("text/sample.txt", savePayload?.Data?.VirtualPath);
        Assert.Single(listPayload?.Data?.Items ?? []);
        Assert.True(diffPayload?.Data?.TextChanged);
        Assert.True(revertPayload?.Data?.Removed);
    }

    [Fact]
    public async Task Patch_dry_run_and_build_return_manifest_and_zip_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "sample.txt"), "base");
        await File.WriteAllBytesAsync(Path.Combine(bundles, "_.index.bin"), [1, 2, 3]);
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "WeGame",
            RootPath: root,
            Platform: ClientPlatform.WeGame,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: Path.Combine(bundles, "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();
        await client.PostAsJsonAsync("/api/index/build", new ResourceIndexBuildRequest(created!.Data!.Id));
        await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(created.Data.Id, "text/sample.txt", "overlay"));

        var dryRun = await client.PostAsJsonAsync("/api/patch/dry-run", new PatchDryRunRequest(created.Data.Id));
        var dryRunPayload = await dryRun.Content.ReadFromJsonAsync<ApiResponse<PatchDryRunResponse>>();
        var build = await client.PostAsJsonAsync("/api/patch/build", new PatchBuildRequest(created.Data.Id, PatchZipTemplate.Epic));
        var buildPayload = await build.Content.ReadFromJsonAsync<ApiResponse<PatchBuildResponse>>();

        Assert.Equal(HttpStatusCode.OK, dryRun.StatusCode);
        Assert.Equal(1, dryRunPayload?.Data?.TotalChanges);
        Assert.Equal(HttpStatusCode.OK, build.StatusCode);
        Assert.True(File.Exists(buildPayload?.Data?.ManifestPath));
        Assert.True(File.Exists(buildPayload?.Data?.RollbackManifestPath));
        Assert.True(File.Exists(buildPayload?.Data?.ZipPath));
        Assert.Equal(PatchBuildMode.OverlayBundleMvp, buildPayload?.Data?.BuildMode);
    }

    [Fact]
    public async Task Patch_build_job_eventually_exposes_zip_result()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "sample.txt"), "base");
        await File.WriteAllBytesAsync(Path.Combine(bundles, "_.index.bin"), [1, 2, 3]);
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "WeGame",
            RootPath: root,
            Platform: ClientPlatform.WeGame,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: Path.Combine(bundles, "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();
        await client.PostAsJsonAsync("/api/index/build", new ResourceIndexBuildRequest(created!.Data!.Id));
        await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(created.Data.Id, "text/sample.txt", "overlay"));

        var start = await client.PostAsJsonAsync("/api/jobs/patch/build", new PatchBuildRequest(created.Data.Id, PatchZipTemplate.WeGame));
        var startPayload = await start.Content.ReadFromJsonAsync<ApiResponse<JobSnapshotDto>>();
        JobSnapshotDto? snapshot = null;
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(25);
            var statusPayload = await client.GetFromJsonAsync<ApiResponse<JobSnapshotDto>>($"/api/jobs/{startPayload!.Data!.Id}");
            snapshot = statusPayload?.Data;
            if (snapshot?.Status is JobStatus.Succeeded or JobStatus.Failed)
            {
                break;
            }
        }

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.NotNull(snapshot);
        Assert.Equal(JobStatus.Succeeded, snapshot!.Status);
        Assert.Contains("zipPath", snapshot.ResultJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Patch_build_history_lists_recent_zip_outputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "sample.txt"), "base");
        await File.WriteAllBytesAsync(Path.Combine(bundles, "_.index.bin"), [1, 2, 3]);
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "WeGame",
            RootPath: root,
            Platform: ClientPlatform.WeGame,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: Path.Combine(bundles, "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();
        await client.PostAsJsonAsync("/api/index/build", new ResourceIndexBuildRequest(created!.Data!.Id));
        await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(created.Data.Id, "text/sample.txt", "overlay"));
        await client.PostAsJsonAsync("/api/patch/build", new PatchBuildRequest(created.Data.Id, PatchZipTemplate.WeGame));

        var history = await client.PostAsJsonAsync("/api/patch/build-history", new PatchBuildHistoryRequest(created.Data.Id));
        var payload = await history.Content.ReadFromJsonAsync<ApiResponse<PatchBuildHistoryResponse>>();

        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var item = Assert.Single(payload?.Data?.Items ?? []);
        Assert.True(File.Exists(item.ZipPath));
        Assert.True(File.Exists(item.ManifestPath));
        Assert.True(item.ZipSize > 0);
    }

    [Fact]
    public async Task Patch_build_native_mode_returns_clear_failure_until_writer_is_available()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "sample.txt"), "base");
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "WeGame",
            RootPath: root,
            Platform: ClientPlatform.WeGame,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: Path.Combine(bundles, "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();
        await client.PostAsJsonAsync("/api/index/build", new ResourceIndexBuildRequest(created!.Data!.Id));
        await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(created.Data.Id, "text/sample.txt", "overlay"));

        var build = await client.PostAsJsonAsync("/api/patch/build", new PatchBuildRequest(created.Data.Id, WriterKind: PatchPackageWriterKind.NativeBundles2));
        var payload = await build.Content.ReadFromJsonAsync<ApiResponse<PatchBuildResponse>>();

        Assert.Equal(HttpStatusCode.BadRequest, build.StatusCode);
        Assert.False(payload?.Ok);
        Assert.Equal("native_writer_unavailable", payload?.ErrorCode);
    }

    [Fact]
    public async Task Native_index_probe_returns_header_status()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(bundles);
        var indexPath = Path.Combine(bundles, "_.index.bin");
        await WriteIndexHeaderAsync(indexPath);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/native/bundles2/probe-index", new NativeIndexProbeRequest(indexPath, OodleAvailable: false));
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<NativeIndexProbeResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload?.Data?.HeaderValid);
        Assert.Equal(NativeIndexProbeStatus.HeaderOnlyOodleMissing, payload?.Data?.Status);
    }

    [Fact]
    public async Task Native_index_decompress_returns_oodle_missing_by_default()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(bundles);
        var indexPath = Path.Combine(bundles, "_.index.bin");
        await WriteIndexHeaderAsync(indexPath);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/native/bundles2/decompress-index", new NativeIndexDecompressRequest("profile", indexPath));
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<NativeIndexDecompressResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload?.Ok);
        Assert.Equal(NativeIndexDecompressStatus.OodleMissing, payload?.Data?.Status);
        Assert.False(payload?.Data?.Ok);
    }

    [Fact]
    public async Task Native_index_decompress_reports_missing_request_oodle_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(bundles);
        var indexPath = Path.Combine(bundles, "_.index.bin");
        await WriteIndexHeaderAsync(indexPath);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/native/bundles2/decompress-index",
            new NativeIndexDecompressRequest("profile", indexPath, Path.Combine(root, "missing-oo2core.dll")));
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<NativeIndexDecompressResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(NativeIndexDecompressStatus.OodleMissing, payload?.Data?.Status);
        Assert.Contains(payload?.Data?.Warnings ?? [], warning => warning.Contains("oo2core", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Native_index_parse_returns_record_counts()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var indexCache = Path.Combine(root, "index.decompressed.bin");
        await WriteDecompressedIndexAsync(indexCache);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/native/bundles2/parse-index-cache", new NativeIndexParseRequest(indexCache));
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<NativeIndexParseResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload?.Data?.Ok);
        Assert.Equal(1, payload?.Data?.BundleCount);
        Assert.Equal(1, payload?.Data?.FileCount);
        Assert.Equal(1, payload?.Data?.DirectoryCount);
    }

    [Fact]
    public async Task Native_resource_index_build_returns_not_found_for_unknown_profile()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/native/bundles2/build-resource-index",
            new NativeResourceIndexBuildRequest("missing-profile", "C:/missing/_.index.bin", "C:/missing/oo2core.dll"));
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<NativeResourceIndexBuildResponse>>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(payload?.Ok);
        Assert.Equal("profile_not_found", payload?.ErrorCode);
    }

    [Fact]
    public async Task Native_resource_index_build_reports_oodle_missing_without_crashing()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(bundles);
        var indexPath = Path.Combine(bundles, "_.index.bin");
        await WriteIndexHeaderAsync(indexPath);
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "Official",
            RootPath: root,
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: indexPath,
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();

        var response = await client.PostAsJsonAsync(
            "/api/native/bundles2/build-resource-index",
            new NativeResourceIndexBuildRequest(created!.Data!.Id, indexPath));
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<NativeResourceIndexBuildResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload?.Ok);
        Assert.False(payload?.Data?.Ok);
        Assert.Equal(0, payload?.Data?.ResolvedResources);
        Assert.Contains(payload?.Data?.Warnings ?? [], warning => warning.Contains("Oodle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Native_resource_index_job_returns_job_snapshot_immediately()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(bundles);
        var indexPath = Path.Combine(bundles, "_.index.bin");
        await WriteIndexHeaderAsync(indexPath);
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "Official",
            RootPath: root,
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: indexPath,
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();

        var start = await client.PostAsJsonAsync(
            "/api/jobs/native/bundles2/build-resource-index",
            new NativeResourceIndexBuildRequest(created!.Data!.Id, indexPath));
        var startPayload = await start.Content.ReadFromJsonAsync<ApiResponse<JobSnapshotDto>>();
        var status = await client.GetAsync($"/api/jobs/{startPayload!.Data!.Id}");
        var statusPayload = await status.Content.ReadFromJsonAsync<ApiResponse<JobSnapshotDto>>();

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("native-bundles2-resource-index", startPayload.Data.Kind);
        Assert.InRange(startPayload.Data.ProgressPercent, 0, 100);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(startPayload.Data.Id, statusPayload?.Data?.Id);
    }

    [Fact]
    public async Task Native_resource_index_job_eventually_exposes_result_json()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(bundles);
        var indexPath = Path.Combine(bundles, "_.index.bin");
        await WriteIndexHeaderAsync(indexPath);
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "Official",
            RootPath: root,
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: indexPath,
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();
        var start = await client.PostAsJsonAsync(
            "/api/jobs/native/bundles2/build-resource-index",
            new NativeResourceIndexBuildRequest(created!.Data!.Id, indexPath));
        var startPayload = await start.Content.ReadFromJsonAsync<ApiResponse<JobSnapshotDto>>();

        JobSnapshotDto? snapshot = null;
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(25);
            var statusPayload = await client.GetFromJsonAsync<ApiResponse<JobSnapshotDto>>($"/api/jobs/{startPayload!.Data!.Id}");
            snapshot = statusPayload?.Data;
            if (snapshot?.Status is JobStatus.Succeeded or JobStatus.Failed)
            {
                break;
            }
        }

        Assert.NotNull(snapshot);
        Assert.Equal(JobStatus.Succeeded, snapshot!.Status);
        Assert.Equal(100, snapshot.ProgressPercent);
        Assert.Contains("\"ok\":false", snapshot.ResultJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_native_resource_reports_oodle_missing_when_no_oodle_path_is_supplied()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "Metadata"));
        await File.WriteAllBytesAsync(Path.Combine(bundles, "Metadata", "Text.bundle.bin"), NativeBundleTestData.CreateBundle([1, 2, 3, 4]));
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "Official",
            RootPath: root,
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: Path.Combine(bundles, "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();
        var store = factory.Services.GetRequiredService<PoeStudio.Storage.Resources.ResourceIndexStore>();
        await store.SaveAsync(created!.Data!.Id, [
            new ResourceSummaryDto(
                Id: "native",
                ProfileId: created.Data.Id,
                VirtualPath: "metadata/text/sample.txt",
                NormalizedPath: "metadata/text/sample.txt",
                Extension: ".txt",
                Kind: ResourceKind.Text,
                Size: 4,
                PhysicalPath: "native-bundles2://Metadata/Text.bundle.bin#offset=0&size=4",
                SourceLayer: ResourceSourceLayer.Base,
                IndexedAt: DateTimeOffset.UtcNow)
        ], [], CancellationToken.None);

        var response = await client.PostAsJsonAsync("/api/preview", new ResourcePreviewRequest(created.Data.Id, "metadata/text/sample.txt"));
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ResourcePreviewResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PreviewKind.Unavailable, payload?.Data?.Kind);
        Assert.Equal("native_oodle_missing", payload?.Data?.ErrorCode);
    }

    private static async Task WriteIndexHeaderAsync(string path)
    {
        await using var stream = File.Create(path);
        await using var writer = new BinaryWriter(stream);
        writer.Write(1024);
        writer.Write(512);
        writer.Write(52);
        writer.Write(9);
        writer.Write(1);
        writer.Write(1024L);
        writer.Write(512L);
        writer.Write(1);
        writer.Write(262144);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(512);
        writer.Write(new byte[512]);
    }

    private static async Task WriteDecompressedIndexAsync(string path)
    {
        await using var stream = File.Create(path);
        await using var writer = new BinaryWriter(stream);
        writer.Write(1);
        var bundlePath = System.Text.Encoding.UTF8.GetBytes("Tiny/V0.1");
        writer.Write(bundlePath.Length);
        writer.Write(bundlePath);
        writer.Write(100);
        writer.Write(1);
        writer.Write(123UL);
        writer.Write(0);
        writer.Write(0);
        writer.Write(10);
        writer.Write(1);
        writer.Write(456UL);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
    }
}
