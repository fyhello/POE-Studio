using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Resources;
using PoeStudio.Core.Workspace;

namespace PoeStudio.Storage.Tables;

public sealed class TableSchemaStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string workspaceRoot;

    public TableSchemaStore(string workspaceRoot)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public async Task<TableSchemaEntryDto> SaveAsync(TableSchemaSaveRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("结构名称不能为空。", nameof(request));
        }

        var layout = WorkspaceLayout.ForProfile(workspaceRoot, request.ProfileId);
        layout.EnsureDirectories();
        var now = DateTimeOffset.UtcNow;
        var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : SafeSchemaId(request.Id);
        var existing = await GetAsync(request.ProfileId, id, cancellationToken);
        var entry = new TableSchemaEntryDto(
            id,
            request.ProfileId,
            ResourcePath.Normalize(request.VirtualPath),
            request.Name.Trim(),
            string.IsNullOrWhiteSpace(request.MatchPattern) ? null : request.MatchPattern.Trim(),
            request.Schema,
            existing?.CreatedAt ?? now,
            now);

        var path = GetSchemaPath(request.ProfileId, id);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
        return entry;
    }

    public async Task<TableSchemaListResponse> ListAsync(TableSchemaListRequest request, CancellationToken cancellationToken)
    {
        var root = WorkspaceLayout.ForProfile(workspaceRoot, request.ProfileId).TableSchemaRoot;
        if (!Directory.Exists(root))
        {
            return new TableSchemaListResponse(request.ProfileId, []);
        }

        var normalizedPath = string.IsNullOrWhiteSpace(request.VirtualPath)
            ? null
            : ResourcePath.Normalize(request.VirtualPath);
        var items = new List<TableSchemaEntryDto>();
        foreach (var path in Directory.EnumerateFiles(root, "*.json"))
        {
            await using var stream = File.OpenRead(path);
            var entry = await JsonSerializer.DeserializeAsync<TableSchemaEntryDto>(stream, JsonOptions, cancellationToken);
            if (entry is null)
            {
                continue;
            }

            if (normalizedPath is null || Matches(entry, normalizedPath))
            {
                items.Add(entry);
            }
        }

        return new TableSchemaListResponse(
            request.ProfileId,
            items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<TableSchemaEntryDto?> GetAsync(string profileId, string schemaId, CancellationToken cancellationToken)
    {
        var id = SafeSchemaId(schemaId);
        var path = GetSchemaPath(profileId, id);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<TableSchemaEntryDto>(stream, JsonOptions, cancellationToken);
    }

    public Task<TableSchemaDeleteResponse> DeleteAsync(TableSchemaDeleteRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var id = SafeSchemaId(request.SchemaId);
        var path = GetSchemaPath(request.ProfileId, id);
        var removed = File.Exists(path);
        if (removed)
        {
            File.Delete(path);
        }

        return Task.FromResult(new TableSchemaDeleteResponse(request.ProfileId, id, removed));
    }

    private string GetSchemaPath(string profileId, string schemaId)
    {
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, profileId);
        Directory.CreateDirectory(layout.TableSchemaRoot);
        return Path.Combine(layout.TableSchemaRoot, $"{SafeSchemaId(schemaId)}.json");
    }

    private static bool Matches(TableSchemaEntryDto entry, string normalizedPath)
    {
        if (string.Equals(entry.VirtualPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(entry.MatchPattern)
            && normalizedPath.Contains(entry.MatchPattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeSchemaId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')))
        {
            throw new ArgumentException("结构编号不合法。", nameof(value));
        }

        return value;
    }
}
