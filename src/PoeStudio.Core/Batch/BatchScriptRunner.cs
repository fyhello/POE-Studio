using PoeStudio.Contracts;

namespace PoeStudio.Core.Batch;

public sealed class BatchScriptRunner
{
    public BatchScriptRunResponse Run(
        string profileId,
        IReadOnlyList<BatchScriptOperationDto> operations,
        IReadOnlyList<(BatchScriptOperationDto Operation, ResourceSummaryDto Resource, string Text)> candidates,
        bool applied)
    {
        var changes = new List<BatchScriptChangeDto>();
        var warnings = new List<string>();
        var matched = 0;

        foreach (var (operation, resource, text) in candidates)
        {
            matched++;
            if (string.IsNullOrEmpty(operation.Find))
            {
                warnings.Add($"跳过空查找规则：{operation.Name}");
                continue;
            }

            if (!text.Contains(operation.Find, StringComparison.Ordinal))
            {
                continue;
            }

            var replaced = text.Replace(operation.Find, operation.Replace, StringComparison.Ordinal);
            changes.Add(new BatchScriptChangeDto(
                operation.Name,
                resource.VirtualPath,
                Preview(text),
                Preview(replaced)));
        }

        return new BatchScriptRunResponse(profileId, applied, matched, changes.Count, changes, warnings);
    }

    private static string Preview(string text)
    {
        const int limit = 160;
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return normalized.Length <= limit ? normalized : normalized[..limit];
    }
}
