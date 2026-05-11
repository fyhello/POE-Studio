namespace PoeStudio.Tests;

public sealed class ProductPackagingTests
{
    [Fact]
    public void Product_entry_scripts_exist()
    {
        var root = FindRepositoryRoot();

        Assert.True(File.Exists(Path.Combine(root, "启动_POE_Studio.ps1")));
        Assert.True(File.Exists(Path.Combine(root, "启动_POE_Studio.bat")));
        Assert.True(File.Exists(Path.Combine(root, "发布_POE_Studio.ps1")));
        Assert.True(File.Exists(Path.Combine(root, "README.md")));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PoeStudio.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("Cannot find repository root.");
    }
}
