using PoeStudio.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;

namespace PoeStudio.Tests;

public sealed class TranslationWorkflowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public TranslationWorkflowTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PoeStudio:WorkspaceRoot", Path.Combine(Path.GetTempPath(), "poe-studio-translation-tests", Guid.NewGuid().ToString("N")));
        });
    }

    [Fact]
    public async Task Translation_export_and_import_csv_applies_text_overlays()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-translation-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "text"));
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "one.txt"), "Hello exile");
        await File.WriteAllTextAsync(Path.Combine(bundles, "text", "two.txt"), "Goodbye exile");
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

        var export = await client.PostAsJsonAsync("/api/translation/export-csv", new TranslationExportRequest(created.Data.Id, "text", Take: 20));
        var exportPayload = await export.Content.ReadFromJsonAsync<ApiResponse<TranslationExportResponse>>();
        var csv = exportPayload!.Data!.Csv.Replace(
            "\"text/one.txt\",\"Hello exile\",\"\",\"new\"",
            "\"text/one.txt\",\"Hello exile\",\"你好 exile\",\"ready\"",
            StringComparison.Ordinal);
        var import = await client.PostAsJsonAsync("/api/translation/import-csv", new TranslationImportRequest(created.Data.Id, csv));
        var importPayload = await import.Content.ReadFromJsonAsync<ApiResponse<TranslationImportResponse>>();
        var list = await client.PostAsJsonAsync("/api/overlay/list", new OverlayListRequest(created.Data.Id));
        var listPayload = await list.Content.ReadFromJsonAsync<ApiResponse<OverlayListResponse>>();

        Assert.Equal(2, exportPayload.Data.Exported);
        Assert.Equal(1, importPayload?.Data?.Applied);
        Assert.Equal("text/one.txt", Assert.Single(importPayload!.Data!.AppliedPaths));
        Assert.Equal(1, listPayload?.Data?.Total);
    }
}
