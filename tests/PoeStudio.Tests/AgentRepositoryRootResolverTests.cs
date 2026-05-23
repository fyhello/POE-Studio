using PoeStudio.Core.Agent;

namespace PoeStudio.Tests;

public sealed class AgentRepositoryRootResolverTests
{
    [Fact]
    public void ResolveFromCandidates_identifies_repository_root()
    {
        using var repo = CreateRepositoryRoot();
        var resolver = new AgentRepositoryRootResolver();

        var resolved = resolver.ResolveFromCandidates(repo.Root);

        Assert.Equal(repo.Root, resolved);
    }

    [Fact]
    public void ResolveFromCandidates_walks_up_from_child_directory()
    {
        using var repo = CreateRepositoryRoot();
        var child = Directory.CreateDirectory(Path.Combine(repo.Root, "src", "PoeStudio.Core", "Agent"));
        var resolver = new AgentRepositoryRootResolver();

        var resolved = resolver.ResolveFromCandidates(child.FullName);

        Assert.Equal(repo.Root, resolved);
    }

    [Fact]
    public void ResolveFromCandidates_prefers_explicit_root_over_candidates()
    {
        using var explicitRepo = CreateRepositoryRoot();
        using var candidateRepo = CreateRepositoryRoot();
        var resolver = new AgentRepositoryRootResolver(explicitRepo.Root);

        var resolved = resolver.ResolveFromCandidates(candidateRepo.Root);

        Assert.Equal(explicitRepo.Root, resolved);
    }

    [Fact]
    public void ResolveFromCandidates_uses_working_directory_candidate_before_environment()
    {
        using var workingDirectoryRepo = CreateRepositoryRoot();
        using var environmentRepo = CreateRepositoryRoot();
        using var environment = TemporaryEnvironmentVariable.Set("POE_STUDIO_REPOSITORY_ROOT", environmentRepo.Root);
        var resolver = new AgentRepositoryRootResolver();

        var resolved = resolver.ResolveFromCandidates(workingDirectoryRepo.Root);

        Assert.Equal(workingDirectoryRepo.Root, resolved);
    }

    [Fact]
    public void Resolve_uses_environment_variable_as_fallback()
    {
        using var repo = CreateRepositoryRoot();
        using var environment = TemporaryEnvironmentVariable.Set("POE_STUDIO_REPOSITORY_ROOT", repo.Root);
        var resolver = new AgentRepositoryRootResolver();

        var resolved = resolver.Resolve();

        Assert.Equal(repo.Root, resolved);
    }

    [Fact]
    public void ResolveFromCandidates_returns_null_when_no_repository_root_exists()
    {
        using var missing = new TemporaryDirectory();
        var resolver = new AgentRepositoryRootResolver();

        var resolved = resolver.ResolveFromCandidates(missing.Root);

        Assert.Null(resolved);
    }

    private static TemporaryDirectory CreateRepositoryRoot()
    {
        var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Root, "PoeStudio.sln"), string.Empty);
        var agentDocs = Directory.CreateDirectory(Path.Combine(directory.Root, "docs", "agent"));
        File.WriteAllText(Path.Combine(agentDocs.FullName, "poe-studio-project-workflows.md"), "# Workflows");
        return directory;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "poe-studio-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class TemporaryEnvironmentVariable : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        private TemporaryEnvironmentVariable(string name, string value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public static TemporaryEnvironmentVariable Set(string name, string value) => new(name, value);

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }
}
