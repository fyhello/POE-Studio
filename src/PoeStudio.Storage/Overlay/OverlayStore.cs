using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Overlay;
using PoeStudio.Core.Patching;
using PoeStudio.Core.Resources;
using PoeStudio.Core.Workspace;

namespace PoeStudio.Storage.Overlay;

public sealed class OverlayStore : IPatchOverlayReader, IPatchOverlayWriter, IOverlayReviewReader
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions JsonLineOptions = new(JsonSerializerDefaults.Web);

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

        await File.WriteAllBytesAsync(overlayPath, EncodeText(request.Text, request.TextEncoding ?? DetectBaseTextEncoding(request.BasePhysicalPath)), cancellationToken);
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
        await AppendAuditAsync(layout, new OverlayAuditEventDto("save", normalized, entry.OverlayHash, entry.OverlaySize, now), cancellationToken);
        return entry;
    }

    public async Task<OverlayListResponse> ListAsync(string profileId, CancellationToken cancellationToken)
    {
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, profileId);
        var manifest = await LoadManifestAsync(layout, cancellationToken);
        var refreshed = await RefreshManifestEntriesAsync(layout, manifest, cancellationToken);
        var items = refreshed.Items.OrderBy(item => item.NormalizedPath, StringComparer.OrdinalIgnoreCase).ToArray();
        return new OverlayListResponse(profileId, items.Length, layout.OverlayFilesRoot, GetManifestPath(layout), items);
    }

    public async Task<OverlaySyncExternalResponse> SyncExternalAsync(OverlaySyncExternalRequest request, CancellationToken cancellationToken)
    {
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, request.ProfileId);
        layout.EnsureDirectories();

        var listPath = Path.Combine(layout.OverlayRoot, "files.txt");
        var useList = File.Exists(listPath);
        var mode = useList ? "files.txt" : "scan";
        var warnings = new List<string>();
        var candidates = useList
            ? await ReadExternalOverlayListAsync(listPath, cancellationToken)
            : ScanExternalOverlayFiles(layout);

        var imported = new List<OverlayEntryDto>();
        var now = DateTimeOffset.UtcNow;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string normalized;
            try
            {
                normalized = ResourcePath.Normalize(candidate);
            }
            catch (ArgumentException ex)
            {
                warnings.Add($"{candidate}: {ex.Message}");
                continue;
            }

            var overlayPath = ResourcePath.ToSafePhysicalPath(layout.OverlayFilesRoot, normalized);
            if (!File.Exists(overlayPath))
            {
                warnings.Add($"{normalized}: overlay 文件不存在。");
                continue;
            }

            imported.Add(new OverlayEntryDto(
                request.ProfileId,
                normalized,
                normalized,
                overlayPath,
                new FileInfo(overlayPath).Length,
                await HashFileAsync(overlayPath, cancellationToken),
                BaseHash: null,
                BaseSize: null,
                CreatedAt: now,
                UpdatedAt: now));
        }

        var ordered = imported
            .GroupBy(item => item.NormalizedPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(item => item.NormalizedPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        await SaveManifestAsync(layout, new OverlayManifest(ordered), cancellationToken);
        await AppendAuditAsync(layout, new OverlayAuditEventDto("sync-external", "*", null, ordered.Count, now), cancellationToken);

        return new OverlaySyncExternalResponse(
            request.ProfileId,
            mode,
            candidates.Count,
            ordered.Count,
            candidates.Count - ordered.Count,
            layout.OverlayFilesRoot,
            GetManifestPath(layout),
            ordered,
            warnings);
    }

    public async Task<IReadOnlyList<OverlayEntryDto>> GetEntriesAsync(string profileId, CancellationToken cancellationToken)
    {
        var list = await ListAsync(profileId, cancellationToken);
        return list.Items;
    }

    private async Task<OverlayManifest> RefreshManifestEntriesAsync(
        WorkspaceLayout layout,
        OverlayManifest manifest,
        CancellationToken cancellationToken)
    {
        var changed = false;
        var refreshed = new List<OverlayEntryDto>(manifest.Items.Count);
        foreach (var item in manifest.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(item.OverlayPath))
            {
                refreshed.Add(item);
                continue;
            }

            var size = new FileInfo(item.OverlayPath).Length;
            var hash = await HashFileAsync(item.OverlayPath, cancellationToken);
            if (size == item.OverlaySize && string.Equals(hash, item.OverlayHash, StringComparison.OrdinalIgnoreCase))
            {
                refreshed.Add(item);
                continue;
            }

            refreshed.Add(item with
            {
                OverlaySize = size,
                OverlayHash = hash,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            changed = true;
        }

        if (!changed)
        {
            return manifest;
        }

        var updated = new OverlayManifest(refreshed);
        await SaveManifestAsync(layout, updated, cancellationToken);
        return updated;
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
        await AppendAuditAsync(layout, new OverlayAuditEventDto("revert", normalized, entry.OverlayHash, entry.OverlaySize, DateTimeOffset.UtcNow), cancellationToken);
        return new RevertOverlayResponse(request.ProfileId, normalized, Removed: true);
    }

    public async Task<OverlayAuditResponse> AuditAsync(OverlayAuditRequest request, CancellationToken cancellationToken)
    {
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, request.ProfileId);
        var path = GetAuditPath(layout);
        if (!File.Exists(path))
        {
            return new OverlayAuditResponse(request.ProfileId, 0, []);
        }

        var events = new List<OverlayAuditEventDto>();
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<OverlayAuditEventDto>(line, JsonLineOptions);
            if (item is not null)
            {
                events.Add(item);
            }
        }

        var ordered = events
            .OrderByDescending(item => item.At)
            .Take(Math.Clamp(request.Take, 1, 500))
            .ToArray();
        return new OverlayAuditResponse(request.ProfileId, events.Count, ordered);
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

    private static byte[] EncodeText(string text, string? encodingName)
    {
        return NormalizeEncodingName(encodingName) switch
        {
            "utf-16le-bom" => Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray(),
            "utf-16le" => Encoding.Unicode.GetBytes(text),
            "utf-8-bom" => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray(),
            _ => Utf8NoBom.GetBytes(text)
        };
    }

    private static string? DetectBaseTextEncoding(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var sample = File.ReadAllBytes(path).AsSpan(0, (int)Math.Min(new FileInfo(path).Length, 512));
        return DetectTextEncoding(sample);
    }

    public static string? DetectTextEncoding(ReadOnlySpan<byte> sample)
    {
        if (sample.Length >= 2 && sample[0] == 0xff && sample[1] == 0xfe)
        {
            return "utf-16le-bom";
        }

        if (sample.Length >= 3 && sample[0] == 0xef && sample[1] == 0xbb && sample[2] == 0xbf)
        {
            return "utf-8-bom";
        }

        if (sample.Length >= 8)
        {
            var pairs = Math.Min(sample.Length / 2, 256);
            var zeroHigh = 0;
            var useful = 0;
            for (var index = 0; index < pairs; index++)
            {
                var low = sample[index * 2];
                var high = sample[index * 2 + 1];
                if (high == 0 && (low is >= 9 and <= 126 || low is 10 or 13))
                {
                    zeroHigh++;
                    useful++;
                }
            }

            if (useful >= pairs * 0.75 && zeroHigh >= Math.Min(4, pairs / 4))
            {
                return "utf-16le";
            }
        }

        return null;
    }

    private static string? NormalizeEncodingName(string? encodingName)
    {
        return encodingName?.Trim().ToLowerInvariant() switch
        {
            "utf-16le-bom" or "utf-16 bom" or "unicode bom" => "utf-16le-bom",
            "utf-16le" or "utf-16" or "unicode" => "utf-16le",
            "utf-8-bom" or "utf8-bom" => "utf-8-bom",
            "utf-8" or "utf8" => "utf-8",
            _ => null
        };
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<IReadOnlyList<string>> ReadExternalOverlayListAsync(string listPath, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(listPath, Utf8NoBom, cancellationToken);
        return lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();
    }

    private static IReadOnlyList<string> ScanExternalOverlayFiles(WorkspaceLayout layout)
    {
        if (!Directory.Exists(layout.OverlayFilesRoot))
        {
            return [];
        }

        var root = Path.GetFullPath(layout.OverlayFilesRoot);
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/'))
            .Where(path => !string.Equals(path, "manifest.json", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(path, "files.txt", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    private static string GetAuditPath(WorkspaceLayout layout)
    {
        return Path.Combine(layout.AuditRoot, "overlay-events.jsonl");
    }

    private static async Task AppendAuditAsync(WorkspaceLayout layout, OverlayAuditEventDto item, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(layout.AuditRoot);
        await File.AppendAllTextAsync(GetAuditPath(layout), JsonSerializer.Serialize(item, JsonLineOptions) + Environment.NewLine, cancellationToken);
    }

    private sealed record OverlayManifest(List<OverlayEntryDto> Items);
}
