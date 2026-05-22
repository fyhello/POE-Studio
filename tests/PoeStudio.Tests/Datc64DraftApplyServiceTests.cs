using System.Text;
using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Agent;
using PoeStudio.Storage.Agent;
using PoeStudio.Storage.Overlay;

namespace PoeStudio.Tests;

public sealed class Datc64DraftApplyServiceTests
{
    [Fact]
    public async Task ApplyAsync_rejects_pending_approval_without_writing_overlay()
    {
        var fixture = await CreateFixtureAsync();
        var service = new Datc64DraftApplyService(fixture.Store, fixture.Overlay, fixture.ReadResourceAsync);

        var result = await service.ApplyAsync(fixture.ThreadId, fixture.RunId, fixture.Approval.Id, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal("approval_not_approved", result.ErrorCode);
        Assert.Empty((await fixture.Overlay.ListAsync(fixture.ProfileId, CancellationToken.None)).Items);
    }

    [Fact]
    public async Task ApplyAsync_writes_overlay_after_approval()
    {
        var fixture = await CreateFixtureAsync();
        await fixture.Store.TryUpdateApprovalStatusAsync(
            fixture.ThreadId,
            fixture.RunId,
            fixture.Approval.Id,
            AgentApprovalStatus.Pending,
            AgentApprovalStatus.Approved,
            null,
            CancellationToken.None);
        var service = new Datc64DraftApplyService(fixture.Store, fixture.Overlay, fixture.ReadResourceAsync);

        var result = await service.ApplyAsync(fixture.ThreadId, fixture.RunId, fixture.Approval.Id, CancellationToken.None);

        Assert.True(result.Applied);
        var entry = Assert.Single((await fixture.Overlay.ListAsync(fixture.ProfileId, CancellationToken.None)).Items);
        Assert.Equal("data/balance/traditional chinese/combatuiprompts.datc64", entry.NormalizedPath);
        var approvals = await fixture.Store.ListApprovalsAsync(fixture.ThreadId, fixture.RunId, CancellationToken.None);
        Assert.Equal(AgentApprovalStatus.Applied, approvals[0].Status);
    }

    [Fact]
    public async Task ApplyAsync_returns_locator_not_found_without_writing_overlay()
    {
        var fixture = await CreateFixtureAsync(rowIndex: 10);
        await fixture.Store.TryUpdateApprovalStatusAsync(
            fixture.ThreadId,
            fixture.RunId,
            fixture.Approval.Id,
            AgentApprovalStatus.Pending,
            AgentApprovalStatus.Approved,
            null,
            CancellationToken.None);
        var service = new Datc64DraftApplyService(fixture.Store, fixture.Overlay, fixture.ReadResourceAsync);

        var result = await service.ApplyAsync(fixture.ThreadId, fixture.RunId, fixture.Approval.Id, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal("locator_not_found", result.ErrorCode);
        Assert.Empty((await fixture.Overlay.ListAsync(fixture.ProfileId, CancellationToken.None)).Items);
    }

    [Fact]
    public async Task ApplyAsync_allows_same_text_candidate_with_warning()
    {
        var fixture = await CreateFixtureAsync(translatedText: "法力不足");
        await fixture.Store.TryUpdateApprovalStatusAsync(
            fixture.ThreadId,
            fixture.RunId,
            fixture.Approval.Id,
            AgentApprovalStatus.Pending,
            AgentApprovalStatus.Approved,
            null,
            CancellationToken.None);
        var service = new Datc64DraftApplyService(fixture.Store, fixture.Overlay, fixture.ReadResourceAsync);

        var result = await service.ApplyAsync(fixture.ThreadId, fixture.RunId, fixture.Approval.Id, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Contains("translatedText matches source cell", result.Warnings);
        Assert.Single((await fixture.Overlay.ListAsync(fixture.ProfileId, CancellationToken.None)).Items);
    }

    private static async Task<Fixture> CreateFixtureAsync(int rowIndex = 0, string translatedText = "魔力不足")
    {
        var workspace = Path.Combine(Path.GetTempPath(), "poe-studio-datc64-apply-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var profileId = "profile-1";
        var resourcePath = "data/balance/traditional chinese/combatuiprompts.datc64";
        var baseBytes = BuildDatc64PointerTableData([
            ("NoMana", "法力不足"),
            ("OnCooldown", "冷却中")
        ]);
        var basePath = Path.Combine(workspace, "base.datc64");
        await File.WriteAllBytesAsync(basePath, baseBytes);
        var store = new AgentStore(workspace);
        var overlay = new OverlayStore(workspace);
        var thread = await store.SaveNewThreadAsync(profileId, "DATC64", "Translate", "datc64-translation", CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var run = new AgentRunDto(
            "run-1",
            thread.Id,
            profileId,
            "Translate",
            "datc64-translation",
            AgentRunStatus.WaitingForApproval,
            90,
            "Waiting",
            now,
            now,
            0,
            null,
            null,
            null);
        await store.SaveRunAsync(run, CancellationToken.None);
        var proposal = new Datc64TranslationDraftProposal(
            "datc64-translation",
            profileId,
            resourcePath,
            [
                new Datc64TranslationCandidate(
                    $"row:{rowIndex + 1};column:3;name:text_3 @12",
                    rowIndex,
                    3,
                    "法力不足",
                    translatedText,
                    0.9,
                    null)
            ]);
        var approval = new AgentApprovalDto(
            "approval-1",
            run.Id,
            profileId,
            "datc64-translation",
            AgentApprovalStatus.Pending,
            "One candidate",
            JsonSerializer.Serialize(proposal, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            now,
            now,
            null);
        await store.SaveApprovalsAsync(thread.Id, run.Id, [approval], CancellationToken.None);

        return new Fixture(
            store,
            overlay,
            profileId,
            thread.Id,
            run.Id,
            approval,
            (_, _, cancellationToken) => Task.FromResult(new Datc64DraftResourceReadResult(
                new ResourceSummaryDto(
                    Guid.NewGuid().ToString("N"),
                    profileId,
                    resourcePath,
                    resourcePath,
                    ".datc64",
                    ResourceKind.Table,
                    baseBytes.Length,
                    basePath,
                    ResourceSourceLayer.Base,
                    DateTimeOffset.UtcNow),
                baseBytes,
                basePath,
                true)));
    }

    private static byte[] BuildDatc64PointerTableData(IReadOnlyList<(string Id, string Text)> rows)
    {
        const int rowLength = 32;
        var fixedData = new byte[rows.Count * rowLength];
        using var variable = new MemoryStream();
        variable.Write(new byte[] { 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb });

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var rowOffset = rowIndex * rowLength;
            WriteUInt32(fixedData, rowOffset, (uint)rowIndex);
            WriteUInt32(fixedData, rowOffset + 4, AppendDatc64String(variable, rows[rowIndex].Id));
            WriteUInt32(fixedData, rowOffset + 8, 0);
            WriteUInt32(fixedData, rowOffset + 12, AppendDatc64String(variable, rows[rowIndex].Text));
        }

        var variableData = variable.ToArray();
        var data = new byte[4 + fixedData.Length + variableData.Length];
        WriteUInt32(data, 0, (uint)rows.Count);
        fixedData.CopyTo(data, 4);
        variableData.CopyTo(data, 4 + fixedData.Length);
        return data;
    }

    private static uint AppendDatc64String(Stream stream, string value)
    {
        var offset = checked((uint)stream.Position);
        var bytes = Encoding.Unicode.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(0);
        return offset;
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value & 0xff);
        data[offset + 1] = (byte)((value >> 8) & 0xff);
        data[offset + 2] = (byte)((value >> 16) & 0xff);
        data[offset + 3] = (byte)((value >> 24) & 0xff);
    }

    private sealed record Fixture(
        AgentStore Store,
        OverlayStore Overlay,
        string ProfileId,
        string ThreadId,
        string RunId,
        AgentApprovalDto Approval,
        Func<string, string, CancellationToken, Task<Datc64DraftResourceReadResult>> ReadResourceAsync);
}
