using PoeStudio.Contracts;

namespace PoeStudio.Core.Oodle;

public sealed record OodleDetectionResult(OodleStatus Status, string? Path);

public static class OodleDetector
{
    private static readonly string[] CandidateNames =
    [
        "oo2core.dll",
        "oo2core_9_win64.dll",
        "oo2core_8_win64.dll"
    ];

    public static OodleDetectionResult Detect(string rootPath, string? explicitSearchPath = null)
    {
        foreach (var dir in BuildSearchDirectories(rootPath, explicitSearchPath))
        {
            foreach (var name in CandidateNames)
            {
                var candidate = System.IO.Path.Combine(dir, name);
                if (File.Exists(candidate))
                {
                    return new OodleDetectionResult(OodleStatus.Found, candidate);
                }
            }
        }

        return new OodleDetectionResult(OodleStatus.Missing, null);
    }

    private static IEnumerable<string> BuildSearchDirectories(string rootPath, string? explicitSearchPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitSearchPath))
        {
            yield return explicitSearchPath;
        }

        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            yield return rootPath;
            yield return System.IO.Path.Combine(rootPath, "Bundles2");
            yield return System.IO.Path.Combine(rootPath, "Bundlebak");
        }
    }
}
