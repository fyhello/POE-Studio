using PoeStudio.Contracts;
using PoeStudio.Core.Oodle;

namespace PoeStudio.Tests;

public sealed class OodleDetectorTests
{
    [Fact]
    public void Detect_returns_missing_when_no_candidate_exists()
    {
        var root = CreateTempDirectory();

        var result = OodleDetector.Detect(root);

        Assert.Equal(OodleStatus.Missing, result.Status);
        Assert.Null(result.Path);
    }

    [Fact]
    public void Detect_finds_oo2core_in_root_directory()
    {
        var root = CreateTempDirectory();
        var dll = Path.Combine(root, "oo2core.dll");
        File.WriteAllText(dll, "fake");

        var result = OodleDetector.Detect(root);

        Assert.Equal(OodleStatus.Found, result.Status);
        Assert.Equal(dll, result.Path);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "poe-studio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
