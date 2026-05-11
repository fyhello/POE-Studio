namespace PoeStudio.Core.Workspace;

using System.Text.RegularExpressions;

public sealed record WorkspaceLayout(
    string WorkspaceRoot,
    string ProfileId,
    string ProfileRoot,
    string ProfileJsonPath,
    string CacheRoot,
    string RawCacheRoot,
    string PreviewCacheRoot,
    string OverlayRoot,
    string OverlayFilesRoot,
    string BuildsRoot,
    string AuditRoot)
{
    private static readonly Regex ProfileIdPattern = new("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant);

    public static WorkspaceLayout ForProfile(string workspaceRoot, string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId)
            || profileId is "." or ".."
            || Path.IsPathRooted(profileId)
            || profileId.Contains(Path.DirectorySeparatorChar)
            || profileId.Contains(Path.AltDirectorySeparatorChar)
            || !ProfileIdPattern.IsMatch(profileId))
        {
            throw new ArgumentException("Profile id must be a safe path segment.", nameof(profileId));
        }

        var root = Path.GetFullPath(workspaceRoot);
        var profileRoot = Path.Combine(root, "profiles", profileId);
        return new WorkspaceLayout(
            WorkspaceRoot: root,
            ProfileId: profileId,
            ProfileRoot: profileRoot,
            ProfileJsonPath: Path.Combine(profileRoot, "profile.json"),
            CacheRoot: Path.Combine(profileRoot, "cache"),
            RawCacheRoot: Path.Combine(profileRoot, "cache", "raw"),
            PreviewCacheRoot: Path.Combine(profileRoot, "cache", "preview"),
            OverlayRoot: Path.Combine(profileRoot, "overlay"),
            OverlayFilesRoot: Path.Combine(profileRoot, "overlay", "files"),
            BuildsRoot: Path.Combine(profileRoot, "builds"),
            AuditRoot: Path.Combine(profileRoot, "audit"));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ProfileRoot);
        Directory.CreateDirectory(CacheRoot);
        Directory.CreateDirectory(RawCacheRoot);
        Directory.CreateDirectory(PreviewCacheRoot);
        Directory.CreateDirectory(OverlayRoot);
        Directory.CreateDirectory(OverlayFilesRoot);
        Directory.CreateDirectory(BuildsRoot);
        Directory.CreateDirectory(AuditRoot);
    }
}
