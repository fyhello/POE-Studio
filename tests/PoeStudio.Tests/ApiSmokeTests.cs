using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
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
}
