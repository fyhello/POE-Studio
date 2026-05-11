using PoeStudio.Contracts;
using PoeStudio.Storage.Profiles;

namespace PoeStudio.Tests;

public sealed class ProfileStoreTests
{
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
}
