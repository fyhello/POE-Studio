using System.Net;
using System.Net.Http.Json;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
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
            builder.UseSetting("PoeStudio:WorkspaceSettingsPath", Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"), "workspace-settings.json"));
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
    public async Task Workspace_settings_can_switch_profile_storage_root()
    {
        var firstRoot = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"), "first");
        var secondRoot = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"), "second");
        var client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PoeStudio:WorkspaceRoot", firstRoot);
            builder.UseSetting("PoeStudio:WorkspaceSettingsPath", Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"), "workspace-settings.json"));
        }).CreateClient();
        var clientRoot = Path.Combine(Path.GetTempPath(), "poe-studio-client-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(clientRoot);

        var settingsResponse = await client.PostAsJsonAsync("/api/workspace", new WorkspaceSettingsUpdateRequest(secondRoot));
        var settingsPayload = await settingsResponse.Content.ReadFromJsonAsync<ApiResponse<WorkspaceSettingsDto>>();
        var createResponse = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "Moved workspace",
            RootPath: clientRoot,
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: Path.Combine(clientRoot, "Bundles2"),
            IndexPath: Path.Combine(clientRoot, "Bundles2", "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var createPayload = await createResponse.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();
        var diagnosticsPayload = await client.GetFromJsonAsync<ApiResponse<AppDiagnosticsDto>>("/api/diagnostics");

        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);
        Assert.Equal(Path.GetFullPath(secondRoot), settingsPayload?.Data?.WorkspaceRoot);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.True(File.Exists(Path.Combine(secondRoot, "profiles", createPayload!.Data!.Id, "profile.json")));
        Assert.False(Directory.Exists(Path.Combine(firstRoot, "profiles", createPayload.Data.Id)));
        Assert.Equal(Path.GetFullPath(secondRoot), diagnosticsPayload?.Data?.WorkspaceRoot);
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
    public async Task Workbench_home_page_contains_project_workbench_controls()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("项目工作台", html);
        Assert.Contains("workbenchStatus", html);
        Assert.Contains("runWorkbenchPipelineBtn", html);
        Assert.Contains("打开输出", html);
    }

    [Fact]
    public async Task Workbench_home_page_allows_named_client_profiles()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("配置名称", html);
        Assert.Contains("profileNameInput", html);
    }

    [Fact]
    public async Task Workbench_home_page_contains_large_text_chunk_editor_controls()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("largeTextEditor", html);
        Assert.Contains("largeTextTargetEditor", html);
        Assert.Contains("largeTextStatus", html);
        Assert.Contains("largeTextReferenceEditor", html);
        Assert.DoesNotContain("largeTextPrevBtn", html);
        Assert.DoesNotContain("largeTextNextBtn", html);
    }

    [Fact]
    public async Task Workbench_serves_local_codemirror_bundle()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/vendor/codemirror/poe-codemirror.js");
        var script = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("createPoeEditor", script);
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
    public async Task Delete_profile_removes_saved_profile_from_list()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var client = factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "Delete me",
            RootPath: root,
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: Path.Combine(root, "Bundles2"),
            IndexPath: Path.Combine(root, "Bundles2", "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var createPayload = await createResponse.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();

        var deleteResponse = await client.PostAsJsonAsync("/api/profiles/delete", new DeleteProfileRequest(createPayload!.Data!.Id));
        var deletePayload = await deleteResponse.Content.ReadFromJsonAsync<ApiResponse<DeleteProfileResponse>>();
        var listPayload = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<ClientProfileDto>>>("/api/profiles");

        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        Assert.True(deletePayload?.Data?.Removed);
        Assert.DoesNotContain(listPayload?.Data ?? [], profile => profile.Id == createPayload.Data.Id);
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
    public async Task Preview_resource_prefers_overlay_content()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "config"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "config", "sample.json"), "{\"ok\":false}");
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
        await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(
            created.Data.Id,
            "config/sample.json",
            "{\"ok\":true,\"overlay\":true}"));

        var preview = await client.PostAsJsonAsync("/api/preview", new ResourcePreviewRequest(created.Data.Id, "config/sample.json"));
        var payload = await preview.Content.ReadFromJsonAsync<ApiResponse<ResourcePreviewResponse>>();

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(PreviewKind.Text, payload?.Data?.Kind);
        Assert.True(payload?.Data?.FromOverlay);
        Assert.Contains("\"overlay\"", payload?.Data?.Text);
        Assert.DoesNotContain("\"ok\":false", payload?.Data?.Text);
    }

    [Fact]
    public async Task Preview_resource_can_bypass_overlay_content()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "config"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "config", "sample.json"), "{\"ok\":false}");
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
        await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(
            created.Data.Id,
            "config/sample.json",
            "{\"ok\":true,\"overlay\":true}"));

        var preview = await client.PostAsJsonAsync("/api/preview", new ResourcePreviewRequest(
            created.Data.Id,
            "config/sample.json",
            UseOverlay: false));
        var payload = await preview.Content.ReadFromJsonAsync<ApiResponse<ResourcePreviewResponse>>();

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(PreviewKind.Text, payload?.Data?.Kind);
        Assert.False(payload?.Data?.FromOverlay);
        Assert.Contains("\"ok\":false", payload?.Data?.Text);
        Assert.DoesNotContain("\"overlay\"", payload?.Data?.Text);
    }

    [Fact]
    public async Task Text_chunk_reads_large_csd_without_returning_whole_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "data", "statdescriptions"));
        var file = Path.Combine(bundles, "data", "statdescriptions", "large.csd");
        var lines = Enumerable.Range(1, 1200)
            .Select(index => index == 1000 ? "lang \"Traditional Chinese\"" : $"line {index:0000}");
        await File.WriteAllBytesAsync(file, System.Text.Encoding.Unicode.GetPreamble()
            .Concat(System.Text.Encoding.Unicode.GetBytes(string.Join("\r\n", lines)))
            .ToArray());
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

        var response = await client.PostAsJsonAsync("/api/text/chunk", new
        {
            profileId = created.Data.Id,
            virtualPath = "data/statdescriptions/large.csd",
            startLine = 995,
            lineCount = 20
        });
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<TextChunkResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload?.Ok);
        Assert.Equal(995, payload!.Data!.StartLine);
        Assert.Equal(1014, payload.Data.EndLine);
        Assert.Equal(1200, payload.Data.TotalLines);
        Assert.Equal(20, payload.Data.LineCount);
        Assert.True(payload.Data.HasPrevious);
        Assert.True(payload.Data.HasNext);
        Assert.Equal("utf-16le-bom", payload.Data.TextEncoding);
        Assert.Contains("lang \"Traditional Chinese\"", payload.Data.Text);
        Assert.DoesNotContain("line 0001", payload.Data.Text);
        Assert.DoesNotContain("line 1200", payload.Data.Text);
    }

    [Fact]
    public async Task Text_chunk_save_replaces_only_selected_lines_and_preserves_encoding()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "data", "statdescriptions"));
        var file = Path.Combine(bundles, "data", "statdescriptions", "large.csd");
        var original = string.Join("\r\n", Enumerable.Range(1, 20).Select(index => $"line {index:0000}"));
        await File.WriteAllBytesAsync(file, System.Text.Encoding.Unicode.GetPreamble()
            .Concat(System.Text.Encoding.Unicode.GetBytes(original))
            .ToArray());
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

        var save = await client.PostAsJsonAsync("/api/text/chunk/save", new
        {
            profileId = created.Data.Id,
            virtualPath = "data/statdescriptions/large.csd",
            startLine = 10,
            originalLineCount = 3,
            text = "line 0010 edited\r\ninserted translation"
        });
        var savePayload = await save.Content.ReadFromJsonAsync<ApiResponse<TextChunkSaveResponse>>();

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.True(savePayload?.Ok);
        Assert.Equal("utf-16le-bom", savePayload!.Data!.TextEncoding);
        var overlayBytes = await File.ReadAllBytesAsync(savePayload.Data.Overlay.OverlayPath);
        Assert.Equal([0xff, 0xfe], overlayBytes.Take(2).ToArray());
        var overlayText = System.Text.Encoding.Unicode.GetString(overlayBytes.Skip(2).ToArray());
        Assert.StartsWith("line 0001\r\nline 0002", overlayText);
        Assert.Contains("line 0010 edited\r\ninserted translation\r\nline 0013", overlayText);
        Assert.EndsWith("line 0020", overlayText);
        Assert.DoesNotContain("line 0011\r\nline 0012", overlayText);
    }

    [Fact]
    public async Task Preview_resource_returns_format_inspection_for_atlas()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "ui"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "ui", "sprites.atlas"), """
        atlas.png
        size: 256,128
        icon_a
          xy: 0,0
          size: 32,32
        """);
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

        var preview = await client.PostAsJsonAsync("/api/preview", new ResourcePreviewRequest(created.Data.Id, "ui/sprites.atlas"));
        var payload = await preview.Content.ReadFromJsonAsync<ApiResponse<ResourcePreviewResponse>>();

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("atlas", payload?.Data?.Inspection?.Format);
        Assert.Equal("1", payload?.Data?.Inspection?.Properties["regions"]);
        Assert.Equal("256x128", payload?.Data?.Inspection?.Properties["size"]);
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
    public async Task Single_export_and_signature_prefer_overlay_content()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "config"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "config", "sample.json"), "{\"base\":true}");
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
        await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(
            created.Data.Id,
            "config/sample.json",
            "{\"overlay\":true}"));

        var export = await client.PostAsJsonAsync("/api/resources/export", new ResourceExportRequest(
            created.Data.Id,
            "config/sample.json"));
        var exportPayload = await export.Content.ReadFromJsonAsync<ApiResponse<ResourceExportResponse>>();
        var signature = await client.PostAsJsonAsync("/api/resources/signature", new ResourceSignatureRequest(
            created.Data.Id,
            "config/sample.json"));
        var signaturePayload = await signature.Content.ReadFromJsonAsync<ApiResponse<ResourceSignatureResponse>>();

        var exportedText = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(exportPayload!.Data!.Base64Content));
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Contains("\"overlay\"", exportedText);
        Assert.DoesNotContain("\"base\"", exportedText);
        Assert.Equal(HttpStatusCode.OK, signature.StatusCode);
        Assert.Equal(16, signaturePayload?.Data?.Size);
        Assert.Contains("7B 22 6F 76", signaturePayload?.Data?.HeaderHex);
    }

    [Fact]
    public async Task Single_export_and_signature_can_bypass_overlay_content()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "config"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "config", "sample.json"), "{\"base\":true}");
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
        await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(
            created.Data.Id,
            "config/sample.json",
            "{\"overlay\":true}"));

        var export = await client.PostAsJsonAsync("/api/resources/export", new ResourceExportRequest(
            created.Data.Id,
            "config/sample.json",
            UseOverlay: false));
        var exportPayload = await export.Content.ReadFromJsonAsync<ApiResponse<ResourceExportResponse>>();
        var signature = await client.PostAsJsonAsync("/api/resources/signature", new ResourceSignatureRequest(
            created.Data.Id,
            "config/sample.json",
            UseOverlay: false));
        var signaturePayload = await signature.Content.ReadFromJsonAsync<ApiResponse<ResourceSignatureResponse>>();
        var exportedText = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(exportPayload!.Data!.Base64Content));

        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Contains("\"base\"", exportedText);
        Assert.DoesNotContain("\"overlay\"", exportedText);
        Assert.Equal(HttpStatusCode.OK, signature.StatusCode);
        Assert.Equal(13, signaturePayload?.Data?.Size);
        Assert.StartsWith("7B 22 62 61", signaturePayload?.Data?.HeaderHex);
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
    public async Task Bulk_export_and_signature_prefer_overlay_content()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "config"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "config", "one.json"), "{\"base\":1}");
        await File.WriteAllTextAsync(Path.Combine(bundles, "config", "two.json"), "{\"base\":2}");
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
        await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(
            created.Data.Id,
            "config/one.json",
            "{\"overlay\":1}"));

        var export = await client.PostAsJsonAsync("/api/resources/bulk-export", new ResourceBulkExportRequest(
            created.Data.Id,
            "config",
            Take: 20));
        var exportPayload = await export.Content.ReadFromJsonAsync<ApiResponse<ResourceBulkExportResponse>>();
        var signature = await client.PostAsJsonAsync("/api/resources/bulk-signature", new ResourceBulkSignatureRequest(
            created.Data.Id,
            "config",
            Take: 20));
        var signaturePayload = await signature.Content.ReadFromJsonAsync<ApiResponse<ResourceBulkSignatureResponse>>();
        var exportedOne = await File.ReadAllTextAsync(Path.Combine(exportPayload!.Data!.ExportRoot, "config", "one.json"));
        var signedOne = signaturePayload!.Data!.Items.Single(item => item.VirtualPath == "config/one.json");

        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Contains("\"overlay\"", exportedOne);
        Assert.DoesNotContain("\"base\"", exportedOne);
        Assert.Equal(HttpStatusCode.OK, signature.StatusCode);
        Assert.Contains("7B 22 6F 76", signedOne.HeaderHex);
    }

    [Fact]
    public async Task Bulk_export_and_signature_can_bypass_overlay_content()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "config"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "config", "one.json"), "{\"base\":1}");
        await File.WriteAllTextAsync(Path.Combine(bundles, "config", "two.json"), "{\"base\":2}");
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
        await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(
            created.Data.Id,
            "config/one.json",
            "{\"overlay\":1}"));

        var export = await client.PostAsJsonAsync("/api/resources/bulk-export", new ResourceBulkExportRequest(
            created.Data.Id,
            "config",
            Take: 20,
            UseOverlay: false));
        var exportPayload = await export.Content.ReadFromJsonAsync<ApiResponse<ResourceBulkExportResponse>>();
        var signature = await client.PostAsJsonAsync("/api/resources/bulk-signature", new ResourceBulkSignatureRequest(
            created.Data.Id,
            "config",
            Take: 20,
            UseOverlay: false));
        var signaturePayload = await signature.Content.ReadFromJsonAsync<ApiResponse<ResourceBulkSignatureResponse>>();
        var exportedOne = await File.ReadAllTextAsync(Path.Combine(exportPayload!.Data!.ExportRoot, "config", "one.json"));
        var signedOne = signaturePayload!.Data!.Items.Single(item => item.VirtualPath == "config/one.json");

        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Contains("\"base\"", exportedOne);
        Assert.DoesNotContain("\"overlay\"", exportedOne);
        Assert.Equal(HttpStatusCode.OK, signature.StatusCode);
        Assert.StartsWith("7B 22 62 61", signedOne.HeaderHex);
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
    public async Task Resource_match_can_bypass_overlay_content()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var sourceBundles = Path.Combine(root, "source", "Bundles2");
        var targetBundles = Path.Combine(root, "target", "Bundles2");
        Directory.CreateDirectory(Path.Combine(sourceBundles, "config"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "config"));
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "config", "one.json"), "{\"base\":true}");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "config", "one.json"), "{\"base\":true}");
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
        await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(
            sourceProfile.Data.Id,
            "config/one.json",
            "{\"overlay\":true}"));

        var overlayMatch = await client.PostAsJsonAsync("/api/resources/match", new ResourceMatchRequest(
            sourceProfile.Data.Id,
            targetProfile!.Data!.Id,
            "config",
            UseOverlay: true));
        var overlayPayload = await overlayMatch.Content.ReadFromJsonAsync<ApiResponse<ResourceMatchResponse>>();
        var baseMatch = await client.PostAsJsonAsync("/api/resources/match", new ResourceMatchRequest(
            sourceProfile.Data.Id,
            targetProfile.Data.Id,
            "config",
            UseOverlay: false));
        var basePayload = await baseMatch.Content.ReadFromJsonAsync<ApiResponse<ResourceMatchResponse>>();

        Assert.Equal(HttpStatusCode.OK, overlayMatch.StatusCode);
        Assert.False(overlayPayload!.Data!.Items.Single().HashMatched);
        Assert.Equal(HttpStatusCode.OK, baseMatch.StatusCode);
        Assert.True(basePayload!.Data!.Items.Single().HashMatched);
    }

    [Fact]
    public async Task Resource_migration_plan_classifies_direct_hash_candidate_and_missing_items()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var sourceBundles = Path.Combine(root, "source", "Bundles2");
        var targetBundles = Path.Combine(root, "target", "Bundles2");
        Directory.CreateDirectory(Path.Combine(sourceBundles, "text"));
        Directory.CreateDirectory(Path.Combine(sourceBundles, "ui"));
        Directory.CreateDirectory(Path.Combine(sourceBundles, "metadata"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "text"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "ui-renamed"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "metadata"));
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "text", "same.txt"), "same");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "text", "same.txt"), "same");
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "ui", "panel.ui"), "panel");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "ui-renamed", "panel.ui"), "panel");
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "metadata", "items.dat"), "abc");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "metadata", "items.dat"), "xyz");
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "text", "missing.txt"), "missing");
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

        var plan = await client.PostAsJsonAsync("/api/resources/migration-plan", new ResourceMigrationPlanRequest(
            sourceProfile.Data.Id,
            targetProfile.Data.Id,
            Query: ""));
        var payload = await plan.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanResponse>>();

        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);
        Assert.Equal(4, payload?.Data?.SourceMatched);
        Assert.Equal(4, payload?.Data?.Planned);
        Assert.Contains(payload!.Data!.Items, item => item.SourcePath == "text/same.txt" && item.Status == ResourceMigrationStatus.Direct && item.RiskLevel == PatchRiskLevel.Low);
        Assert.Contains(payload.Data.Items, item => item.SourcePath == "ui/panel.ui" && item.TargetPath == "ui-renamed/panel.ui" && item.Status == ResourceMigrationStatus.HashMatch);
        Assert.Contains(payload.Data.Items, item => item.SourcePath == "metadata/items.dat" && item.Status == ResourceMigrationStatus.Candidate && item.RiskLevel == PatchRiskLevel.Medium);
        Assert.Contains(payload.Data.Items, item => item.SourcePath == "text/missing.txt" && item.Status == ResourceMigrationStatus.Missing);
    }

    [Fact]
    public async Task Resource_migration_plan_can_use_overlay_layer()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var sourceBundles = Path.Combine(root, "source", "Bundles2");
        var targetBundles = Path.Combine(root, "target", "Bundles2");
        Directory.CreateDirectory(Path.Combine(sourceBundles, "text"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "text", "sample.txt"), "base");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "text", "sample.txt"), "overlay");
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
        await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(
            sourceProfile.Data.Id,
            "text/sample.txt",
            "overlay"));

        var overlayPlan = await client.PostAsJsonAsync("/api/resources/migration-plan", new ResourceMigrationPlanRequest(
            sourceProfile.Data.Id,
            targetProfile.Data.Id,
            Query: "text",
            UseOverlay: true));
        var overlayPayload = await overlayPlan.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanResponse>>();
        var basePlan = await client.PostAsJsonAsync("/api/resources/migration-plan", new ResourceMigrationPlanRequest(
            sourceProfile.Data.Id,
            targetProfile.Data.Id,
            Query: "text",
            UseOverlay: false));
        var basePayload = await basePlan.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanResponse>>();

        Assert.Equal(ResourceMigrationStatus.Direct, overlayPayload!.Data!.Items.Single().Status);
        Assert.Equal(ResourceMigrationStatus.Candidate, basePayload!.Data!.Items.Single().Status);
    }

    [Fact]
    public async Task Resource_migration_draft_writes_safe_direct_and_hash_matches_to_target_overlay()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var sourceBundles = Path.Combine(root, "source", "Bundles2");
        var targetBundles = Path.Combine(root, "target", "Bundles2");
        Directory.CreateDirectory(Path.Combine(sourceBundles, "text"));
        Directory.CreateDirectory(Path.Combine(sourceBundles, "ui"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "text"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "renamed"));
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "text", "same.txt"), "same");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "text", "same.txt"), "same");
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "ui", "panel.txt"), "panel-source");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "renamed", "panel.txt"), "panel-source");
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
        var draft = await client.PostAsJsonAsync("/api/resources/migration-draft", new ResourceMigrationDraftRequest(
            sourceProfile.Data.Id,
            targetProfile.Data.Id,
            Query: ""));
        var payload = await draft.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationDraftResponse>>();
        var list = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(targetProfile.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();

        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        Assert.Equal(2, payload?.Data?.Drafted);
        Assert.Contains(payload!.Data!.Items, item => item.SourcePath == "text/same.txt" && item.TargetPath == "text/same.txt" && item.Status == ResourceMigrationStatus.Direct);
        Assert.Contains(payload.Data.Items, item => item.SourcePath == "ui/panel.txt" && item.TargetPath == "renamed/panel.txt" && item.Status == ResourceMigrationStatus.HashMatch);
        var sameOverlay = Assert.Single(listPayload!.Data!.Items, item => item.VirtualPath == "text/same.txt");
        var renamedOverlay = Assert.Single(listPayload.Data.Items, item => item.VirtualPath == "renamed/panel.txt");
        Assert.Equal("same", await File.ReadAllTextAsync(sameOverlay.OverlayPath));
        Assert.Equal("panel-source", await File.ReadAllTextAsync(renamedOverlay.OverlayPath));
    }

    [Fact]
    public async Task Resource_migration_draft_skips_candidates_high_risk_and_missing_by_default()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var sourceBundles = Path.Combine(root, "source", "Bundles2");
        var targetBundles = Path.Combine(root, "target", "Bundles2");
        Directory.CreateDirectory(Path.Combine(sourceBundles, "text"));
        Directory.CreateDirectory(Path.Combine(sourceBundles, "metadata"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "text"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "metadata"));
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "text", "same.txt"), "same");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "text", "same.txt"), "same");
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "metadata", "items.dat"), "abc");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "metadata", "items.dat"), "xyz");
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "metadata", "effect.mat"), "high");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "metadata", "effect.mat"), "changed");
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "text", "missing.txt"), "missing");
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

        var draft = await client.PostAsJsonAsync("/api/resources/migration-draft", new ResourceMigrationDraftRequest(
            sourceProfile.Data.Id,
            targetProfile.Data.Id,
            Query: ""));
        var payload = await draft.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationDraftResponse>>();
        var list = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(targetProfile.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();

        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        Assert.Equal(1, payload?.Data?.Drafted);
        Assert.Equal(3, payload?.Data?.Skipped);
        Assert.Contains(payload!.Data!.SkippedItems, item => item.SourcePath == "metadata/items.dat" && item.Status == ResourceMigrationStatus.Candidate);
        Assert.Contains(payload.Data.SkippedItems, item => item.SourcePath == "metadata/effect.mat" && item.RiskLevel == PatchRiskLevel.High);
        Assert.Contains(payload.Data.SkippedItems, item => item.SourcePath == "text/missing.txt" && item.Status == ResourceMigrationStatus.Missing);
        Assert.Single(listPayload!.Data!.Items);
        Assert.Equal("text/same.txt", listPayload.Data.Items.Single().VirtualPath);
    }

    [Fact]
    public async Task Resource_migration_draft_job_runs_without_blocking_request()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var sourceBundles = Path.Combine(root, "source", "Bundles2");
        var targetBundles = Path.Combine(root, "target", "Bundles2");
        Directory.CreateDirectory(Path.Combine(sourceBundles, "text"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "text", "same.txt"), "same");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "text", "same.txt"), "same");
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

        var start = await client.PostAsJsonAsync("/api/jobs/resources/migration-draft", new ResourceMigrationDraftRequest(
            sourceProfile.Data.Id,
            targetProfile.Data.Id,
            Query: "text"));
        var job = await start.Content.ReadFromJsonAsync<ApiResponse<JobSnapshotDto>>();
        var snapshot = await WaitJobAsync(client, job!.Data!.Id);
        var result = System.Text.Json.JsonSerializer.Deserialize<ResourceMigrationDraftResponse>(
            snapshot.ResultJson!,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var list = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(targetProfile.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("resources-migration-draft", job.Data.Kind);
        Assert.Equal(JobStatus.Succeeded, snapshot.Status);
        Assert.Equal(1, result?.Drafted);
        Assert.Contains("drafted", snapshot.ResultJson, StringComparison.OrdinalIgnoreCase);
        Assert.Single(listPayload!.Data!.Items);
    }

    [Fact]
    public async Task Resource_migration_apply_item_writes_confirmed_candidate_to_target_overlay()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var sourceBundles = Path.Combine(root, "source", "Bundles2");
        var targetBundles = Path.Combine(root, "target", "Bundles2");
        Directory.CreateDirectory(Path.Combine(sourceBundles, "metadata"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "metadata"));
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "metadata", "items.dat"), "candidate-source");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "metadata", "items.dat"), "candidate-target");
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

        var apply = await client.PostAsJsonAsync("/api/resources/migration-apply-item", new ResourceMigrationApplyItemRequest(
            sourceProfile.Data.Id,
            targetProfile.Data.Id,
            "metadata/items.dat",
            "metadata/items.dat"));
        var payload = await apply.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationApplyItemResponse>>();
        var list = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(targetProfile.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();

        Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        Assert.Equal(PatchRiskLevel.Medium, payload?.Data?.RiskLevel);
        var overlay = Assert.Single(listPayload!.Data!.Items);
        Assert.Equal("metadata/items.dat", overlay.VirtualPath);
        Assert.Equal("candidate-source", await File.ReadAllTextAsync(overlay.OverlayPath));
    }

    [Fact]
    public async Task Resource_migration_plan_can_be_saved_loaded_listed_and_deleted()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var sourceBundles = Path.Combine(root, "source", "Bundles2");
        var targetBundles = Path.Combine(root, "target", "Bundles2");
        Directory.CreateDirectory(Path.Combine(sourceBundles, "text"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "text", "same.txt"), "same");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "text", "same.txt"), "same");
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
        var plan = await client.PostAsJsonAsync("/api/resources/migration-plan", new ResourceMigrationPlanRequest(
            sourceProfile.Data.Id,
            targetProfile.Data.Id,
            Query: "text"));
        var planPayload = await plan.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanResponse>>();

        var save = await client.PostAsJsonAsync("/api/resources/migration-plans/save", new ResourceMigrationPlanSaveRequest(
            null,
            "文本同步",
            new ResourceMigrationPlanCriteriaDto(
                sourceProfile.Data.Id,
                targetProfile.Data.Id,
                "text",
                null,
                null,
                200,
                null,
                null,
                true),
            planPayload!.Data!.Items));
        var savePayload = await save.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanEntryDto>>();
        var list = await client.PostAsJsonAsync("/api/resources/migration-plans/list", new ResourceMigrationPlanListRequest(
            sourceProfile.Data.Id,
            targetProfile.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanListResponse>>();
        var load = await client.PostAsJsonAsync("/api/resources/migration-plans/load", new ResourceMigrationPlanLoadRequest(
            sourceProfile.Data.Id,
            savePayload!.Data!.Id));
        var loadPayload = await load.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanEntryDto>>();
        var delete = await client.PostAsJsonAsync("/api/resources/migration-plans/delete", new ResourceMigrationPlanDeleteRequest(
            sourceProfile.Data.Id,
            savePayload.Data.Id));
        var deletePayload = await delete.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanDeleteResponse>>();

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal("文本同步", savePayload.Data.Name);
        Assert.Single(listPayload!.Data!.Items);
        Assert.Equal(savePayload.Data.Id, listPayload.Data.Items.Single().Id);
        Assert.Equal("text/same.txt", loadPayload!.Data!.Items.Single().SourcePath);
        Assert.True(deletePayload!.Data!.Removed);
    }

    [Fact]
    public async Task Resource_migration_saved_plan_apply_writes_overlay_and_skips_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var sourceBundles = Path.Combine(root, "source", "Bundles2");
        var targetBundles = Path.Combine(root, "target", "Bundles2");
        Directory.CreateDirectory(Path.Combine(sourceBundles, "text"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "text", "same.txt"), "same");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "text", "same.txt"), "same");
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
        var items = new[]
        {
            new ResourceMigrationPlanItemDto(
                "text/same.txt",
                "text/same.txt",
                ResourceMigrationStatus.Direct,
                PatchRiskLevel.Low,
                ResourceKind.Text,
                ".txt",
                100,
                true,
                true,
                true,
                "sourcehash",
                "targethash",
                4,
                4,
                ["路径一致。"]),
            new ResourceMigrationPlanItemDto(
                "text/missing.txt",
                "text/missing.txt",
                ResourceMigrationStatus.Direct,
                PatchRiskLevel.Low,
                ResourceKind.Text,
                ".txt",
                100,
                true,
                true,
                true,
                "missinghash",
                "targethash",
                7,
                7,
                ["用于验证缺失跳过。"])
        };
        var save = await client.PostAsJsonAsync("/api/resources/migration-plans/save", new ResourceMigrationPlanSaveRequest(
            null,
            "可复用迁移",
            new ResourceMigrationPlanCriteriaDto(
                sourceProfile.Data.Id,
                targetProfile.Data.Id,
                "text",
                null,
                null,
                200,
                null,
                null,
                true),
            items));
        var savePayload = await save.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanEntryDto>>();

        var apply = await client.PostAsJsonAsync("/api/resources/migration-plans/apply", new ResourceMigrationPlanApplyRequest(
            sourceProfile.Data.Id,
            savePayload!.Data!.Id,
            IncludeHashMatches: true,
            IncludeCandidates: false,
            MaxRiskLevel: PatchRiskLevel.Low));
        var applyPayload = await apply.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationDraftResponse>>();
        var list = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(targetProfile.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();

        Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        Assert.Equal(1, applyPayload?.Data?.Drafted);
        Assert.Equal(1, applyPayload?.Data?.Skipped);
        Assert.Contains(applyPayload!.Data!.SkippedItems, item => item.SourcePath == "text/missing.txt" && item.Reason == "源资源未找到。");
        var overlay = Assert.Single(listPayload!.Data!.Items);
        Assert.Equal("text/same.txt", overlay.VirtualPath);
        Assert.Equal("same", await File.ReadAllTextAsync(overlay.OverlayPath));
    }

    [Fact]
    public async Task Resource_migration_saved_plan_apply_job_runs_without_blocking_request()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var sourceBundles = Path.Combine(root, "source", "Bundles2");
        var targetBundles = Path.Combine(root, "target", "Bundles2");
        Directory.CreateDirectory(Path.Combine(sourceBundles, "text"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "text", "same.txt"), "same");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "text", "same.txt"), "same");
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
        var plan = await client.PostAsJsonAsync("/api/resources/migration-plan", new ResourceMigrationPlanRequest(
            sourceProfile.Data.Id,
            targetProfile.Data.Id,
            Query: "text"));
        var planPayload = await plan.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanResponse>>();
        var save = await client.PostAsJsonAsync("/api/resources/migration-plans/save", new ResourceMigrationPlanSaveRequest(
            null,
            "后台方案",
            new ResourceMigrationPlanCriteriaDto(sourceProfile.Data.Id, targetProfile.Data.Id, "text"),
            planPayload!.Data!.Items));
        var savePayload = await save.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanEntryDto>>();

        var start = await client.PostAsJsonAsync("/api/jobs/resources/migration-plan-apply", new ResourceMigrationPlanApplyRequest(
            sourceProfile.Data.Id,
            savePayload!.Data!.Id));
        var job = await start.Content.ReadFromJsonAsync<ApiResponse<JobSnapshotDto>>();
        var snapshot = await WaitJobAsync(client, job!.Data!.Id);
        var result = System.Text.Json.JsonSerializer.Deserialize<ResourceMigrationDraftResponse>(
            snapshot.ResultJson!,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("resources-migration-plan-apply", job.Data.Kind);
        Assert.Equal(JobStatus.Succeeded, snapshot.Status);
        Assert.Equal(1, result?.Drafted);
    }

    [Fact]
    public async Task Resource_migration_saved_plan_validate_reports_ready_changed_and_missing_items()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var sourceBundles = Path.Combine(root, "source", "Bundles2");
        var targetBundles = Path.Combine(root, "target", "Bundles2");
        Directory.CreateDirectory(Path.Combine(sourceBundles, "text"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "text", "ready.txt"), "ready");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "text", "ready.txt"), "ready");
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "text", "changed.txt"), "before");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "text", "changed.txt"), "before");
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "text", "missing.txt"), "missing");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "text", "missing.txt"), "missing");
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
        var plan = await client.PostAsJsonAsync("/api/resources/migration-plan", new ResourceMigrationPlanRequest(
            sourceProfile.Data.Id,
            targetProfile.Data.Id,
            Query: "text"));
        var planPayload = await plan.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanResponse>>();
        var save = await client.PostAsJsonAsync("/api/resources/migration-plans/save", new ResourceMigrationPlanSaveRequest(
            null,
            "校验方案",
            new ResourceMigrationPlanCriteriaDto(sourceProfile.Data.Id, targetProfile.Data.Id, "text"),
            planPayload!.Data!.Items));
        var savePayload = await save.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanEntryDto>>();
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "text", "changed.txt"), "after");
        File.Delete(Path.Combine(sourceBundles, "text", "missing.txt"));

        var validate = await client.PostAsJsonAsync("/api/resources/migration-plans/validate", new ResourceMigrationPlanValidateRequest(
            sourceProfile.Data.Id,
            savePayload!.Data!.Id));
        var payload = await validate.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanValidateResponse>>();

        Assert.Equal(HttpStatusCode.OK, validate.StatusCode);
        Assert.Equal(3, payload?.Data?.Checked);
        Assert.Equal(1, payload?.Data?.Ready);
        Assert.Equal(1, payload?.Data?.Changed);
        Assert.Equal(1, payload?.Data?.Missing);
        Assert.Contains(payload!.Data!.Items, item => item.SourcePath == "text/ready.txt" && item.State == ResourceMigrationPlanValidationState.Ready);
        Assert.Contains(payload.Data.Items, item => item.SourcePath == "text/changed.txt" && item.State == ResourceMigrationPlanValidationState.Changed);
        Assert.Contains(payload.Data.Items, item => item.SourcePath == "text/missing.txt" && item.State == ResourceMigrationPlanValidationState.Missing);
    }

    [Fact]
    public async Task Resource_migration_saved_plan_apply_can_confirm_candidates_with_medium_risk()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var sourceBundles = Path.Combine(root, "source", "Bundles2");
        var targetBundles = Path.Combine(root, "target", "Bundles2");
        Directory.CreateDirectory(Path.Combine(sourceBundles, "metadata"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "metadata"));
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "metadata", "items.dat"), "candidate-source");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "metadata", "items.dat"), "candidate-target");
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
        var plan = await client.PostAsJsonAsync("/api/resources/migration-plan", new ResourceMigrationPlanRequest(
            sourceProfile.Data.Id,
            targetProfile.Data.Id,
            Query: "metadata"));
        var planPayload = await plan.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanResponse>>();
        var save = await client.PostAsJsonAsync("/api/resources/migration-plans/save", new ResourceMigrationPlanSaveRequest(
            null,
            "候选确认",
            new ResourceMigrationPlanCriteriaDto(sourceProfile.Data.Id, targetProfile.Data.Id, "metadata"),
            planPayload!.Data!.Items));
        var savePayload = await save.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanEntryDto>>();

        var apply = await client.PostAsJsonAsync("/api/resources/migration-plans/apply", new ResourceMigrationPlanApplyRequest(
            sourceProfile.Data.Id,
            savePayload!.Data!.Id,
            IncludeHashMatches: true,
            IncludeCandidates: true,
            MaxRiskLevel: PatchRiskLevel.Medium));
        var payload = await apply.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationDraftResponse>>();
        var list = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(targetProfile.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();

        Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        Assert.Equal(1, payload?.Data?.Drafted);
        Assert.Equal(0, payload?.Data?.Skipped);
        var overlay = Assert.Single(listPayload!.Data!.Items);
        Assert.Equal("metadata/items.dat", overlay.VirtualPath);
        Assert.Equal("candidate-source", await File.ReadAllTextAsync(overlay.OverlayPath));
    }

    [Fact]
    public async Task Resource_format_scan_reports_capability_by_extension()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        Directory.CreateDirectory(Path.Combine(bundles, "data"));
        Directory.CreateDirectory(Path.Combine(bundles, "ui"));
        Directory.CreateDirectory(Path.Combine(bundles, "textures"));
        Directory.CreateDirectory(Path.Combine(bundles, "materials"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "a.txt"), "hello");
        await File.WriteAllTextAsync(Path.Combine(bundles, "data", "base.dat"), "rows");
        await File.WriteAllTextAsync(Path.Combine(bundles, "ui", "panel.ui"), "ui");
        await File.WriteAllBytesAsync(Path.Combine(bundles, "textures", "icon.dds"), [0x44, 0x44, 0x53, 0x20, 1, 2, 3, 4]);
        await File.WriteAllTextAsync(Path.Combine(bundles, "materials", "fire.mat"), "mat");
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
        await client.PostAsJsonAsync("/api/index/build", new ResourceIndexBuildRequest(created!.Data!.Id));

        var scan = await client.PostAsJsonAsync("/api/resources/format-scan", new ResourceFormatScanRequest(created.Data.Id));
        var payload = await scan.Content.ReadFromJsonAsync<ApiResponse<ResourceFormatScanResponse>>();

        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
        Assert.Equal(5, payload?.Data?.Scanned);
        Assert.Equal(5, payload?.Data?.ExtensionCount);
        Assert.Contains(payload!.Data!.Items, item => item.Extension == ".txt" && item.Previewable == 1 && item.Editable == 1);
        Assert.Contains(payload.Data.Items, item => item.Extension == ".dat" && item.Previewable == 1 && item.Editable == 1);
        Assert.Contains(payload.Data.Items, item => item.Extension == ".dds" && item.Previewable == 1 && item.Editable == 0);
        Assert.Contains(payload.Data.Items, item => item.Extension == ".mat" && item.ExportOnly == 1);
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
    public async Task Overlay_review_endpoint_returns_changed_text_preview()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        var basePath = Path.Combine(bundles, "text", "sample.txt");
        await File.WriteAllTextAsync(basePath, "Hello exile");
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
        await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(created.Data.Id, "text/sample.txt", "Hello 流放者", basePath, true));

        var review = await client.PostAsJsonAsync("/api/overlay/review", new OverlayReviewRequest(created.Data.Id));
        var payload = await review.Content.ReadFromJsonAsync<ApiResponse<OverlayReviewResponse>>();

        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var item = Assert.Single(payload?.Data?.Items ?? []);
        Assert.Contains("Hello exile", item.BasePreview);
        Assert.Contains("Hello 流放者", item.OverlayPreview);
        Assert.True(item.TextChanged);
    }

    [Fact]
    public async Task Overlay_bulk_revert_can_remove_only_high_risk_changes()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "metadata", "effects"));
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "metadata", "effects", "fire.mat"), "base");
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
        await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(created.Data.Id, "metadata/effects/fire.mat", "risky"));
        await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(created.Data.Id, "text/sample.txt", "safe"));

        var revert = await client.PostAsJsonAsync("/api/overlay/bulk-revert", new OverlayBulkRevertRequest(created.Data.Id, PatchRiskLevel.High));
        var revertPayload = await revert.Content.ReadFromJsonAsync<ApiResponse<OverlayBulkRevertResponse>>();
        var list = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(created.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();

        Assert.Equal(HttpStatusCode.OK, revert.StatusCode);
        Assert.Equal(1, revertPayload?.Data?.Removed);
        var remaining = Assert.Single(listPayload?.Data?.Items ?? []);
        Assert.Equal("text/sample.txt", remaining.VirtualPath);
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
        Assert.True(previewPayload?.Data?.FromOverlay);
        Assert.Contains("你好 exile", previewPayload?.Data?.Text);
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
    public async Task Overlay_save_file_accepts_multipart_without_base64_json()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "data", "statdescriptions"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "data", "statdescriptions", "stat_descriptions.csd"), "old");
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

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(created.Data.Id), "profileId");
        form.Add(new StringContent("data/statdescriptions/stat_descriptions.csd"), "virtualPath");
        form.Add(new ByteArrayContent([0xff, 0xfe, 0x6e, 0x00, 0x65, 0x00, 0x77, 0x00]), "file", "stat_descriptions.csd");
        var save = await client.PostAsync("/api/overlay/save-file", form);
        var savePayload = await save.Content.ReadFromJsonAsync<ApiResponse<OverlayEntryDto>>();

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal(8, savePayload?.Data?.OverlaySize);
        Assert.Equal([0xff, 0xfe, 0x6e, 0x00, 0x65, 0x00, 0x77, 0x00], await File.ReadAllBytesAsync(savePayload!.Data!.OverlayPath));
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
    public async Task Batch_script_template_can_be_saved_listed_loaded_run_and_deleted()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "sample.txt"), "Hello exile");
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

        var save = await client.PostAsJsonAsync("/api/batch/templates/save", new BatchScriptTemplateSaveRequest(
            created.Data.Id,
            "常用汉化",
            [operation]));
        var savePayload = await save.Content.ReadFromJsonAsync<ApiResponse<BatchScriptTemplateDto>>();
        var list = await client.PostAsJsonAsync("/api/batch/templates/list", new BatchScriptTemplateListRequest(created.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<BatchScriptTemplateListResponse>>();
        var run = await client.PostAsJsonAsync("/api/batch/run-template", new BatchScriptTemplateRunRequest(
            created.Data.Id,
            savePayload!.Data!.Id,
            Apply: false));
        var runPayload = await run.Content.ReadFromJsonAsync<ApiResponse<BatchScriptRunResponse>>();
        var delete = await client.PostAsJsonAsync("/api/batch/templates/delete", new BatchScriptTemplateDeleteRequest(
            created.Data.Id,
            savePayload.Data.Id));
        var deletePayload = await delete.Content.ReadFromJsonAsync<ApiResponse<BatchScriptTemplateDeleteResponse>>();

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal("常用汉化", savePayload.Data.Name);
        Assert.Single(savePayload.Data.Operations);
        Assert.Single(listPayload!.Data!.Items);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        Assert.Equal(1, runPayload!.Data!.Changed);
        Assert.Equal("text/sample.txt", runPayload.Data.Changes[0].VirtualPath);
        Assert.True(deletePayload!.Data!.Removed);
    }

    [Fact]
    public async Task Batch_script_can_apply_on_existing_overlay_layer()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "sample.txt"), "Hello exile");
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
        var first = new BatchScriptOperationDto("step1", "text", "exile", "流放者", ResourceKind.Text, ".txt", Take: 20);
        var second = new BatchScriptOperationDto("step2", "text", "Hello", "你好", ResourceKind.Text, ".txt", Take: 20);

        var firstApply = await client.PostAsJsonAsync("/api/batch/run-script", new BatchScriptRunRequest(
            created.Data.Id,
            [first],
            Apply: true));
        var secondApply = await client.PostAsJsonAsync("/api/batch/run-script", new BatchScriptRunRequest(
            created.Data.Id,
            [second],
            Apply: true,
            UseOverlay: true));
        var list = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(created.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();
        var overlay = Assert.Single(listPayload!.Data!.Items);
        var text = await File.ReadAllTextAsync(overlay.OverlayPath);

        Assert.Equal(HttpStatusCode.OK, firstApply.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondApply.StatusCode);
        Assert.Contains("你好 流放者", text);
    }

    [Fact]
    public async Task Batch_template_can_apply_on_existing_overlay_layer()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "sample.txt"), "Hello exile");
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
        var first = new BatchScriptOperationDto("step1", "text", "exile", "流放者", ResourceKind.Text, ".txt", Take: 20);
        var second = new BatchScriptOperationDto("step2", "text", "Hello", "你好", ResourceKind.Text, ".txt", Take: 20);
        var save = await client.PostAsJsonAsync("/api/batch/templates/save", new BatchScriptTemplateSaveRequest(
            created.Data.Id,
            "第二步",
            [second]));
        var savePayload = await save.Content.ReadFromJsonAsync<ApiResponse<BatchScriptTemplateDto>>();

        await client.PostAsJsonAsync("/api/batch/run-script", new BatchScriptRunRequest(
            created.Data.Id,
            [first],
            Apply: true));
        var templateApply = await client.PostAsJsonAsync("/api/batch/run-template", new BatchScriptTemplateRunRequest(
            created.Data.Id,
            savePayload!.Data!.Id,
            Apply: true,
            UseOverlay: true));
        var list = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(created.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();
        var overlay = Assert.Single(listPayload!.Data!.Items);
        var text = await File.ReadAllTextAsync(overlay.OverlayPath);

        Assert.Equal(HttpStatusCode.OK, templateApply.StatusCode);
        Assert.Contains("你好 流放者", text);
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
        var data = new byte[64];
        data[0] = 2;
        data[4] = 16;
        System.Text.Encoding.ASCII.GetBytes("Sword").CopyTo(data, 16);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "metadata", "sample.datc64"), data);
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
        Assert.StartsWith("02 00 00 00 10 00 00 00", payload?.Data?.HexPreview);
        Assert.Equal("2", payload?.Data?.HeaderFields?["u32_0"]);
        Assert.Contains(payload?.Data?.Strings ?? [], item => item.Value == "Sword");
        Assert.Contains(payload?.Data?.LayoutHints ?? [], item => item.Contains("可能行宽", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(payload?.Data?.Warnings ?? [], item => item.Contains("二进制", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Table_inspect_uses_schema_for_binary_dat_rows()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "metadata"));
        var data = new byte[40];
        data[0] = 2;
        data[4] = 16;
        data[8] = 1;
        System.Text.Encoding.ASCII.GetBytes("Sword").CopyTo(data, 12);
        data[24] = 2;
        System.Text.Encoding.ASCII.GetBytes("Shield").CopyTo(data, 28);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "metadata", "schema.datc64"), data);
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

        var inspect = await client.PostAsJsonAsync("/api/tables/inspect", new TableInspectRequest(
            created.Data.Id,
            "metadata/schema.datc64",
            Schema: new TableSchemaDto(
                16,
                8,
                [
                    new TableSchemaFieldDto("id", 0, "u32"),
                    new TableSchemaFieldDto("name", 4, "ascii", 8)
                ])));
        var payload = await inspect.Content.ReadFromJsonAsync<ApiResponse<TableInspectResponse>>();

        Assert.Equal(HttpStatusCode.OK, inspect.StatusCode);
        Assert.True(payload?.Data?.Structured);
        Assert.Equal(["id", "name"], payload?.Data?.Columns);
        Assert.Equal(["1", "Sword"], payload!.Data!.Rows[0].Cells);
        Assert.Equal(["2", "Shield"], payload.Data.Rows[1].Cells);
    }

    [Fact]
    public async Task Table_schema_can_be_saved_listed_applied_and_deleted()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "metadata"));
        var data = new byte[40];
        data[0] = 2;
        data[4] = 16;
        data[8] = 1;
        System.Text.Encoding.ASCII.GetBytes("Sword").CopyTo(data, 12);
        data[24] = 2;
        System.Text.Encoding.ASCII.GetBytes("Shield").CopyTo(data, 28);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "metadata", "schema.datc64"), data);
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
        var schema = new TableSchemaDto(
            16,
            8,
            [
                new TableSchemaFieldDto("id", 0, "u32"),
                new TableSchemaFieldDto("name", 4, "ascii", 8)
            ]);

        var save = await client.PostAsJsonAsync("/api/tables/schemas/save", new TableSchemaSaveRequest(
            created.Data.Id,
            "metadata/schema.datc64",
            "Items",
            schema));
        var savePayload = await save.Content.ReadFromJsonAsync<ApiResponse<TableSchemaEntryDto>>();
        var list = await client.PostAsJsonAsync("/api/tables/schemas/list", new TableSchemaListRequest(
            created.Data.Id,
            "metadata/schema.datc64"));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<TableSchemaListResponse>>();
        var inspect = await client.PostAsJsonAsync("/api/tables/inspect", new TableInspectRequest(
            created.Data.Id,
            "metadata/schema.datc64",
            SchemaId: savePayload!.Data!.Id));
        var inspectPayload = await inspect.Content.ReadFromJsonAsync<ApiResponse<TableInspectResponse>>();
        var delete = await client.PostAsJsonAsync("/api/tables/schemas/delete", new TableSchemaDeleteRequest(
            created.Data.Id,
            savePayload.Data.Id));
        var deletePayload = await delete.Content.ReadFromJsonAsync<ApiResponse<TableSchemaDeleteResponse>>();
        var emptyList = await client.PostAsJsonAsync("/api/tables/schemas/list", new TableSchemaListRequest(
            created.Data.Id,
            "metadata/schema.datc64"));
        var emptyPayload = await emptyList.Content.ReadFromJsonAsync<ApiResponse<TableSchemaListResponse>>();

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal("Items", savePayload.Data.Name);
        Assert.Equal("metadata/schema.datc64", savePayload.Data.VirtualPath);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Single(listPayload!.Data!.Items);
        Assert.True(inspectPayload!.Data!.Structured);
        Assert.Equal(["1", "Sword"], inspectPayload.Data.Rows[0].Cells);
        Assert.True(deletePayload!.Data!.Removed);
        Assert.Empty(emptyPayload!.Data!.Items);
    }

    [Fact]
    public async Task Table_schema_infer_uses_neighbor_fmt_for_datc64()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "metadata"));
        var data = new byte[40];
        data[0] = 2;
        data[4] = 16;
        data[8] = 1;
        System.Text.Encoding.ASCII.GetBytes("Sword").CopyTo(data, 12);
        data[24] = 2;
        System.Text.Encoding.ASCII.GetBytes("Shield").CopyTo(data, 28);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "metadata", "schema.datc64"), data);
        await File.WriteAllTextAsync(Path.Combine(bundles, "metadata", "schema.fmt"), """
recordSize=16
headerSize=8
id 0 u32
name 4 ascii 8
""");
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

        var infer = await client.PostAsJsonAsync("/api/tables/schemas/infer", new TableSchemaInferRequest(
            created.Data.Id,
            "metadata/schema.datc64"));
        var inferPayload = await infer.Content.ReadFromJsonAsync<ApiResponse<TableSchemaInferResponse>>();
        var inspect = await client.PostAsJsonAsync("/api/tables/inspect", new TableInspectRequest(
            created.Data.Id,
            "metadata/schema.datc64",
            Schema: inferPayload!.Data!.Schema));
        var inspectPayload = await inspect.Content.ReadFromJsonAsync<ApiResponse<TableInspectResponse>>();

        Assert.Equal(HttpStatusCode.OK, infer.StatusCode);
        Assert.True(inferPayload.Data.Inferred);
        Assert.Equal("metadata/schema.fmt", inferPayload.Data.FormatPath);
        Assert.Equal(16, inferPayload.Data.Schema?.RecordSize);
        Assert.Equal(8, inferPayload.Data.Schema?.HeaderSize);
        Assert.Equal(["id", "name"], inferPayload.Data.Schema?.Fields.Select(field => field.Name).ToArray());
        Assert.True(inspectPayload!.Data!.Structured);
        Assert.Equal(["1", "Sword"], inspectPayload.Data.Rows[0].Cells);
    }

    [Fact]
    public async Task Table_csv_export_import_and_reference_scan_support_structured_binary_tables()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "data"));
        await File.WriteAllBytesAsync(Path.Combine(bundles, "data", "items.dat"), [
            1, 0, 0, 0, (byte)'A', 0, 0, 0,
            2, 0, 0, 0, (byte)'B', 0, 0, 0
        ]);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "data", "refs.dat"), [
            1, 0, 0, 0,
            3, 0, 0, 0
        ]);
        var schema = new TableSchemaDto(
            RecordSize: 8,
            HeaderSize: 0,
            Fields:
            [
                new TableSchemaFieldDto("id", 0, "u32"),
                new TableSchemaFieldDto("name", 4, "ascii", 4)
            ]);
        var refSchema = new TableSchemaDto(
            RecordSize: 4,
            HeaderSize: 0,
            Fields: [new TableSchemaFieldDto("item_id", 0, "u32")]);
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
        await client.PostAsJsonAsync("/api/index/build", new ResourceIndexBuildRequest(created!.Data!.Id));

        var export = await client.PostAsJsonAsync("/api/tables/export-csv", new TableCsvExportRequest(
            created.Data.Id,
            "data/items.dat",
            Schema: schema));
        var exportPayload = await export.Content.ReadFromJsonAsync<ApiResponse<TableCsvExportResponse>>();
        var editedCsv = exportPayload!.Data!.Csv.Replace("B", "Beta", StringComparison.Ordinal);
        var import = await client.PostAsJsonAsync("/api/tables/import-csv", new TableCsvImportRequest(
            created.Data.Id,
            "data/items.dat",
            editedCsv,
            Schema: schema));
        var importText = await import.Content.ReadAsStringAsync();
        var importPayload = JsonSerializer.Deserialize<ApiResponse<TableCsvImportResponse>>(importText, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var inspect = await client.PostAsJsonAsync("/api/tables/inspect", new TableInspectRequest(
            created.Data.Id,
            "data/items.dat",
            Schema: schema));
        var inspectPayload = await inspect.Content.ReadFromJsonAsync<ApiResponse<TableInspectResponse>>();
        var refs = await client.PostAsJsonAsync("/api/tables/reference-scan", new TableReferenceScanRequest(
            created.Data.Id,
            "data/refs.dat",
            ColumnIndex: 0,
            TargetVirtualPath: "data/items.dat",
            TargetColumnIndex: 0,
            Schema: refSchema,
            TargetSchema: schema));
        var refsPayload = await refs.Content.ReadFromJsonAsync<ApiResponse<TableReferenceScanResponse>>();

        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Contains("id,name", exportPayload.Data.Csv);
        Assert.True(import.StatusCode == HttpStatusCode.OK, importText);
        Assert.Equal(1, importPayload?.Data?.EditedCells);
        Assert.Contains(inspectPayload!.Data!.Rows, row => row.Cells.Contains("Beta"));
        Assert.Equal(1, refsPayload?.Data?.Matched);
        Assert.Equal(1, refsPayload?.Data?.Missing);
        Assert.Contains(refsPayload!.Data!.Items, item => item.Value == "3" && !item.Matched);
    }

    [Fact]
    public async Task Table_csv_import_preserves_utf16_text_table_encoding()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "data"));
        var tableText = "id\tname\r\n1\t简体中文\r\n2\t繁體中文\r\n";
        await File.WriteAllBytesAsync(
            Path.Combine(bundles, "data", "strings.tdt"),
            System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes(tableText)).ToArray());
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
        await client.PostAsJsonAsync("/api/index/build", new ResourceIndexBuildRequest(created!.Data!.Id));

        var export = await client.PostAsJsonAsync("/api/tables/export-csv", new TableCsvExportRequest(
            created.Data.Id,
            "data/strings.tdt"));
        var exportPayload = await export.Content.ReadFromJsonAsync<ApiResponse<TableCsvExportResponse>>();
        var editedCsv = $"\uFEFF{exportPayload!.Data!.Csv.Replace("简体中文", "简体文本", StringComparison.Ordinal)}";
        var import = await client.PostAsJsonAsync("/api/tables/import-csv", new TableCsvImportRequest(
            created.Data.Id,
            "data/strings.tdt",
            editedCsv));
        var importPayload = await import.Content.ReadFromJsonAsync<ApiResponse<TableCsvImportResponse>>();
        var overlayBytes = await File.ReadAllBytesAsync(importPayload!.Data!.Overlay.OverlayPath);
        var inspect = await client.PostAsJsonAsync("/api/tables/inspect", new TableInspectRequest(
            created.Data.Id,
            "data/strings.tdt"));
        var inspectPayload = await inspect.Content.ReadFromJsonAsync<ApiResponse<TableInspectResponse>>();

        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Contains("简体中文", exportPayload.Data.Csv);
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        Assert.Equal(1, importPayload.Data.EditedCells);
        Assert.True(overlayBytes.AsSpan(0, 2).SequenceEqual(System.Text.Encoding.Unicode.GetPreamble()));
        Assert.Equal("UTF-16LE", inspectPayload!.Data!.TextEncoding);
        Assert.Contains(inspectPayload.Data.Rows, row => row.Cells.Contains("简体文本"));
    }

    [Fact]
    public async Task Table_csv_file_import_accepts_multipart_without_json_stringifying_csv()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "data"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "data", "items.dat"), "id\tname\r\n1\t旧文本\r\n");
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
        await client.PostAsJsonAsync("/api/index/build", new ResourceIndexBuildRequest(created!.Data!.Id));
        var export = await client.PostAsJsonAsync("/api/tables/export-csv", new TableCsvExportRequest(
            created.Data.Id,
            "data/items.dat"));
        var exportPayload = await export.Content.ReadFromJsonAsync<ApiResponse<TableCsvExportResponse>>();
        var editedCsv = exportPayload!.Data!.Csv.Replace("旧文本", "新文本", StringComparison.Ordinal);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(created.Data.Id), "profileId");
        form.Add(new StringContent("data/items.dat"), "virtualPath");
        form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(editedCsv)).ToArray()), "csvFile", "items.csv");

        var import = await client.PostAsync("/api/tables/import-csv-file", form);
        var importText = await import.Content.ReadAsStringAsync();
        var importPayload = JsonSerializer.Deserialize<ApiResponse<TableCsvImportResponse>>(importText, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var inspect = await client.PostAsJsonAsync("/api/tables/inspect", new TableInspectRequest(
            created.Data.Id,
            "data/items.dat"));
        var inspectPayload = await inspect.Content.ReadFromJsonAsync<ApiResponse<TableInspectResponse>>();

        Assert.True(import.StatusCode == HttpStatusCode.OK, importText);
        Assert.Equal(1, importPayload!.Data!.EditedCells);
        Assert.Contains(inspectPayload!.Data!.Rows, row => row.Cells.Contains("新文本"));
    }

    [Fact]
    public async Task Table_csv_import_ignores_readonly_columns_changed_by_spreadsheet_tools()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "data"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "data", "items.dat"), "id\tname\r\n1\t旧文本\r\n");
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
        var schema = new TableSchemaDto(
            RecordSize: 20,
            HeaderSize: 0,
            Fields:
            [
                new TableSchemaFieldDto("id", 0, "u32"),
                new TableSchemaFieldDto("name", 4, "utf8z", 12),
                new TableSchemaFieldDto("enabled", 16, "u8")
            ]);
        var data = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), 1);
        Encoding.UTF8.GetBytes("旧文本").CopyTo(data.AsSpan(4));
        data[16] = 1;
        await File.WriteAllBytesAsync(Path.Combine(bundles, "data", "schema.datc64"), data);
        await client.PostAsJsonAsync("/api/index/build", new ResourceIndexBuildRequest(created!.Data!.Id));
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(created.Data.Id), "profileId");
        form.Add(new StringContent("data/schema.datc64"), "virtualPath");
        form.Add(new StringContent(JsonSerializer.Serialize(schema)), "schema");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("id,name,enabled\r\n999,新文本,0\r\n")), "csvFile", "schema.csv");

        var import = await client.PostAsync("/api/tables/import-csv-file", form);
        var importText = await import.Content.ReadAsStringAsync();
        var importPayload = JsonSerializer.Deserialize<ApiResponse<TableCsvImportResponse>>(importText, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var inspect = await client.PostAsJsonAsync("/api/tables/inspect", new TableInspectRequest(
            created.Data.Id,
            "data/schema.datc64",
            Schema: schema));
        var inspectPayload = await inspect.Content.ReadFromJsonAsync<ApiResponse<TableInspectResponse>>();

        Assert.True(import.StatusCode == HttpStatusCode.OK, importText);
        Assert.Equal(1, importPayload!.Data!.EditedCells);
        Assert.Equal(["1", "新文本", "1"], inspectPayload!.Data!.Rows[0].Cells);
    }

    [Fact]
    public async Task Structured_ui_and_atlas_resources_can_be_inspected_and_edited_to_overlay()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "ui"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "ui", "panel.ui"), "title = Old\nsize = 10\n");
        await File.WriteAllTextAsync(Path.Combine(bundles, "ui", "panel.atlas"), "icon 0 0 32 32\nlabel 32 0 64 16\n");
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
        await client.PostAsJsonAsync("/api/index/build", new ResourceIndexBuildRequest(created!.Data!.Id));

        var inspectUi = await client.PostAsJsonAsync("/api/resources/structured-inspect", new StructuredTextInspectRequest(
            created.Data.Id,
            "ui/panel.ui"));
        var inspectUiPayload = await inspectUi.Content.ReadFromJsonAsync<ApiResponse<StructuredTextInspectResponse>>();
        var inspectAtlas = await client.PostAsJsonAsync("/api/resources/structured-inspect", new StructuredTextInspectRequest(
            created.Data.Id,
            "ui/panel.atlas"));
        var inspectAtlasPayload = await inspectAtlas.Content.ReadFromJsonAsync<ApiResponse<StructuredTextInspectResponse>>();
        var save = await client.PostAsJsonAsync("/api/resources/structured-save", new StructuredTextSaveRequest(
            created.Data.Id,
            "ui/panel.ui",
            [new StructuredTextEditDto("title", "New")]));
        var savePayload = await save.Content.ReadFromJsonAsync<ApiResponse<StructuredTextSaveResponse>>();
        var preview = await client.PostAsJsonAsync("/api/resources/preview", new ResourcePreviewRequest(
            created.Data.Id,
            "ui/panel.ui"));
        var previewPayload = await preview.Content.ReadFromJsonAsync<ApiResponse<ResourcePreviewResponse>>();

        Assert.Equal(HttpStatusCode.OK, inspectUi.StatusCode);
        Assert.Contains(inspectUiPayload!.Data!.Nodes, node => node.Key == "title" && node.Value == "Old");
        Assert.Equal(HttpStatusCode.OK, inspectAtlas.StatusCode);
        Assert.Contains(inspectAtlasPayload!.Data!.Nodes, node => node.Key == "icon" && node.Value == "0 0 32 32");
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal(1, savePayload?.Data?.Edited);
        Assert.Contains("title = New", previewPayload!.Data!.Text);
    }

    [Fact]
    public async Task Table_save_with_schema_writes_binary_overlay_without_resizing_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "metadata"));
        var data = new byte[40];
        data[0] = 2;
        data[4] = 16;
        data[8] = 1;
        System.Text.Encoding.ASCII.GetBytes("Sword").CopyTo(data, 12);
        data[24] = 2;
        System.Text.Encoding.ASCII.GetBytes("Shield").CopyTo(data, 28);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "metadata", "schema.datc64"), data);
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
        var schema = new TableSchemaDto(
            16,
            8,
            [
                new TableSchemaFieldDto("id", 0, "u32"),
                new TableSchemaFieldDto("name", 4, "ascii", 8)
            ]);
        var schemaSave = await client.PostAsJsonAsync("/api/tables/schemas/save", new TableSchemaSaveRequest(
            created.Data.Id,
            "metadata/schema.datc64",
            "Items",
            schema));
        var schemaPayload = await schemaSave.Content.ReadFromJsonAsync<ApiResponse<TableSchemaEntryDto>>();

        var save = await client.PostAsJsonAsync("/api/tables/save", new TableSaveRequest(
            created.Data.Id,
            "metadata/schema.datc64",
            [
                new TableCellEditDto(2, 0, "99"),
                new TableCellEditDto(2, 1, "Axe")
            ],
            SchemaId: schemaPayload!.Data!.Id));
        var payload = await save.Content.ReadFromJsonAsync<ApiResponse<TableSaveResponse>>();
        var overlayBytes = await File.ReadAllBytesAsync(payload!.Data!.Overlay.OverlayPath);
        var inspect = await client.PostAsJsonAsync("/api/tables/inspect", new TableInspectRequest(
            created.Data.Id,
            "metadata/schema.datc64",
            Schema: schema));
        var inspectPayload = await inspect.Content.ReadFromJsonAsync<ApiResponse<TableInspectResponse>>();

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal(2, payload.Data.EditedCells);
        Assert.Equal(data.Length, overlayBytes.Length);
        Assert.Equal(99, overlayBytes[24]);
        Assert.Equal((byte)'A', overlayBytes[28]);
        Assert.Equal(["1", "Sword"], inspectPayload!.Data!.Rows[0].Cells);
    }

    [Fact]
    public async Task Table_save_with_schema_applies_next_edit_on_existing_overlay()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "metadata"));
        var data = new byte[40];
        data[0] = 2;
        data[4] = 16;
        data[8] = 1;
        System.Text.Encoding.ASCII.GetBytes("Sword").CopyTo(data, 12);
        data[24] = 2;
        System.Text.Encoding.ASCII.GetBytes("Shield").CopyTo(data, 28);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "metadata", "schema.datc64"), data);
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
        var schema = new TableSchemaDto(
            16,
            8,
            [
                new TableSchemaFieldDto("id", 0, "u32"),
                new TableSchemaFieldDto("name", 4, "ascii", 8)
            ]);

        var first = await client.PostAsJsonAsync("/api/tables/save", new TableSaveRequest(
            created.Data.Id,
            "metadata/schema.datc64",
            [new TableCellEditDto(2, 0, "99")],
            Schema: schema));
        var firstPayload = await first.Content.ReadFromJsonAsync<ApiResponse<TableSaveResponse>>();
        var second = await client.PostAsJsonAsync("/api/tables/save", new TableSaveRequest(
            created.Data.Id,
            "metadata/schema.datc64",
            [new TableCellEditDto(2, 1, "Axe")],
            Schema: schema));
        var secondPayload = await second.Content.ReadFromJsonAsync<ApiResponse<TableSaveResponse>>();
        var overlayBytes = await File.ReadAllBytesAsync(secondPayload!.Data!.Overlay.OverlayPath);
        var inspector = new PoeStudio.Core.Tables.TableInspector();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: created.Data.Id,
            VirtualPath: "metadata/schema.datc64",
            NormalizedPath: "metadata/schema.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: overlayBytes.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Overlay,
            IndexedAt: DateTimeOffset.UtcNow);
        var preview = inspector.Inspect(resource, overlayBytes, 4096, schema);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.NotNull(firstPayload?.Data);
        Assert.Equal(["99", "Axe"], preview.Rows[1].Cells);
    }

    [Fact]
    public async Task Table_inspect_prefers_existing_overlay_after_schema_save()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "metadata"));
        var data = new byte[40];
        data[0] = 2;
        data[4] = 16;
        data[8] = 1;
        System.Text.Encoding.ASCII.GetBytes("Sword").CopyTo(data, 12);
        data[24] = 2;
        System.Text.Encoding.ASCII.GetBytes("Shield").CopyTo(data, 28);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "metadata", "schema.datc64"), data);
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
        var schema = new TableSchemaDto(
            16,
            8,
            [
                new TableSchemaFieldDto("id", 0, "u32"),
                new TableSchemaFieldDto("name", 4, "ascii", 8)
            ]);
        await client.PostAsJsonAsync("/api/tables/save", new TableSaveRequest(
            created.Data.Id,
            "metadata/schema.datc64",
            [new TableCellEditDto(2, 1, "Axe")],
            Schema: schema));

        var inspect = await client.PostAsJsonAsync("/api/tables/inspect", new TableInspectRequest(
            created.Data.Id,
            "metadata/schema.datc64",
            Schema: schema));
        var payload = await inspect.Content.ReadFromJsonAsync<ApiResponse<TableInspectResponse>>();

        Assert.Equal(HttpStatusCode.OK, inspect.StatusCode);
        Assert.Equal(["2", "Axe"], payload!.Data!.Rows[1].Cells);
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
    public async Task Patch_analyze_zip_endpoint_reports_external_patch_structure()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var zipPath = Path.Combine(root, "external.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            await WriteZipEntryAsync(zip, "Bundles2/_.index.bin", [1, 2, 3]);
            await WriteZipEntryAsync(zip, "Bundles2/Tiny.V0.1.bundle.bin", [4, 5, 6]);
        }
        var client = factory.CreateClient();

        var analyze = await client.PostAsJsonAsync("/api/patch/analyze-zip", new PatchZipAnalyzeRequest(zipPath, "Tiny.V0.1.bundle.bin"));
        var payload = await analyze.Content.ReadFromJsonAsync<ApiResponse<PatchZipAnalyzeResponse>>();

        Assert.Equal(HttpStatusCode.OK, analyze.StatusCode);
        Assert.True(payload?.Data?.HasIndex);
        Assert.True(payload?.Data?.HasPatchBundle);
        Assert.Equal(PatchZipTemplate.Official, payload?.Data?.Template);
        Assert.Equal(2, payload?.Data?.EntryCount);
    }

    [Fact]
    public async Task Patch_analyze_zip_job_eventually_returns_structure_summary()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var zipPath = Path.Combine(root, "external-job.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            await WriteZipEntryAsync(zip, "Bundles2/_.index.bin", [1, 2, 3]);
            await WriteZipEntryAsync(zip, "Bundles2/Tiny.V0.1.bundle.bin", [4, 5, 6]);
        }
        var client = factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/jobs/patch/analyze-zip", new PatchZipAnalyzeRequest(zipPath, "Tiny.V0.1.bundle.bin"));
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
        Assert.Contains("hasIndex", snapshot.ResultJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Patch_import_zip_job_adds_external_patch_to_build_history()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "client", "Bundles2");
        Directory.CreateDirectory(bundles);
        var zipPath = Path.Combine(root, "external-import.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            await WriteZipEntryAsync(zip, "Bundles2/_.index.bin", [1, 2, 3]);
            await WriteZipEntryAsync(zip, "Bundles2/Tiny.V0.1.bundle.bin", [4, 5, 6]);
        }
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "WeGame",
            RootPath: Path.Combine(root, "client"),
            Platform: ClientPlatform.WeGame,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: Path.Combine(bundles, "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();

        var start = await client.PostAsJsonAsync("/api/jobs/patch/import-zip", new PatchZipImportRequest(created!.Data!.Id, zipPath, "Tiny.V0.1.bundle.bin"));
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
        var history = await client.PostAsJsonAsync("/api/patch/build-history", new PatchBuildHistoryRequest(created.Data.Id));
        var historyPayload = await history.Content.ReadFromJsonAsync<ApiResponse<PatchBuildHistoryResponse>>();
        var item = Assert.Single(historyPayload?.Data?.Items ?? []);
        var manifest = await client.PostAsJsonAsync("/api/patch/import-manifest", new PatchImportManifestRequest(created.Data.Id, item.BuildId));
        var manifestPayload = await manifest.Content.ReadFromJsonAsync<ApiResponse<PatchZipImportManifestDto>>();

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.NotNull(snapshot);
        Assert.Equal(JobStatus.Succeeded, snapshot!.Status);
        Assert.Contains("external", item.ZipPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(item.ImportManifestPath));
        Assert.True(File.Exists(Path.Combine(item.OutputDirectory, "Bundles2", "_.index.bin")));
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        Assert.Equal(zipPath, manifestPayload?.Data?.SourceZipPath);
    }

    [Fact]
    public async Task Patch_preview_zip_install_job_reports_file_impact()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "client", "Bundles2");
        Directory.CreateDirectory(bundles);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "_.index.bin"), [1, 1, 1]);
        var zipPath = Path.Combine(root, "external-impact.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            await WriteZipEntryAsync(zip, "Bundles2/_.index.bin", [2, 2, 2]);
            await WriteZipEntryAsync(zip, "Bundles2/Tiny.V0.1.bundle.bin", [4, 5, 6]);
        }
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "WeGame",
            RootPath: Path.Combine(root, "client"),
            Platform: ClientPlatform.WeGame,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: Path.Combine(bundles, "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();

        var start = await client.PostAsJsonAsync("/api/jobs/patch/preview-zip-install", new PatchZipInstallPreviewRequest(created!.Data!.Id, zipPath, "Tiny.V0.1.bundle.bin"));
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
        Assert.Contains("replacedFiles", snapshot.ResultJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("newFiles", snapshot.ResultJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Patch_import_overlay_draft_job_turns_imported_patch_into_overlay()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var clientRoot = Path.Combine(root, "client");
        var bundles = Path.Combine(clientRoot, "Bundles2");
        Directory.CreateDirectory(bundles);
        var zipPath = Path.Combine(root, "external-draft.zip");
        var hash = PoeStudio.Core.Native.NativeIndexPathResolver.MurmurHash64A(System.Text.Encoding.UTF8.GetBytes("text/sample.txt"));
        var indexPayload = await BuildPatchIndexPayloadAsync(root, hash);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var compressor = new PoeStudio.Core.Native.NativeBundleCompressor(new PoeStudio.Core.Native.CopyNativeBundleCodec());
            await WriteZipEntryAsync(zip, "Bundles2/_.index.bin", compressor.Compress(indexPayload));
            await WriteZipEntryAsync(zip, "Bundles2/Tiny.V0.1.bundle.bin", compressor.Compress(System.Text.Encoding.UTF8.GetBytes("patched text")));
        }
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "WeGame",
            RootPath: clientRoot,
            Platform: ClientPlatform.WeGame,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: Path.Combine(bundles, "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();
        var resourceStore = factory.Services.GetRequiredService<PoeStudio.Storage.Resources.ResourceIndexStore>();
        await resourceStore.SaveAsync(created!.Data!.Id, [
            new ResourceSummaryDto(
                Id: "sample",
                ProfileId: created.Data.Id,
                VirtualPath: "text/sample.txt",
                NormalizedPath: "text/sample.txt",
                Extension: ".txt",
                Kind: ResourceKind.Text,
                Size: 4,
                PhysicalPath: Path.Combine(clientRoot, "Bundles2", "text", "sample.txt"),
                SourceLayer: ResourceSourceLayer.Base,
                IndexedAt: DateTimeOffset.UtcNow)
        ], [], CancellationToken.None);
        var import = await client.PostAsJsonAsync("/api/jobs/patch/import-zip", new PatchZipImportRequest(created.Data.Id, zipPath, "Tiny.V0.1.bundle.bin", "__copy__"));
        var importJob = await import.Content.ReadFromJsonAsync<ApiResponse<JobSnapshotDto>>();
        var imported = await WaitJobAsync(client, importJob!.Data!.Id);
        Assert.Equal(JobStatus.Succeeded, imported.Status);
        var history = await client.PostAsJsonAsync("/api/patch/build-history", new PatchBuildHistoryRequest(created.Data.Id));
        var historyPayload = await history.Content.ReadFromJsonAsync<ApiResponse<PatchBuildHistoryResponse>>();
        var buildId = Assert.Single(historyPayload?.Data?.Items ?? []).BuildId;

        var draft = await client.PostAsJsonAsync("/api/jobs/patch/import-overlay-draft", new PatchOverlayDraftRequest(created.Data.Id, buildId, "Tiny.V0.1.bundle.bin", "__copy__"));
        var draftJob = await draft.Content.ReadFromJsonAsync<ApiResponse<JobSnapshotDto>>();
        var draftDone = await WaitJobAsync(client, draftJob!.Data!.Id);
        var list = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(created.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();

        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        Assert.Equal(JobStatus.Succeeded, draftDone.Status);
        var item = Assert.Single(listPayload?.Data?.Items ?? []);
        Assert.Equal("text/sample.txt", item.VirtualPath);
    }

    [Fact]
    public async Task Patch_sandbox_prepare_seeds_client_shell_and_validates_build()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var clientRoot = Path.Combine(root, "client");
        var bundles = Path.Combine(clientRoot, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(clientRoot, "PathOfExile.exe"), "launcher");
        await File.WriteAllTextAsync(Path.Combine(bundles, "_.index.bin"), "base-index");
        await File.WriteAllTextAsync(Path.Combine(bundles, "Base.bundle.bin"), "base-bundle");
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "sample.txt"), "base");
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "Official",
            RootPath: clientRoot,
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: Path.Combine(bundles, "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();
        await client.PostAsJsonAsync("/api/index/build", new ResourceIndexBuildRequest(created!.Data!.Id));
        await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(created.Data.Id, "text/sample.txt", "overlay"));
        var build = await client.PostAsJsonAsync("/api/patch/build", new PatchBuildRequest(created.Data.Id));
        var buildPayload = await build.Content.ReadFromJsonAsync<ApiResponse<PatchBuildResponse>>();
        var buildId = new DirectoryInfo(buildPayload!.Data!.OutputDirectory).Name;
        var sandbox = Path.Combine(root, "sandbox-client");

        var response = await client.PostAsJsonAsync("/api/patch/sandbox-prepare", new PatchSandboxPrepareRequest(created.Data.Id, buildId, sandbox));
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<PatchSandboxPrepareResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload?.Data?.Ok);
        Assert.Equal(3, payload?.Data?.SeededFiles);
        Assert.Equal(2, payload?.Data?.Validation.CheckedFiles);
        Assert.True(File.Exists(Path.Combine(sandbox, "PathOfExile.exe")));
        Assert.True(File.Exists(Path.Combine(sandbox, "Bundles2", "Tiny.V0.1.bundle.bin")));
    }

    [Fact]
    public async Task Patch_sandbox_prepare_job_runs_without_blocking_request()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var clientRoot = Path.Combine(root, "client");
        var bundles = Path.Combine(clientRoot, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(clientRoot, "PathOfExile.exe"), "launcher");
        await File.WriteAllTextAsync(Path.Combine(bundles, "_.index.bin"), "base-index");
        await File.WriteAllTextAsync(Path.Combine(bundles, "Base.bundle.bin"), "base-bundle");
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "sample.txt"), "base");
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "Official",
            RootPath: clientRoot,
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: Path.Combine(bundles, "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "fingerprint"));
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();
        await client.PostAsJsonAsync("/api/index/build", new ResourceIndexBuildRequest(created!.Data!.Id));
        await client.PostAsJsonAsync("/api/overlay/save-text", new SaveTextOverlayRequest(created.Data.Id, "text/sample.txt", "overlay"));
        var build = await client.PostAsJsonAsync("/api/patch/build", new PatchBuildRequest(created.Data.Id));
        var buildPayload = await build.Content.ReadFromJsonAsync<ApiResponse<PatchBuildResponse>>();
        var buildId = new DirectoryInfo(buildPayload!.Data!.OutputDirectory).Name;
        var sandbox = Path.Combine(root, "sandbox-client");

        var start = await client.PostAsJsonAsync("/api/jobs/patch/sandbox-prepare", new PatchSandboxPrepareRequest(created.Data.Id, buildId, sandbox));
        var job = await start.Content.ReadFromJsonAsync<ApiResponse<JobSnapshotDto>>();
        var snapshot = await WaitJobAsync(client, job!.Data!.Id);

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("patch-sandbox-prepare", job.Data.Kind);
        Assert.Equal(JobStatus.Succeeded, snapshot.Status);
        Assert.Contains("seededFiles", snapshot.ResultJson, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(sandbox, "Bundles2", "Tiny.V0.1.bundle.bin")));
    }

    [Fact]
    public async Task Patch_pipeline_job_validates_migrates_builds_and_prepares_sandbox()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(root, "source");
        var targetRoot = Path.Combine(root, "target");
        var sourceBundles = Path.Combine(sourceRoot, "Bundles2");
        var targetBundles = Path.Combine(targetRoot, "Bundles2");
        Directory.CreateDirectory(Path.Combine(sourceBundles, "text"));
        Directory.CreateDirectory(Path.Combine(targetBundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(targetRoot, "PathOfExile.exe"), "launcher");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "_.index.bin"), "base-index");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "Base.bundle.bin"), "base-bundle");
        await File.WriteAllTextAsync(Path.Combine(sourceBundles, "text", "sample.txt"), "translated");
        await File.WriteAllTextAsync(Path.Combine(targetBundles, "text", "sample.txt"), "base");
        var sandbox = Path.Combine(root, "sandbox");
        var client = factory.CreateClient();
        var source = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "Source",
            RootPath: sourceRoot,
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Bundles2,
            ContentGgpkPath: null,
            Bundles2Path: sourceBundles,
            IndexPath: Path.Combine(sourceBundles, "_.index.bin"),
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "source"));
        var target = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "Target",
            RootPath: targetRoot,
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
        var plan = await client.PostAsJsonAsync("/api/resources/migration-plan", new ResourceMigrationPlanRequest(
            sourceProfile.Data.Id,
            targetProfile.Data.Id,
            Query: "text"));
        var planPayload = await plan.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanResponse>>();
        var save = await client.PostAsJsonAsync("/api/resources/migration-plans/save", new ResourceMigrationPlanSaveRequest(
            null,
            "流水线方案",
            new ResourceMigrationPlanCriteriaDto(sourceProfile.Data.Id, targetProfile.Data.Id, "text"),
            planPayload!.Data!.Items));
        var savePayload = await save.Content.ReadFromJsonAsync<ApiResponse<ResourceMigrationPlanEntryDto>>();

        var start = await client.PostAsJsonAsync("/api/jobs/patch/pipeline-run", new PatchPipelineRunRequest(
            sourceProfile.Data.Id,
            targetProfile.Data.Id,
            savePayload!.Data!.Id,
            PatchZipTemplate.WeGame,
            IncludeCandidates: true,
            SandboxRootPath: sandbox));
        var job = await start.Content.ReadFromJsonAsync<ApiResponse<JobSnapshotDto>>();
        var snapshot = await WaitJobAsync(client, job!.Data!.Id);
        var result = System.Text.Json.JsonSerializer.Deserialize<PatchPipelineRunResponse>(
            snapshot.ResultJson!,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("patch-pipeline-run", job.Data.Kind);
        Assert.Equal(JobStatus.Succeeded, snapshot.Status);
        Assert.True(result!.Ok);
        Assert.Equal(1, result.Validation.Ready);
        Assert.Equal(1, result.Migration.Drafted);
        Assert.Equal(1, result.Build.TotalChanges);
        Assert.True(File.Exists(result.Build.ZipPath));
        Assert.NotNull(result.Sandbox);
        Assert.True(result.Sandbox!.Ok);
        Assert.True(File.Exists(Path.Combine(sandbox, "Bundles2", "Tiny.V0.1.bundle.bin")));
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
    public async Task Ggpk_build_resource_index_indexes_content_ggpk_profiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var ggpkPath = Path.Combine(root, "Content.ggpk");
        await GgpkTestData.WriteTinyGgpkAsync(ggpkPath);
        var client = factory.CreateClient();
        var create = await client.PostAsJsonAsync("/api/profiles", new CreateProfileRequest(
            DisplayName: "Official GGPK",
            RootPath: root,
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Ggpk,
            ContentGgpkPath: ggpkPath,
            Bundles2Path: null,
            IndexPath: null,
            OodleStatus: OodleStatus.Found,
            ClientFingerprint: "fingerprint"));
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ClientProfileDto>>();

        var build = await client.PostAsJsonAsync("/api/native/ggpk/build-resource-index", new GgpkResourceIndexBuildRequest(created!.Data!.Id));
        var buildPayload = await build.Content.ReadFromJsonAsync<ApiResponse<GgpkResourceIndexBuildResponse>>();
        var search = await client.PostAsJsonAsync("/api/resources/search", new ResourceSearchRequest(created.Data.Id, Query: "amulet"));
        var searchPayload = await search.Content.ReadFromJsonAsync<ApiResponse<ResourceSearchResponse>>();

        Assert.Equal(HttpStatusCode.OK, build.StatusCode);
        Assert.True(buildPayload?.Data?.Ok);
        Assert.Equal(1, buildPayload?.Data?.ResolvedResources);
        var resource = Assert.Single(searchPayload?.Data?.Items ?? []);
        Assert.Equal("metadata/items/amulet.ot", resource.VirtualPath);
        Assert.StartsWith("ggpk://", resource.PhysicalPath, StringComparison.Ordinal);
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

    private static async Task WriteZipEntryAsync(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes);
    }

    private static async Task<JobSnapshotDto> WaitJobAsync(HttpClient client, string jobId)
    {
        JobSnapshotDto? snapshot = null;
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(25);
            var statusPayload = await client.GetFromJsonAsync<ApiResponse<JobSnapshotDto>>($"/api/jobs/{jobId}");
            snapshot = statusPayload?.Data;
            if (snapshot?.Status is JobStatus.Succeeded or JobStatus.Failed)
            {
                return snapshot;
            }
        }

        return snapshot ?? throw new InvalidOperationException("Job did not return a snapshot.");
    }

    private static async Task<byte[]> BuildPatchIndexPayloadAsync(string root, ulong hash)
    {
        var path = Path.Combine(root, $"patch-index-{Guid.NewGuid():N}.bin");
        await using (var stream = File.Create(path))
        await using (var writer = new BinaryWriter(stream))
        {
            writer.Write(2);
            WriteBundleRecord(writer, "Base", 4096);
            WriteBundleRecord(writer, "Tiny.V0.1", 12);
            writer.Write(1);
            writer.Write(hash);
            writer.Write(1);
            writer.Write(0);
            writer.Write(12);
            writer.Write(0);
        }

        return await File.ReadAllBytesAsync(path);
    }

    private static void WriteBundleRecord(BinaryWriter writer, string path, int uncompressedSize)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(path);
        writer.Write(bytes.Length);
        writer.Write(bytes);
        writer.Write(uncompressedSize);
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
