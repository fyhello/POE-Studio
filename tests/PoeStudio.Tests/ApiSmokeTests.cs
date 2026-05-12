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
    public async Task Diagnostics_returns_workspace_and_profile_count()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/diagnostics");
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<AppDiagnosticsDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload?.Ok);
        Assert.True(payload?.Data?.WorkspaceWritable);
        Assert.Equal("ok", payload?.Data?.Status);
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
    public async Task Detect_and_save_creates_profile_in_one_request()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(bundles);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "_.index.bin"), [1, 2, 3]);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/profiles/detect-and-save", new DetectClientRequest(root));
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();
        var listPayload = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<ClientProfileDto>>>("/api/profiles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload?.Ok);
        Assert.Equal(ClientEntryKind.Bundles2, payload?.Data?.EntryKind);
        Assert.Equal(payload?.Data?.Id, Assert.Single(listPayload?.Data ?? []).Id);
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
    public async Task Resource_signature_returns_hash_header_and_match_hints()
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

        var signature = await client.PostAsJsonAsync("/api/resources/signature", new ResourceSignatureRequest(created.Data.Id, "config/sample.json"));
        var payload = await signature.Content.ReadFromJsonAsync<ApiResponse<ResourceSignatureResponse>>();

        Assert.Equal(HttpStatusCode.OK, signature.StatusCode);
        Assert.Equal(ResourceKind.Text, payload?.Data?.Kind);
        Assert.Equal(11, payload?.Data?.Size);
        Assert.Equal(64, payload?.Data?.Sha256.Length);
        Assert.StartsWith("7B 22 6F 6B", payload?.Data?.HeaderHex);
        Assert.Contains("path:config/sample.json", payload?.Data?.MatchHints ?? []);
    }

    [Fact]
    public async Task Bulk_resource_signature_returns_signature_manifest_for_search_results()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "config"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "config", "one.json"), "{\"one\":true}");
        await File.WriteAllTextAsync(Path.Combine(bundles, "config", "two.json"), "{\"two\":true}");
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

        var signature = await client.PostAsJsonAsync("/api/resources/bulk-signature", new ResourceBulkSignatureRequest(
            created.Data.Id,
            "config",
            Kind: ResourceKind.Text,
            Extension: ".json"));
        var payload = await signature.Content.ReadFromJsonAsync<ApiResponse<ResourceBulkSignatureResponse>>();

        Assert.Equal(HttpStatusCode.OK, signature.StatusCode);
        Assert.Equal(2, payload?.Data?.Matched);
        Assert.Equal(2, payload?.Data?.Signed);
        Assert.All(payload?.Data?.Items ?? [], item => Assert.Equal(64, item.Sha256.Length));
    }

    [Fact]
    public async Task Resource_match_compares_signatures_between_profiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var sourceBundles = Path.Combine(root, "source", "Bundles2");
        var targetBundles = Path.Combine(root, "target", "Bundles2");
        Directory.CreateDirectory(Path.Combine(sourceBundles, "config"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "config"));
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "config", "one.json"), "{\"same\":true}");
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "config", "two.json"), "{\"source\":true}");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "config", "one.json"), "{\"same\":true}");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "config", "two.json"), "{\"target\":true}");
        var client = factory.CreateClient();
        var source = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "Source",
            RootPath: Path.Combine(root, "source"),
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: sourceBundles,
            IndexPath: Path.Combine(sourceBundles, "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "source"));
        var target = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "Target",
            RootPath: Path.Combine(root, "target"),
            Platform: ClientPlatform.WeGame,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: targetBundles,
            IndexPath: Path.Combine(targetBundles, "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "target"));
        var sourceProfile = await source.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();
        var targetProfile = await target.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();
        await client.PostAsJsonAsync("/api/index/build", new ResourceIndexBuildRequest(sourceProfile!.Data!.Id));
        await client.PostAsJsonAsync("/api/index/build", new ResourceIndexBuildRequest(targetProfile!.Data!.Id));

        var match = await client.PostAsJsonAsync("/api/resources/match", new ResourceMatchRequest(
            sourceProfile.Data.Id,
            targetProfile.Data.Id,
            "config",
            Kind: ResourceKind.Text,
            Extension: ".json"));
        var payload = await match.Content.ReadFromJsonAsync<ApiResponse<ResourceMatchResponse>>();

        Assert.Equal(HttpStatusCode.OK, match.StatusCode);
        Assert.True(payload?.Data?.Matched >= 2);
        var exact = Assert.Single(payload!.Data!.Items.Where(item => item.SourcePath == "config/one.json"));
        Assert.True(exact.PathMatched);
        Assert.True(exact.HashMatched);
        Assert.Equal(100, exact.Score);
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
        var audit = await client.PostAsJsonAsync("/api/overlay/audit", new OverlayAuditRequest(created.Data.Id));
        var auditPayload = await audit.Content.ReadFromJsonAsync<ApiResponse<OverlayAuditResponse>>();

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        Assert.Equal("text/sample.txt", savePayload?.Data?.VirtualPath);
        Assert.Single(listPayload?.Data?.Items ?? []);
        Assert.True(diffPayload?.Data?.TextChanged);
        Assert.True(revertPayload?.Data?.Removed);
        Assert.Equal(2, auditPayload?.Data?.Total);
        Assert.Equal("revert", auditPayload?.Data?.Items.FirstOrDefault()?.Action);
    }

    [Fact]
    public async Task Batch_overlay_save_updates_text_resources_from_search()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "one.txt"), "base");
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "two.txt"), "base");
        await File.WriteAllBytesAsync(Path.Combine(bundles, "text", "skip.bin"), [1, 2, 3]);
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

        var batch = await client.PostAsJsonAsync("/api/overlay/batch-save-text", new BatchSaveTextOverlayRequest(created.Data.Id, "text", "changed"));
        var batchPayload = await batch.Content.ReadFromJsonAsync<ApiResponse<BatchSaveTextOverlayResponse>>();
        var list = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(created.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();

        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        Assert.Equal(2, batchPayload?.Data?.Matched);
        Assert.Equal(2, batchPayload?.Data?.Saved);
        Assert.Equal(2, listPayload?.Data?.Total);
    }

    [Fact]
    public async Task Batch_replace_text_overlay_replaces_matching_text_resources()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "one.txt"), "hello exile");
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "two.txt"), "hello world");
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

        var batch = await client.PostAsJsonAsync("/api/overlay/batch-replace-text", new BatchReplaceTextOverlayRequest(
            created.Data.Id,
            "text",
            "hello",
            "你好"));
        var batchPayload = await batch.Content.ReadFromJsonAsync<ApiResponse<BatchReplaceTextOverlayResponse>>();
        var preview = await client.PostAsJsonAsync("/api/preview", new ResourcePreviewRequest(created.Data.Id, "text/one.txt"));
        var previewPayload = await preview.Content.ReadFromJsonAsync<ApiResponse<ResourcePreviewResponse>>();

        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        Assert.Equal(2, batchPayload?.Data?.Changed);
        Assert.Equal("hello exile", previewPayload?.Data?.Text);
    }

    [Fact]
    public async Task Binary_resource_export_and_overlay_save_work()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "art", "icons"));
        await File.WriteAllBytesAsync(Path.Combine(bundles, "art", "icons", "item.dds"), [1, 2, 3, 4]);
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

        var export = await client.PostAsJsonAsync("/api/resources/export", new ResourceExportRequest(created.Data.Id, "art/icons/item.dds"));
        var exportPayload = await export.Content.ReadFromJsonAsync<ApiResponse<ResourceExportResponse>>();
        var save = await client.PostAsJsonAsync("/api/overlay/save-binary", new SaveBinaryOverlayRequest(
            created.Data.Id,
            "art/icons/item.dds",
            Convert.ToBase64String([9, 8, 7])));
        var savePayload = await save.Content.ReadFromJsonAsync<ApiResponse<OverlayEntryDto>>();

        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal(Convert.ToBase64String([1, 2, 3, 4]), exportPayload?.Data?.Base64Content);
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal(3, savePayload?.Data?.OverlaySize);
        Assert.Equal([9, 8, 7], await File.ReadAllBytesAsync(savePayload!.Data!.OverlayPath));
    }

    [Fact]
    public async Task Bulk_resource_export_writes_matching_files_to_workspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "one.txt"), "one");
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "two.txt"), "two");
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

        var export = await client.PostAsJsonAsync("/api/resources/bulk-export", new ResourceBulkExportRequest(created.Data.Id, "text", Kind: ResourceKind.Text));
        var payload = await export.Content.ReadFromJsonAsync<ApiResponse<ResourceBulkExportResponse>>();

        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal(2, payload?.Data?.Matched);
        Assert.Equal(2, payload?.Data?.Exported);
        Assert.True(Directory.Exists(payload?.Data?.ExportRoot));
        Assert.All(payload?.Data?.Items ?? [], item => Assert.True(File.Exists(item.ExportPath)));
        Assert.Contains(payload?.Data?.Items ?? [], item => item.VirtualPath == "text/one.txt" && File.ReadAllText(item.ExportPath) == "one");
    }

    [Fact]
    public async Task Bulk_import_overlay_reads_export_workspace_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "one.txt"), "one");
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "two.txt"), "two");
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
        var export = await client.PostAsJsonAsync("/api/resources/bulk-export", new ResourceBulkExportRequest(created.Data.Id, "text", Kind: ResourceKind.Text));
        var exportPayload = await export.Content.ReadFromJsonAsync<ApiResponse<ResourceBulkExportResponse>>();
        var one = Assert.Single(exportPayload!.Data!.Items.Where(item => item.VirtualPath == "text/one.txt"));
        await File.WriteAllTextAsync(one.ExportPath, "changed one");

        var import = await client.PostAsJsonAsync("/api/resources/bulk-import-overlay", new ResourceBulkImportOverlayRequest(created.Data.Id, exportPayload.Data.ExportRoot));
        var importPayload = await import.Content.ReadFromJsonAsync<ApiResponse<ResourceBulkImportOverlayResponse>>();
        var list = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(created.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();

        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        Assert.Equal(2, importPayload?.Data?.Imported);
        Assert.Contains("text/one.txt", importPayload?.Data?.ImportedPaths ?? []);
        Assert.Equal(2, listPayload?.Data?.Total);
        var overlayOne = Assert.Single(listPayload!.Data!.Items.Where(item => item.VirtualPath == "text/one.txt"));
        Assert.Equal("changed one", await File.ReadAllTextAsync(overlayOne.OverlayPath));
    }

    [Fact]
    public async Task Batch_script_preview_and_apply_text_replacements()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "one.txt"), "alpha exile");
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "two.txt"), "beta exile");
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
        var operation = new BatchScriptOperationDto("汉化 exile", "text", "exile", "流放者", ResourceKind.Text, ".txt", Take: 20);

        var preview = await client.PostAsJsonAsync("/api/batch/run-script", new BatchScriptRunRequest(created.Data.Id, [operation], Apply: false));
        var previewPayload = await preview.Content.ReadFromJsonAsync<ApiResponse<BatchScriptRunResponse>>();
        var emptyList = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(created.Data.Id));
        var emptyListPayload = await emptyList.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();
        var apply = await client.PostAsJsonAsync("/api/batch/run-script", new BatchScriptRunRequest(created.Data.Id, [operation], Apply: true));
        var applyPayload = await apply.Content.ReadFromJsonAsync<ApiResponse<BatchScriptRunResponse>>();
        var list = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(created.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.False(previewPayload?.Data?.Applied);
        Assert.Equal(2, previewPayload?.Data?.Changed);
        Assert.Equal(0, emptyListPayload?.Data?.Total);
        Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        Assert.True(applyPayload?.Data?.Applied);
        Assert.Equal(2, applyPayload?.Data?.Changed);
        Assert.Equal(2, listPayload?.Data?.Total);
    }

    [Fact]
    public async Task Table_inspect_returns_structured_preview_for_text_table()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "metadata", "items"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "metadata", "items", "sample.ot"), "Id\tName\r\n1\tSword\r\n2\tShield");
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

        var inspect = await client.PostAsJsonAsync("/api/tables/inspect", new TableInspectRequest(created.Data.Id, "metadata/items/sample.ot"));
        var payload = await inspect.Content.ReadFromJsonAsync<ApiResponse<TableInspectResponse>>();

        Assert.Equal(HttpStatusCode.OK, inspect.StatusCode);
        Assert.True(payload?.Data?.Structured);
        Assert.Equal("\\t", payload?.Data?.Delimiter);
        Assert.Equal(3, payload?.Data?.PreviewRowCount);
        Assert.Equal(["1", "Sword"], payload!.Data!.Rows[1].Cells);
    }

    [Fact]
    public async Task Table_save_cell_edit_writes_overlay_for_structured_text_table()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "metadata", "items"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "metadata", "items", "sample.ot"), "Id\tName\r\n1\tSword\r\n2\tShield");
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

        var save = await client.PostAsJsonAsync("/api/tables/save", new TableSaveRequest(
            created.Data.Id,
            "metadata/items/sample.ot",
            [new TableCellEditDto(RowNumber: 2, ColumnIndex: 1, Value: "长剑")]));
        var payload = await save.Content.ReadFromJsonAsync<ApiResponse<TableSaveResponse>>();
        var list = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(created.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal(1, payload?.Data?.EditedCells);
        var overlay = Assert.Single(listPayload?.Data?.Items ?? []);
        Assert.Equal("metadata/items/sample.ot", overlay.VirtualPath);
        Assert.Contains("1\t长剑", await File.ReadAllTextAsync(overlay.OverlayPath));
    }

    [Fact]
    public async Task Table_inspect_returns_hex_preview_for_binary_dat()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "metadata"));
        await File.WriteAllBytesAsync(Path.Combine(bundles, "metadata", "sample.datc64"), [0, 1, 2, 255]);
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

        var inspect = await client.PostAsJsonAsync("/api/tables/inspect", new TableInspectRequest(created.Data.Id, "metadata/sample.datc64"));
        var payload = await inspect.Content.ReadFromJsonAsync<ApiResponse<TableInspectResponse>>();

        Assert.Equal(HttpStatusCode.OK, inspect.StatusCode);
        Assert.False(payload?.Data?.Structured);
        Assert.Equal("datc64", payload?.Data?.Format);
        Assert.Equal("00 01 02 FF", payload?.Data?.HexPreview);
        Assert.Contains(payload?.Data?.Warnings ?? [], item => item.Contains("二进制", StringComparison.OrdinalIgnoreCase));
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
        Assert.False(string.IsNullOrWhiteSpace(item.DownloadUrl));
        Assert.True(File.Exists(item.ManifestPath));
        Assert.True(item.ZipSize > 0);
    }

    [Fact]
    public async Task Patch_build_zip_can_be_downloaded_by_build_id()
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
        var build = Assert.Single(payload?.Data?.Items ?? []);

        var download = await client.GetAsync(build.DownloadUrl);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);
        Assert.True((await download.Content.ReadAsByteArrayAsync()).Length > 0);
    }

    [Fact]
    public async Task Patch_install_and_uninstall_endpoints_apply_build_files()
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
        await client.PostAsJsonAsync("/api/patch/build", new PatchBuildRequest(created.Data.Id));
        var history = await client.PostAsJsonAsync("/api/patch/build-history", new PatchBuildHistoryRequest(created.Data.Id));
        var historyPayload = await history.Content.ReadFromJsonAsync<ApiResponse<PatchBuildHistoryResponse>>();
        var buildId = Assert.Single(historyPayload?.Data?.Items ?? []).BuildId;

        var preview = await client.PostAsJsonAsync("/api/patch/install", new PatchInstallRequest(created.Data.Id, buildId, Apply: false));
        var previewPayload = await preview.Content.ReadFromJsonAsync<ApiResponse<PatchInstallResponse>>();
        var install = await client.PostAsJsonAsync("/api/patch/install", new PatchInstallRequest(created.Data.Id, buildId, Apply: true));
        var installPayload = await install.Content.ReadFromJsonAsync<ApiResponse<PatchInstallResponse>>();
        var uninstall = await client.PostAsJsonAsync("/api/patch/uninstall", new PatchUninstallRequest(created.Data.Id, buildId, Apply: true));
        var uninstallPayload = await uninstall.Content.ReadFromJsonAsync<ApiResponse<PatchUninstallResponse>>();

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.False(previewPayload?.Data?.Applied);
        Assert.Equal(2, previewPayload?.Data?.FileCount);
        Assert.Equal(HttpStatusCode.OK, install.StatusCode);
        Assert.True(installPayload?.Data?.Applied);
        Assert.True(File.Exists(installPayload?.Data?.InstallManifestPath));
        Assert.Equal(HttpStatusCode.OK, uninstall.StatusCode);
        Assert.True(uninstallPayload?.Data?.Applied);
        Assert.Equal(2, uninstallPayload?.Data?.Removed);
    }

    [Fact]
    public async Task Patch_verify_returns_clear_failure_for_missing_build()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(bundles);
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

        var verify = await client.PostAsJsonAsync("/api/patch/verify", new PatchVerifyRequest(created!.Data!.Id, "404"));
        var payload = await verify.Content.ReadFromJsonAsync<ApiResponse<PatchVerifyResponse>>();

        Assert.Equal(HttpStatusCode.BadRequest, verify.StatusCode);
        Assert.False(payload?.Ok);
        Assert.Equal("build_not_found", payload?.ErrorCode);
    }

    [Fact]
    public async Task Patch_build_native_mode_returns_clear_failure_when_codec_is_missing()
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
        Assert.Equal("native_codec_unavailable", payload?.ErrorCode);
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
