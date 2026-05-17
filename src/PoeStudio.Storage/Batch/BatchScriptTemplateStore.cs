using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Workspace;

namespace PoeStudio.Storage.Batch;

public sealed class BatchScriptTemplateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string workspaceRoot;

    public BatchScriptTemplateStore(string workspaceRoot)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public async Task<BatchScriptTemplateDto> SaveAsync(BatchScriptTemplateSaveRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("模板名称不能为空。", nameof(request));
        }

        if (request.Operations.Count == 0)
        {
            throw new ArgumentException("模板至少需要一条规则。", nameof(request));
        }

        var layout = WorkspaceLayout.ForProfile(workspaceRoot, request.ProfileId);
        layout.EnsureDirectories();
        var now = DateTimeOffset.UtcNow;
        var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : SafeTemplateId(request.Id);
        var existing = await GetAsync(request.ProfileId, id, cancellationToken);
        var item = new BatchScriptTemplateDto(
            id,
            request.ProfileId,
            request.Name.Trim(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            request.Operations,
            existing?.CreatedAt ?? now,
            now);
        var path = GetTemplatePath(request.ProfileId, id);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, item, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
        return item;
    }

    public async Task<BatchScriptTemplateListResponse> ListAsync(BatchScriptTemplateListRequest request, CancellationToken cancellationToken)
    {
        var root = WorkspaceLayout.ForProfile(workspaceRoot, request.ProfileId).BatchScriptRoot;
        if (!Directory.Exists(root))
        {
            return new BatchScriptTemplateListResponse(request.ProfileId, []);
        }

        var items = new List<BatchScriptTemplateDto>();
        foreach (var path in Directory.EnumerateFiles(root, "*.json"))
        {
            await using var stream = File.OpenRead(path);
            var item = await JsonSerializer.DeserializeAsync<BatchScriptTemplateDto>(stream, JsonOptions, cancellationToken);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return new BatchScriptTemplateListResponse(
            request.ProfileId,
            items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<BatchScriptTemplateDto?> GetAsync(string profileId, string templateId, CancellationToken cancellationToken)
    {
        var path = GetTemplatePath(profileId, SafeTemplateId(templateId));
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<BatchScriptTemplateDto>(stream, JsonOptions, cancellationToken);
    }

    public Task<BatchScriptTemplateDeleteResponse> DeleteAsync(BatchScriptTemplateDeleteRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var id = SafeTemplateId(request.TemplateId);
        var path = GetTemplatePath(request.ProfileId, id);
        var removed = File.Exists(path);
        if (removed)
        {
            File.Delete(path);
        }

        return Task.FromResult(new BatchScriptTemplateDeleteResponse(request.ProfileId, id, removed));
    }

    private string GetTemplatePath(string profileId, string templateId)
    {
        var layout = WorkspaceLayout.ForProfile(workspaceRoot, profileId);
        Directory.CreateDirectory(layout.BatchScriptRoot);
        return Path.Combine(layout.BatchScriptRoot, $"{SafeTemplateId(templateId)}.json");
    }

    private static string SafeTemplateId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')))
        {
            throw new ArgumentException("模板编号不合法。", nameof(value));
        }

        return value;
    }
}
