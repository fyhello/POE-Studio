namespace PoeStudio.Core.Agent;

public sealed class AgentRepositoryRootResolver
{
    private const string RepositoryRootEnvironmentVariable = "POE_STUDIO_REPOSITORY_ROOT";
    private const string SolutionFileName = "PoeStudio.sln";
    private static readonly string WorkflowDocumentPath = Path.Combine("docs", "agent", "poe-studio-project-workflows.md");

    private readonly string? _explicitRoot;

    public AgentRepositoryRootResolver(string? explicitRoot = null)
    {
        _explicitRoot = explicitRoot;
    }

    public string? Resolve()
    {
        return ResolveCore([], includeProcessDefaults: true);
    }

    public string? ResolveFromCandidates(params string?[] candidates)
    {
        return ResolveCore(candidates, includeProcessDefaults: false);
    }

    private string? ResolveCore(IReadOnlyList<string?> candidates, bool includeProcessDefaults)
    {
        foreach (var candidate in BuildCandidates(candidates))
        {
            var resolved = ResolveCandidate(candidate);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        if (includeProcessDefaults)
        {
            foreach (var candidate in ResolveProcessDefaults())
            {
                var resolved = ResolveCandidate(candidate);
                if (resolved is not null)
                {
                    return resolved;
                }
            }
        }

        return null;
    }

    private IEnumerable<string?> BuildCandidates(IReadOnlyList<string?> candidates)
    {
        yield return _explicitRoot;

        foreach (var candidate in candidates)
        {
            yield return candidate;
        }

        yield return Environment.GetEnvironmentVariable(RepositoryRootEnvironmentVariable);
    }

    private static IEnumerable<string?> ResolveProcessDefaults()
    {
        yield return Environment.CurrentDirectory;
        yield return AppContext.BaseDirectory;
    }

    private static string? ResolveCandidate(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(candidate);
        }
        catch (Exception) when (
            candidate.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
            candidate.Length == 0)
        {
            return null;
        }

        var directory = File.Exists(fullPath)
            ? Directory.GetParent(fullPath)
            : new DirectoryInfo(fullPath);

        while (directory is not null)
        {
            if (IsRepositoryRoot(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool IsRepositoryRoot(string directory)
    {
        return Directory.Exists(directory) &&
               File.Exists(Path.Combine(directory, SolutionFileName)) &&
               File.Exists(Path.Combine(directory, WorkflowDocumentPath));
    }
}
