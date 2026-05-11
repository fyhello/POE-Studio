using PoeStudio.Contracts;

namespace PoeStudio.Core.Patching;

public static class PatchRiskClassifier
{
    private static readonly HashSet<string> HighRiskExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mat", ".ao", ".aoc", ".pet", ".hlsl", ".fxgraph"
    };

    private static readonly HashSet<string> MediumRiskExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dat", ".datc64", ".fmt", ".tdt", ".ot", ".otc", ".ui", ".atlas"
    };

    public static PatchRiskLevel Classify(string virtualPath)
    {
        var extension = Path.GetExtension(virtualPath);
        if (HighRiskExtensions.Contains(extension))
        {
            return PatchRiskLevel.High;
        }

        if (MediumRiskExtensions.Contains(extension))
        {
            return PatchRiskLevel.Medium;
        }

        return PatchRiskLevel.Low;
    }
}
