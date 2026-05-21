using PoeStudio.Contracts;
using PoeStudio.Core.Resources;

namespace PoeStudio.Tests;

public sealed class ResourceClassifierTests
{
    [Theory]
    [InlineData("data/game.datc64", ResourceKind.Table)]
    [InlineData("data/game.dat", ResourceKind.Table)]
    [InlineData("metadata/item.ot", ResourceKind.Table)]
    [InlineData("ui/root.ui", ResourceKind.Ui)]
    [InlineData("ui/sprites.atlas", ResourceKind.Ui)]
    [InlineData("textures/icon.dds", ResourceKind.Image)]
    [InlineData("audio/click.ogg", ResourceKind.Audio)]
    [InlineData("fonts/main.ttf", ResourceKind.Font)]
    [InlineData("materials/item.mat", ResourceKind.Material)]
    [InlineData("models/player.smd", ResourceKind.Model)]
    [InlineData("config/filter.filter", ResourceKind.Text)]
    [InlineData("data/statdescriptions/stat_descriptions.csd", ResourceKind.Text)]
    [InlineData("unknown/blob.bin", ResourceKind.Binary)]
    public void Classify_maps_known_extensions(string virtualPath, ResourceKind expected)
    {
        Assert.Equal(expected, ResourceClassifier.Classify(virtualPath));
    }

    [Theory]
    [InlineData("metadata/items/foo.ot", "metadata/items/foo.ot")]
    [InlineData("Metadata\\Items\\Foo.OT", "metadata/items/foo.ot")]
    public void Normalize_accepts_safe_virtual_paths(string input, string expected)
    {
        Assert.Equal(expected, ResourcePath.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../secret.txt")]
    [InlineData("metadata/../../secret.txt")]
    [InlineData("\\metadata\\items\\foo.ot")]
    [InlineData("C:\\secret.txt")]
    [InlineData("//server/share/file.txt")]
    public void Normalize_rejects_unsafe_virtual_paths(string input)
    {
        Assert.Throws<ArgumentException>(() => ResourcePath.Normalize(input));
    }
}
