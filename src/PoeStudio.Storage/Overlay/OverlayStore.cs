using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Patching;
using PoeStudio.Core.Resources;
using PoeStudio.Core.Workspace;

namespace PoeStudio.Storage.Overlay;

public sealed class OverlayStore : IPatchOverlayReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string workspaceRoot;

    public OverlayStore(string workspaceRoot)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public async Task<OverlayEntryDto> SaveTextAsync(SaveTextOverlayRequest request, CancellationToken cancellationToken)
    {
        var normalized = ResourcePath.Normalize(request.VirtualPath);
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, request.ProfileId);
        layout.EnsureDirectories();
        var overlayPath = ResourcePath.ToSafePhysicalPath(layout.OverlayFilesRoot, normalized);
        Directory.CreateDirectory(Path.GetDirectoryName(overlayPath)!);

        await File.WriteAllTextAsync(overlayPath, request.Text, Encoding.UTF8, cancellationToken);
        return await UpsertManifestAsync(
            layout,
            request.ProfileId,
            normalized,
            overlayPath,
            request.BasePhysicalPath,
            request.HasBasePhysicalPath,
            cancellationToken);
    }

    public async Task<OverlayEntryDto> SaveBytesAsync(
        string profileId,
        string virtualPath,
        byte[] content,
        string? BasePhysicalPath,
        bool HasBasePhysicalPath,
        CancellationToken cancellationToken)
    {
        var normalized = ResourcePath.Normalize(virtualPath);
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, profileId);
        layout.EnsureDirectories();
        var overlayPath = ResourcePath.ToSafePhysicalPath(layout.OverlayFilesRoot, normalized);
        Directory.CreateDirectory(Path.GetDirectoryName(overlayPath)!);

        await File.WriteAllBytesAsync(overlayPath, content, cancellationToken);
        return await UpsertManifestAsync(
            layout,
            profileId,
            normalized,
            overlayPath,
            BasePhysicalPath,
            HasBasePhysicalPath,
            cancellationToken);
    }

    private async Task<OverlayEntryDto> UpsertManifestAsync(
        WorkspaceLayout layout,
        string profileId,
        string normalized,
        string overlayPath,
        string? basePhysicalPath,
        bool hasBasePhysicalPath,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var manifest = await LoadManifestAsync(layout, cancellationToken);
        var existing = manifest.Items.FirstOrDefault(item => string.Equals(item.NormalizedPath, normalized, StringComparison.OrdinalIgnoreCase));
        var entry = new OverlayEntryDto(
            profileId,
            normalized,
            normalized,
            overlayPath,
            new FileInfo(overlayPath).Length,
            await HashFileAsync(overlayPath, cancellationToken),
            hasBasePhysicalPath ? await TryHashFileAsync(basePhysicalPath, cancellationToken) : existing?.BaseHash,
            hasBasePhysicalPath ? GetFileSize(basePhysicalPath) : existing?.BaseSize,
            existing?.CreatedAt ?? now,
            now);

        manifest.Items.RemoveAll(item => string.Equals(item.NormalizedPath, normalized, StringComparison.OrdinalIgnoreCase));
        manifest.Items.Add(entry);
        await SaveManifestAsync(layout, manifest, cancellationToken);
        return entry;
    }

    public async Task<OverlayListResponse> ListAsync(string profileId, CancellationToken cancellationToken)
    {
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, profileId);
        var manifest = await LoadManifestAsync(layout, cancellationToken);
        var items = manifest.Items.OrderBy(item => item.NormalizedPath, StringComparer.OrdinalIgnoreCase).ToArray();
        return new OverlayListResponse(profileId, items.Length, items);
    }

    public async Task<IReadOnlyList<OverlayEntryDto>> GetEntriesAsync(string profileId, CancellationToken cancellationToken)
    {
        var list = await ListAsync(profileId, cancellationToken);
        return list.Items;
    }

    public async Task<OverlayDiffResponse> DiffAsync(OverlayDiffRequest request, CancellationToken cancellationToken)
    {
        var normalized = ResourcePath.Normalize(request.VirtualPath);
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, request.ProfileId);
        var manifest = await LoadManifestAsync(layout, cancellationToken);
        var entry = manifest.Items.FirstOrDefault(item => string.Equals(item.NormalizedPath, normalized, StringComparison.OrdinalIgnoreCase));
        if (entry is null || !File.Exists(entry.OverlayPath))
        {
            return new OverlayDiffResponse(request.ProfileId, normalized, Exists: false, null, null, null, null, TextChanged: false, "未找到 overlay 修改。");
        }

        var overlayHash = await HashFileAsync(entry.OverlayPath, cancellationToken);
        var overlaySize = new FileInfo(entry.OverlayPath).Length;
        return new OverlayDiffResponse(
            request.ProfileId,
            normalized,
            Exists: true,
            BaseSize: entry.BaseSize,
            OverlaySize: overlaySize,
            BaseHash: entry.BaseHash,
            OverlayHash: overlayHash,
            TextChanged: !string.Equals(entry.BaseHash, overlayHash, StringComparison.OrdinalIgnoreCase),
            Message: null);
    }

    public async Task<RevertOverlayResponse> RevertAsync(RevertOverlayRequest request, CancellationToken cancellationToken)
    {
        var normalized = ResourcePath.Normalize(request.VirtualPath);
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, request.ProfileId);
        var manifest = await LoadManifestAsync(layout, cancellationToken);
        var entry = manifest.Items.FirstOrDefault(item => string.Equals(item.NormalizedPath, normalized, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return new RevertOverlayResponse(request.ProfileId, normalized, Removed: false);
        }

        if (File.Exists(entry.OverlayPath))
        {
            File.Delete(entry.OverlayPath);
        }

        manifest.Items.RemoveAll(item => string.Equals(item.NormalizedPath, normalized, StringComparison.OrdinalIgnoreCase));
        await SaveManifestAsync(layout, manifest, cancellationToken);
        return new RevertOverlayResponse(request.ProfileId, normalized, Removed: true);
    }

    private static async Task<string?> TryHashFileAsync(string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        return await HashFileAsync(path, cancellationToken);
    }

    private static long? GetFileSize(string? path)
    {
        return string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : new FileInfo(path).Length;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<OverlayManifest> LoadManifestAsync(WorkspaceLayout layout, CancellationToken cancellationToken)
    {
        var path = GetManifestPath(layout);
        if (!File.Exists(path))
        {
            return new OverlayManifest([]);
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<OverlayManifest>(stream, JsonOptions, cancellationToken) ?? new OverlayManifest([]);
    }

    private static async Task SaveManifestAsync(WorkspaceLayout layout, OverlayManifest manifest, CancellationToken cancellationToken)
    {
        var path = GetManifestPath(layout);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string GetManifestPath(WorkspaceLayout layout)
    {
        return Path.Combine(layout.OverlayRoot, "manifest.json");
    }

    private sealed record OverlayManifest(List<OverlayEntryDto> Items);
}
