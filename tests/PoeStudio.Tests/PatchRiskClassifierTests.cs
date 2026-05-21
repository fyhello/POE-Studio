using PoeStudio.Contracts;
using PoeStudio.Core.Patching;

namespace PoeStudio.Tests;

public sealed class PatchRiskClassifierTests
{
    [Theory]
    [InlineData("config/text.txt", PatchRiskLevel.Low)]
    [InlineData("config/data.json", PatchRiskLevel.Low)]
    [InlineData("config/view.xml", PatchRiskLevel.Low)]
    [InlineData("data/base.dat", PatchRiskLevel.Medium)]
    [InlineData("data/base.datc64", PatchRiskLevel.Medium)]
    [InlineData("ui/main.ui", PatchRiskLevel.Medium)]
    [InlineData("metadata/effects/fire.mat", PatchRiskLevel.High)]
    [InlineData("metadata/effects/fire.ao", PatchRiskLevel.High)]
    [InlineData("metadata/effects/fire.aoc", PatchRiskLevel.High)]
    [InlineData("metadata/monsters/boss.pet", PatchRiskLevel.High)]
    [InlineData("shader/main.hlsl", PatchRiskLevel.High)]
    [InlineData("shader/main.fxgraph", PatchRiskLevel.High)]
    public void Classify_maps_extensions_to_patch_risk(string path, PatchRiskLevel expected)
    {
        Assert.Equal(expected, PatchRiskClassifier.Classify(path));
    }
}
