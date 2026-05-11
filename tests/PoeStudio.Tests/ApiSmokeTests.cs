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
}
