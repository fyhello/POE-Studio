using System.Text;
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
    private readonly Func<string, string, string?, CancellationToken, Task<Datc64DraftResourceReadResult>> _readResourceAsync;

    public Datc64DraftApplyService(
        AgentStore store,
        OverlayStore overlay,
        Func<string, string, string?, CancellationToken, Task<Datc64DraftResourceReadResult>> readResourceAsync)
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
        try
        {
            var approvals = await _store.ListApprovalsAsync(threadId, runId, cancellationToken);
            var approval = approvals.FirstOrDefault(x => string.Equals(x.Id, approvalId, StringComparison.Ordinal));
            if (approval is null)
            {
                return Datc64DraftApplyResult.Fail("approval_not_found");
            }

            if (approval.Status != AgentApprovalStatus.Pending)
            {
                return Datc64DraftApplyResult.Fail("approval_not_pending");
            }

            var proposal = JsonSerializer.Deserialize<Datc64TranslationDraftProposal>(approval.ProposalJson, JsonOptions);
            if (proposal is null)
            {
                return Datc64DraftApplyResult.Fail("proposal_invalid");
            }

            var run = await _store.GetRunAsync(threadId, runId, cancellationToken);
            var read = await _readResourceAsync(proposal.ProfileId, proposal.ResourcePath, run?.OodlePath, cancellationToken);
            var inspector = new TableInspector();
            var inspect = inspector.Inspect(read.Resource, read.Content, 4096);
            var warnings = new List<string>();
            var edits = new List<TableCellEditDto>();
            var rawStringEdits = new List<RawDatc64StringEdit>();
            foreach (var candidate in proposal.Candidates)
            {
                var locator = ParseLocator(candidate.Locator);
                if (locator.ContainsKey("raw-string") || locator.ContainsKey("byte") || locator.ContainsKey("cstring"))
                {
                    var rawResolved = ResolveRawStringEdit(candidate, locator, inspect, read.Content);
                    if (!rawResolved.Success)
                    {
                        return Datc64DraftApplyResult.Fail(rawResolved.ErrorCode ?? "locator_not_writable");
                    }

                    rawStringEdits.Add(rawResolved.RawEdit!);
                    continue;
                }

                var resolved = ResolveEdit(candidate, locator, inspect, read.Content);
                if (!resolved.Success)
                {
                    if (locator.ContainsKey("offset"))
                    {
                        var rawResolved = ResolveRawStringEdit(candidate, locator, inspect, read.Content);
                        if (rawResolved.Success)
                        {
                            rawStringEdits.Add(rawResolved.RawEdit!);
                            continue;
                        }
                    }

                    return Datc64DraftApplyResult.Fail(resolved.ErrorCode ?? "locator_not_found");
                }

                var edit = resolved.Edit!;
                var row = inspect.Rows[edit.RowNumber - 1];
                if (string.Equals(row.Cells[edit.ColumnIndex], candidate.TranslatedText, StringComparison.Ordinal))
                {
                    warnings.Add("translatedText matches source cell");
                }

                edits.Add(edit);
            }

            var edited = read.Content.AsSpan(0, read.Content.Length).ToArray();
            if (edits.Count > 0)
            {
                var editResult = inspector.ApplyDatc64CatalogCellEditsWithReport(read.Resource, read.Content, edits);
                if (editResult.Skipped.Count > 0)
                {
                    return Datc64DraftApplyResult.Fail("locator_not_writable");
                }

                edited = editResult.Data;
            }

            if (rawStringEdits.Count > 0)
            {
                var rawResult = ApplyRawDatc64StringEdits(edited, inspect, rawStringEdits);
                if (!rawResult.Success)
                {
                    return Datc64DraftApplyResult.Fail(rawResult.ErrorCode ?? "locator_not_writable");
                }

                edited = rawResult.Data!;
            }

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
                AgentApprovalStatus.Pending,
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException)
        {
            return Datc64DraftApplyResult.Fail("proposal_invalid");
        }
        catch (ArgumentException)
        {
            return Datc64DraftApplyResult.Fail("locator_not_writable");
        }
        catch (InvalidOperationException)
        {
            return Datc64DraftApplyResult.Fail("locator_not_writable");
        }
        catch (IOException)
        {
            return Datc64DraftApplyResult.Fail("resource_read_failed");
        }
        catch (UnauthorizedAccessException)
        {
            return Datc64DraftApplyResult.Fail("resource_read_failed");
        }
    }

    private static ResolveEditResult ResolveEdit(
        Datc64TranslationCandidate candidate,
        IReadOnlyDictionary<string, string> locator,
        TableInspectResponse inspect,
        byte[] content)
    {
        if (locator.ContainsKey("offset") || locator.ContainsKey("stringIndex"))
        {
            return ResolveOffsetEdit(candidate, locator, inspect, content);
        }

        return ResolveRowColumnEdit(candidate, locator, inspect);
    }

    private static ResolveEditResult ResolveRowColumnEdit(
        Datc64TranslationCandidate candidate,
        IReadOnlyDictionary<string, string> locator,
        TableInspectResponse inspect)
    {
        var rowIndex = candidate.RowIndex;
        if (locator.TryGetValue("row", out var rowText) && int.TryParse(rowText, out var oneBasedRow))
        {
            rowIndex = oneBasedRow - 1;
        }

        var columnIndex = candidate.ColumnIndex;
        if (locator.TryGetValue("column", out var columnText) && int.TryParse(columnText, out var parsedColumn))
        {
            columnIndex = parsedColumn;
        }

        if (rowIndex < 0 || rowIndex >= inspect.Rows.Count)
        {
            return ResolveEditResult.Fail("locator_not_found");
        }

        var row = inspect.Rows[rowIndex];
        if (columnIndex < 0 || columnIndex >= row.Cells.Count)
        {
            return ResolveEditResult.Fail("locator_not_found");
        }

        return ResolveEditResult.Ok(new TableCellEditDto(rowIndex + 1, columnIndex, candidate.TranslatedText));
    }

    private static ResolveEditResult ResolveOffsetEdit(
        Datc64TranslationCandidate candidate,
        IReadOnlyDictionary<string, string> locator,
        TableInspectResponse inspect,
        byte[] content)
    {
        if (!locator.TryGetValue("offset", out var offsetText) || !int.TryParse(offsetText, out var offset) || offset < 0)
        {
            return ResolveEditResult.Fail("locator_not_found");
        }

        if (!TryGetDatc64Layout(inspect, content.Length, out var fixedStart, out var fixedBytes, out var variableStart, out var rowLength))
        {
            return ResolveEditResult.Fail("locator_not_writable");
        }

        var editableColumns = inspect.EditableColumnIndexes?.ToHashSet() ?? [];
        if (editableColumns.Count == 0)
        {
            return ResolveEditResult.Fail("locator_not_writable");
        }

        var variableOffset = offset >= variableStart ? offset - variableStart : offset;
        for (var rowIndex = 0; rowIndex < inspect.Rows.Count; rowIndex++)
        {
            var row = inspect.Rows[rowIndex];
            foreach (var columnIndex in editableColumns)
            {
                if (columnIndex < 0 || columnIndex >= row.Cells.Count)
                {
                    continue;
                }

                var pointerOffset = fixedStart + rowIndex * rowLength + columnIndex * 4;
                if (pointerOffset < fixedStart || pointerOffset + 4 > fixedStart + fixedBytes || pointerOffset + 4 > content.Length)
                {
                    continue;
                }

                var pointer = ReadUInt32(content, pointerOffset);
                if (pointer != variableOffset)
                {
                    continue;
                }

                if (!string.Equals(row.Cells[columnIndex], candidate.SourceText, StringComparison.Ordinal))
                {
                    return ResolveEditResult.Fail("locator_source_mismatch");
                }

                return ResolveEditResult.Ok(new TableCellEditDto(rowIndex + 1, columnIndex, candidate.TranslatedText));
            }
        }

        if ((inspect.Strings ?? []).Any(item => item.Offset == offset && string.Equals(item.Value, candidate.SourceText, StringComparison.Ordinal)))
        {
            return ResolveEditResult.Fail("locator_not_writable");
        }

        return ResolveEditResult.Fail("locator_not_found");
    }

    private static ResolveRawEditResult ResolveRawStringEdit(
        Datc64TranslationCandidate candidate,
        IReadOnlyDictionary<string, string> locator,
        TableInspectResponse inspect,
        byte[] content)
    {
        if (!TryGetDatc64Layout(inspect, content.Length, out var fixedStart, out var fixedBytes, out var variableStart, out _))
        {
            return ResolveRawEditResult.Fail("locator_not_writable");
        }

        var fixedData = content.AsSpan(fixedStart, fixedBytes);
        var variable = content.AsSpan(variableStart);
        var offsets = new List<int>();
        if (locator.TryGetValue("name", out var name)
            && !string.Equals(name, candidate.SourceText, StringComparison.Ordinal))
        {
            return ResolveRawEditResult.Fail("locator_source_mismatch");
        }

        if (locator.TryGetValue("raw-string", out var rawString))
        {
            if (!string.Equals(rawString, candidate.SourceText, StringComparison.Ordinal))
            {
                return ResolveRawEditResult.Fail("locator_source_mismatch");
            }

            offsets.AddRange(FindReferencedRawStringOffsets(fixedData, variable, candidate.SourceText));
        }
        else if (locator.TryGetValue("offset", out var offsetText) && int.TryParse(offsetText, out var offset) && offset >= 0)
        {
            offsets.AddRange(CandidateVariableOffsets(offset, fixedBytes, variableStart, variable.Length));
        }
        else if (locator.TryGetValue("byte", out var byteText) && int.TryParse(byteText, out var byteOffset) && byteOffset >= 0)
        {
            offsets.AddRange(CandidateVariableOffsets(byteOffset, fixedBytes, variableStart, variable.Length));
        }
        else
        {
            return ResolveRawEditResult.Fail("locator_not_found");
        }

        var matches = new List<int>();
        foreach (var offset in offsets.Distinct())
        {
            if (TryReadNullTerminatedUtf8(variable, offset, out var value)
                && string.Equals(value, candidate.SourceText, StringComparison.Ordinal))
            {
                matches.Add(offset);
            }
        }

        if (matches.Count == 0)
        {
            return ResolveRawEditResult.Fail("locator_not_found");
        }

        var referenced = new List<int>();
        foreach (var offset in matches)
        {
            if (HasPointerReference(fixedData, offset))
            {
                referenced.Add(offset);
            }
        }

        if (referenced.Count == 0)
        {
            return ResolveRawEditResult.Fail("locator_not_writable");
        }

        if (referenced.Count > 1)
        {
            return ResolveRawEditResult.Fail("locator_ambiguous");
        }

        return ResolveRawEditResult.Ok(new RawDatc64StringEdit(referenced[0], candidate.SourceText, candidate.TranslatedText));
    }

    private static RawApplyResult ApplyRawDatc64StringEdits(
        byte[] content,
        TableInspectResponse inspect,
        IReadOnlyList<RawDatc64StringEdit> edits)
    {
        if (!TryGetDatc64Layout(inspect, content.Length, out var fixedStart, out var fixedBytes, out var variableStart, out _))
        {
            return RawApplyResult.Fail("locator_not_writable");
        }

        var fixedData = content.AsSpan(fixedStart, fixedBytes).ToArray();
        var variable = content.AsSpan(variableStart).ToArray();
        var editMap = new Dictionary<int, RawDatc64StringEdit>();
        foreach (var edit in edits)
        {
            if (!TryReadNullTerminatedUtf8(variable, edit.VariableOffset, out var source)
                || !string.Equals(source, edit.SourceText, StringComparison.Ordinal)
                || !HasPointerReference(fixedData, edit.VariableOffset))
            {
                return RawApplyResult.Fail("locator_not_writable");
            }

            if (editMap.TryGetValue(edit.VariableOffset, out var existing)
                && !string.Equals(existing.TranslatedText, edit.TranslatedText, StringComparison.Ordinal))
            {
                return RawApplyResult.Fail("locator_ambiguous");
            }

            editMap[edit.VariableOffset] = edit;
        }

        using var variableOutput = new MemoryStream();
        variableOutput.Write(variable, 0, variable.Length);
        foreach (var edit in editMap.Values.OrderBy(item => item.VariableOffset))
        {
            if (variableOutput.Position > uint.MaxValue)
            {
                return RawApplyResult.Fail("locator_not_writable");
            }

            var newOffset = checked((uint)variableOutput.Position);
            var translated = Encoding.UTF8.GetBytes(edit.TranslatedText);
            variableOutput.Write(translated, 0, translated.Length);
            variableOutput.WriteByte(0);

            for (var pointerOffset = 0; pointerOffset + 4 <= fixedData.Length; pointerOffset += 4)
            {
                if (ReadUInt32(fixedData, pointerOffset) == edit.VariableOffset)
                {
                    WriteUInt32(fixedData, pointerOffset, newOffset);
                }
            }
        }

        var variableData = variableOutput.ToArray();
        var output = new byte[fixedStart + fixedData.Length + variableData.Length];
        content.AsSpan(0, fixedStart).CopyTo(output);
        fixedData.CopyTo(output.AsSpan(fixedStart));
        variableData.CopyTo(output.AsSpan(variableStart));
        return RawApplyResult.Ok(output);
    }

    private static bool TryGetDatc64Layout(
        TableInspectResponse inspect,
        int contentLength,
        out int fixedStart,
        out int fixedBytes,
        out int variableStart,
        out int rowLength)
    {
        fixedStart = 4;
        fixedBytes = 0;
        variableStart = 0;
        rowLength = 0;
        if (inspect.HeaderFields is null
            || !inspect.HeaderFields.TryGetValue("fixedBytes", out var fixedText)
            || !inspect.HeaderFields.TryGetValue("rowLength", out var rowLengthText)
            || !int.TryParse(fixedText, out fixedBytes)
            || !int.TryParse(rowLengthText, out rowLength)
            || fixedBytes < 0
            || rowLength < 4)
        {
            return false;
        }

        variableStart = fixedStart + fixedBytes;
        return variableStart <= contentLength;
    }

    private static Dictionary<string, string> ParseLocator(string locator)
    {
        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in locator.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var marker = segment.IndexOf(':', StringComparison.Ordinal);
            if (marker <= 0)
            {
                continue;
            }

            parts[segment[..marker]] = segment[(marker + 1)..];
        }

        return parts;
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        return (uint)(data[offset]
            | (data[offset + 1] << 8)
            | (data[offset + 2] << 16)
            | (data[offset + 3] << 24));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
    {
        return (uint)(data[offset]
            | (data[offset + 1] << 8)
            | (data[offset + 2] << 16)
            | (data[offset + 3] << 24));
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value & 0xff);
        data[offset + 1] = (byte)((value >> 8) & 0xff);
        data[offset + 2] = (byte)((value >> 16) & 0xff);
        data[offset + 3] = (byte)((value >> 24) & 0xff);
    }

    private static IEnumerable<int> CandidateVariableOffsets(int locatorOffset, int fixedBytes, int variableStart, int variableLength)
    {
        var candidates = new[]
        {
            locatorOffset >= variableStart ? locatorOffset - variableStart : -1,
            locatorOffset >= fixedBytes ? locatorOffset - fixedBytes : -1,
            locatorOffset
        };
        return candidates.Where(offset => offset >= 0 && offset < variableLength);
    }

    private static IReadOnlyList<int> FindReferencedRawStringOffsets(
        ReadOnlySpan<byte> fixedData,
        ReadOnlySpan<byte> variable,
        string sourceText)
    {
        var offsets = new List<int>();
        for (var pointerOffset = 0; pointerOffset + 4 <= fixedData.Length; pointerOffset += 4)
        {
            var pointer = checked((int)ReadUInt32(fixedData, pointerOffset));
            if (pointer >= 0
                && pointer < variable.Length
                && TryReadNullTerminatedUtf8(variable, pointer, out var value)
                && string.Equals(value, sourceText, StringComparison.Ordinal))
            {
                offsets.Add(pointer);
            }
        }

        return offsets;
    }

    private static bool HasPointerReference(ReadOnlySpan<byte> fixedData, int variableOffset)
    {
        for (var pointerOffset = 0; pointerOffset + 4 <= fixedData.Length; pointerOffset += 4)
        {
            if (ReadUInt32(fixedData, pointerOffset) == variableOffset)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadNullTerminatedUtf8(ReadOnlySpan<byte> data, int offset, out string value)
    {
        value = string.Empty;
        if (offset < 0 || offset >= data.Length || data[offset] == 0)
        {
            return false;
        }

        var end = offset;
        while (end < data.Length && data[end] != 0)
        {
            end++;
        }

        if (end <= offset || end >= data.Length)
        {
            return false;
        }

        try
        {
            value = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(data.Slice(offset, end - offset));
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private sealed record ResolveEditResult(bool Success, TableCellEditDto? Edit, string? ErrorCode)
    {
        public static ResolveEditResult Ok(TableCellEditDto edit)
        {
            return new ResolveEditResult(true, edit, null);
        }

        public static ResolveEditResult Fail(string errorCode)
        {
            return new ResolveEditResult(false, null, errorCode);
        }
    }

    private sealed record RawDatc64StringEdit(int VariableOffset, string SourceText, string TranslatedText);

    private sealed record ResolveRawEditResult(bool Success, RawDatc64StringEdit? RawEdit, string? ErrorCode)
    {
        public static ResolveRawEditResult Ok(RawDatc64StringEdit edit)
        {
            return new ResolveRawEditResult(true, edit, null);
        }

        public static ResolveRawEditResult Fail(string errorCode)
        {
            return new ResolveRawEditResult(false, null, errorCode);
        }
    }

    private sealed record RawApplyResult(bool Success, byte[]? Data, string? ErrorCode)
    {
        public static RawApplyResult Ok(byte[] data)
        {
            return new RawApplyResult(true, data, null);
        }

        public static RawApplyResult Fail(string errorCode)
        {
            return new RawApplyResult(false, null, errorCode);
        }
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
