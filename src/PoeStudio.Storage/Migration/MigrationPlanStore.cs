using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Workspace;

namespace PoeStudio.Storage.Migration;

public sealed class MigrationPlanStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string workspaceRoot;

    public MigrationPlanStore(string workspaceRoot)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public async Task<ResourceMigrationPlanEntryDto> SaveAsync(ResourceMigrationPlanSaveRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("迁移方案名称不能为空。", nameof(request));
        }

        if (request.Items.Count == 0)
        {
            throw new ArgumentException("迁移方案至少需要一条资源。", nameof(request));
        }

        var sourceProfileId = request.Criteria.SourceProfileId;
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, sourceProfileId);
        layout.EnsureDirectories();
        var now = DateTimeOffset.UtcNow;
        var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : SafePlanId(request.Id);
        var existing = await GetAsync(sourceProfileId, id, cancellationToken);
        var entry = new ResourceMigrationPlanEntryDto(
            id,
            request.Name.Trim(),
            request.Criteria with
            {
                Take = Math.Clamp(request.Criteria.Take, 1, 500),
                Extension = string.IsNullOrWhiteSpace(request.Criteria.Extension) ? null : request.Criteria.Extension.Trim()
            },
            request.Items.Count,
            request.Items.GroupBy(item => item.Status).ToDictionary(group => group.Key, group => group.Count()),
            request.Items.GroupBy(item => item.RiskLevel).ToDictionary(group => group.Key, group => group.Count()),
            request.Items,
            existing?.CreatedAt ?? now,
            now);

        var path = GetPlanPath(sourceProfileId, id);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
        return entry;
    }

    public async Task<ResourceMigrationPlanListResponse> ListAsync(ResourceMigrationPlanListRequest request, CancellationToken cancellationToken)
    {
        var root = WorkspaceLayout.ForProfile(workspaceRoot, request.SourceProfileId).MigrationPlanRoot;
        if (!Directory.Exists(root))
        {
            return new ResourceMigrationPlanListResponse(request.SourceProfileId, []);
        }

        var items = new List<ResourceMigrationPlanEntryDto>();
        foreach (var path in Directory.EnumerateFiles(root, "*.json"))
        {
            await using var stream = File.OpenRead(path);
            var item = await JsonSerializer.DeserializeAsync<ResourceMigrationPlanEntryDto>(stream, JsonOptions, cancellationToken);
            if (item is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(request.TargetProfileId)
                || string.Equals(item.Criteria.TargetProfileId, request.TargetProfileId, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(item);
            }
        }

        return new ResourceMigrationPlanListResponse(
            request.SourceProfileId,
            items.OrderByDescending(item => item.UpdatedAt).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<ResourceMigrationPlanEntryDto?> GetAsync(string sourceProfileId, string planId, CancellationToken cancellationToken)
    {
        var id = SafePlanId(planId);
        var path = GetPlanPath(sourceProfileId, id);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ResourceMigrationPlanEntryDto>(stream, JsonOptions, cancellationToken);
    }

    public Task<ResourceMigrationPlanDeleteResponse> DeleteAsync(ResourceMigrationPlanDeleteRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var id = SafePlanId(request.PlanId);
        var path = GetPlanPath(request.SourceProfileId, id);
        var removed = File.Exists(path);
        if (removed)
        {
            File.Delete(path);
        }

        return Task.FromResult(new ResourceMigrationPlanDeleteResponse(request.SourceProfileId, id, removed));
    }

    private string GetPlanPath(string sourceProfileId, string planId)
    {
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, sourceProfileId);
        Directory.CreateDirectory(layout.MigrationPlanRoot);
        return Path.Combine(layout.MigrationPlanRoot, $"{SafePlanId(planId)}.json");
    }

    private static string SafePlanId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')))
        {
            throw new ArgumentException("迁移方案编号不合法。", nameof(value));
        }

        return value;
    }
}
