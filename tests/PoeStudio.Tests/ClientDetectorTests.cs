using PoeStudio.Contracts;

namespace PoeStudio.Tests;

public sealed class ClientDetectorTests
{
    [Fact]
    public void ContractTypes_can_be_constructed()
    {
        var response = new DetectClientResponse(
            Detected: true,
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Ggpk,
            RootPath: "C:/Game",
            ContentGgpkPath: "C:/Game/Content.ggpk",
            Bundles2Path: null,
            IndexPath: null,
            OodleStatus: OodleStatus.Missing,
            OodlePath: null,
            ClientFingerprint: "abc",
            Warnings: Array.Empty<string>());

        Assert.True(response.Detected);
        Assert.Equal(ClientEntryKind.Ggpk, response.EntryKind);
    }
}
