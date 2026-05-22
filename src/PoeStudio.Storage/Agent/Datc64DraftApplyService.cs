using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;
using PoeStudio.Core.Tables;
using PoeStudio.Storage.Overlay;

namespace PoeStudio.Storage.Agent;

public sealed class Datc64DraftApplyService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AgentStore _store;
    private readonly OverlayStore _overlay;
    private readonly Func<string, string, CancellationToken, Task<Datc64DraftResourceReadResult>> _readResourceAsync;

    public Datc64DraftApplyService(
        AgentStore store,
        OverlayStore overlay,
        Func<string, string, CancellationToken, Task<Datc64DraftResourceReadResult>> readResourceAsync)
    {
        _store = store;
        _overlay = overlay;
        _readResourceAsync = readResourceAsync;
    }

    public async Task<Datc64DraftApplyResult> ApplyAsync(
        string threadId,
        string runId,
        string approvalId,
        CancellationToken cancellationToken)
    {
        var approvals = await _store.ListApprovalsAsync(threadId, runId, cancellationToken);
        var approval = approvals.FirstOrDefault(x => string.Equals(x.Id, approvalId, StringComparison.Ordinal));
        if (approval is null)
        {
            return Datc64DraftApplyResult.Fail("approval_not_found");
        }

        if (approval.Status != AgentApprovalStatus.Approved)
        {
            return Datc64DraftApplyResult.Fail("approval_not_approved");
        }

        var proposal = JsonSerializer.Deserialize<Datc64TranslationDraftProposal>(approval.ProposalJson, JsonOptions);
        if (proposal is null)
        {
            return Datc64DraftApplyResult.Fail("proposal_invalid");
        }

        var read = await _readResourceAsync(proposal.ProfileId, proposal.ResourcePath, cancellationToken);
        var inspector = new TableInspector();
        var inspect = inspector.Inspect(read.Resource, read.Content, 4096);
        var warnings = new List<string>();
        var edits = new List<TableCellEditDto>();
        foreach (var candidate in proposal.Candidates)
        {
            if (candidate.RowIndex < 0 || candidate.RowIndex >= inspect.Rows.Count)
            {
                return Datc64DraftApplyResult.Fail("locator_not_found");
            }

            var row = inspect.Rows[candidate.RowIndex];
            if (candidate.ColumnIndex < 0 || candidate.ColumnIndex >= row.Cells.Count)
            {
                return Datc64DraftApplyResult.Fail("locator_not_found");
            }

            if (string.Equals(row.Cells[candidate.ColumnIndex], candidate.TranslatedText, StringComparison.Ordinal))
            {
                warnings.Add("translatedText matches source cell");
            }

            edits.Add(new TableCellEditDto(candidate.RowIndex + 1, candidate.ColumnIndex, candidate.TranslatedText));
        }

        var edited = inspector.ApplyDatc64CatalogCellEdits(read.Resource, read.Content, edits);
        var entry = await _overlay.SaveBytesAsync(
            proposal.ProfileId,
            proposal.ResourcePath,
            edited,
            read.BasePhysicalPath,
            read.HasBasePhysicalPath,
            cancellationToken);
        var updated = await _store.TryUpdateApprovalStatusAsync(
            threadId,
            runId,
            approvalId,
            AgentApprovalStatus.Approved,
            AgentApprovalStatus.Applied,
            entry.NormalizedPath,
            cancellationToken);
        if (!updated)
        {
            return Datc64DraftApplyResult.Fail("approval_update_failed");
        }

        await _store.AppendEventAsync(
            threadId,
            runId,
            AgentEventType.OverlayDraftWritten,
            $"Overlay draft written: {entry.NormalizedPath}",
            JsonSerializer.Serialize(entry, JsonOptions),
            cancellationToken);
        return new Datc64DraftApplyResult(true, null, entry.NormalizedPath, warnings);
    }
}

public sealed record Datc64DraftResourceReadResult(
    ResourceSummaryDto Resource,
    byte[] Content,
    string? BasePhysicalPath,
    bool HasBasePhysicalPath);

public sealed record Datc64DraftApplyResult(
    bool Applied,
    string? ErrorCode,
    string? AppliedOverlayPath,
    IReadOnlyList<string> Warnings)
{
    public static Datc64DraftApplyResult Fail(string errorCode)
    {
        return new Datc64DraftApplyResult(false, errorCode, null, []);
    }
}
