using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Workspace;

namespace PoeStudio.Storage.Profiles;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string workspaceRoot;

    public ProfileStore(string workspaceRoot)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public async Task SaveAsync(ClientProfileDto profile, CancellationToken cancellationToken)
    {
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, profile.Id);
        layout.EnsureDirectories();
        var tempPath = $"{layout.ProfileJsonPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken);
            }

            File.Move(tempPath, layout.ProfileJsonPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async Task<IReadOnlyList<ClientProfileDto>> ListAsync(CancellationToken cancellationToken)
    {
        var profilesRoot = Path.Combine(workspaceRoot, "profiles");
        if (!Directory.Exists(profilesRoot))
        {
            return Array.Empty<ClientProfileDto>();
        }

        var items = new List<ClientProfileDto>();
        foreach (var profileRoot in Directory.EnumerateDirectories(profilesRoot))
        {
            var file = Path.Combine(profileRoot, "profile.json");
            if (!File.Exists(file))
            {
                continue;
            }

            await using var stream = File.OpenRead(file);
            var profile = await JsonSerializer.DeserializeAsync<ClientProfileDto>(stream, JsonOptions, cancellationToken);
            if (profile is not null)
            {
                items.Add(profile);
            }
        }

        return items.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<ClientProfileDto?> GetAsync(string profileId, CancellationToken cancellationToken)
    {
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, profileId);
        if (!File.Exists(layout.ProfileJsonPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(layout.ProfileJsonPath);
        return await JsonSerializer.DeserializeAsync<ClientProfileDto>(stream, JsonOptions, cancellationToken);
    }
}
