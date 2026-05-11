using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Storage.Profiles;

namespace PoeStudio.Tests;

public sealed class ProfileStoreTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [Fact]
    public async Task Save_and_list_profiles_roundtrips_json()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "poe-studio-tests", Guid.NewGuid().ToString("N"));
        var store = new ProfileStore(workspace);
        var profile = new ClientProfileDto(
            Id: "profile-1",
            DisplayName: "Official",
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Ggpk,
            RootPath: "C:/Game",
            ContentGgpkPath: "C:/Game/Content.ggpk",
            Bundles2Path: null,
            IndexPath: null,
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "abc",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        await store.SaveAsync(profile, CancellationToken.None);
        var items = await store.ListAsync(CancellationToken.None);

        Assert.Single(items);
        Assert.Equal("profile-1", items[0].Id);
        Assert.Equal("Official", items[0].DisplayName);
    }

    [Fact]
    public async Task ListAsync_ignores_nested_profile_json_files()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "poe-studio-tests", Guid.NewGuid().ToString("N"));
        var store = new ProfileStore(workspace);
        var profile = new ClientProfileDto(
            Id: "profile-1",
            DisplayName: "Official",
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Ggpk,
            RootPath: "C:/Game",
            ContentGgpkPath: "C:/Game/Content.ggpk",
            Bundles2Path: null,
            IndexPath: null,
            OodleStatus: OodleStatus.Missing,
            ClientFingerprint: "abc",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);
        var nestedProfile = profile with
        {
            Id = "nested",
            DisplayName = "Nested"
        };

        await store.SaveAsync(profile, CancellationToken.None);
        var nestedPath = Path.Combine(workspace, "profiles", "profile-1", "cache", "profile.json");
        Directory.CreateDirectory(Path.GetDirectoryName(nestedPath)!);
        await File.WriteAllTextAsync(
            nestedPath,
            JsonSerializer.Serialize(nestedProfile, JsonOptions),
            CancellationToken.None);

        var items = await store.ListAsync(CancellationToken.None);

        Assert.Single(items);
        Assert.Equal("profile-1", items[0].Id);
    }
}
