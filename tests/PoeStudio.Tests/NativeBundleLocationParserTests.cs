using PoeStudio.Core.Patching;

namespace PoeStudio.Tests;

public sealed class NativeBundleLocationParserTests
{
    [Fact]
    public void TryParse_accepts_native_bundles2_location()
    {
        var ok = NativeBundleLocationParser.TryParse(
            "native-bundles2://folders/data/1a/balance.datc64.bundle.bin#offset=423448&size=11592",
            out var location);

        Assert.True(ok);
        Assert.Equal("folders/data/1a/balance.datc64.bundle.bin", location.BundleName);
        Assert.Equal(423448, location.Offset);
        Assert.Equal(11592, location.Size);
    }

    [Fact]
    public void TryParse_accepts_ggpk_embedded_bundles2_location()
    {
        var ok = NativeBundleLocationParser.TryParse(
            "ggpk-bundles2://E:\\PSAutoRecover\\ui\\rood\\Grinding Gear Games\\Path of Exile 2\\Content.ggpk#bundleOffset=15766937897&bundleSize=269942&offset=423448&size=11592&bundlePath=bundles2%2Ffolders%2Fdata%2F1a%2Fbalance.datc64.bundle.bin",
            out var location);

        Assert.True(ok);
        Assert.Equal("folders/data/1a/balance.datc64.bundle.bin", location.BundleName);
        Assert.Equal(423448, location.Offset);
        Assert.Equal(11592, location.Size);
    }
}
