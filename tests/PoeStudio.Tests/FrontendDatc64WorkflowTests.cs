using System.Text.RegularExpressions;

namespace PoeStudio.Tests;

public sealed class FrontendDatc64WorkflowTests
{
    [Fact]
    public void Resource_tree_marks_files_and_directories_that_have_overlay_drafts()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));
        var styles = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "styles.css"));

        Assert.Contains("overlayDraftPaths: new Set()", appJs);
        Assert.Contains("item.hasDraft = state.overlayDraftPaths.has(normalizeVirtualPath(item.virtualPath));", appJs);
        Assert.Contains("pathNode.draftCount += 1;", appJs);
        Assert.Contains("resource-tree-has-draft", appJs);
        Assert.Contains("resource-tree-draft-dot", appJs);
        Assert.Contains("function isSelectedResourceItem(item)", appJs);
        Assert.Contains("button.dataset.profileId = item.profileId;", appJs);
        Assert.Contains("isSelectedResourceItem(item) ? \" selected\" : \"\"", appJs);
        Assert.Contains(".resource-tree-draft-dot", styles);
        Assert.Contains("outline: 1px solid", styles);
    }

    [Fact]
    public void Workbench_exposes_workspace_root_and_manual_reference_selection()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));
        var html = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "index.html"));
        var styles = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "styles.css"));

        Assert.Contains("workspaceRootInput", html);
        Assert.Contains("saveWorkspaceBtn", html);
        Assert.Contains("manualReferenceBtn", html);
        Assert.Contains("manualReferenceDialog", html);
        Assert.Contains("manualReferenceSearchInput", html);
        Assert.Contains("manualReferenceResults", html);
        Assert.Contains("manualReferenceResource: null", appJs);
        Assert.Contains("async function loadWorkspaceSettings()", appJs);
        Assert.Contains("async function saveWorkspaceSettings()", appJs);
        Assert.Contains("async function openManualReferenceDialog()", appJs);
        Assert.Contains("async function chooseManualReferenceResource(resource)", appJs);
        Assert.Contains("function clearManualReferenceSelection()", appJs);
        Assert.Contains("state.manualReferenceResource", appJs);
        Assert.Contains("手动选择", appJs);
        Assert.Contains(".manual-reference-dialog", styles);
    }

    [Fact]
    public void Datc64_auto_tables_use_the_same_reference_comparison_workflow_as_schema_tables()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("inspection?.delimiter === \"datc64-auto\"", appJs);
        Assert.Contains("inspection?.delimiter === \"legacy-dat-schema\"", appJs);
        Assert.DoesNotContain("state.tableEditBase?.delimiter === \"datc64-schema\"", appJs);
        Assert.DoesNotContain("result.delimiter === \"datc64-schema\")", appJs);

        var comparisonGateCalls = Regex.Matches(appJs, @"isTableComparisonInspection\(").Count;
        Assert.True(comparisonGateCalls >= 5, $"Expected table comparison gate to be reused across load/apply/render paths, found {comparisonGateCalls}.");
        Assert.Contains("function renderDatc64ComparisonTable(result, referenceResult = null)", appJs);
        Assert.Contains("if (isDatc64ComparisonInspection(state.tableEditBase) || isLegacyDatComparisonInspection(state.tableEditBase))", appJs);
    }

    [Fact]
    public void Csd_files_are_routed_through_text_comparison_and_editing_workflow()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("\".csd\"", appJs);
        Assert.Contains("const textExtensions = new Set([\".ui\", \".xml\", \".json\", \".txt\", \".filter\", \".atlas\", \".csd\"])", appJs);
    }

    [Fact]
    public void Large_text_resource_previews_load_full_text_into_codemirror()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("function previewLimitForResource(resource)", appJs);
        Assert.Contains("limit: previewLimitForResource(resource)", appJs);
        Assert.Contains("function isLargeTextResource(resource)", appJs);
        Assert.Contains("const defaultPreviewLimit = 65536;", appJs);
        Assert.Contains("openLargeTextEditor(resource, preview)", appJs);
        Assert.Contains("Math.min(maxTextPreviewLimit, Math.max(defaultPreviewLimit, size + 16))", appJs);
        Assert.DoesNotContain("largeTextPreviewLimit", appJs);
        Assert.DoesNotContain("/api/text/chunk", appJs);
        Assert.Contains("$('saveOverlayBtn').disabled = usesCodeMirrorEditor(resource) ? false : preview.kind !== 1 || preview.truncated", appJs.Replace("\"", "'"));
        Assert.DoesNotContain("limit: 65536", appJs);
    }

    [Fact]
    public void Csd_resources_always_use_codemirror_full_file_editor()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("function usesCodeMirrorEditor(resource)", appJs);
        Assert.Contains("return isCsdResource(resource) || isLargeTextResource(resource);", appJs);
        Assert.Contains("if (usesCodeMirrorEditor(resource))", appJs);
        Assert.Contains("$('saveOverlayBtn').disabled = usesCodeMirrorEditor(resource) ? false : preview.kind !== 1 || preview.truncated", appJs.Replace("\"", "'"));
        Assert.Contains("if (state.largeText.active && usesCodeMirrorEditor(state.selectedResource))", appJs);
    }

    [Fact]
    public void Large_csd_files_defer_expensive_tag_and_line_analysis_until_requested()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("const largeCsdAutoAnalysisThreshold = 8 * 1024 * 1024;", appJs);
        Assert.Contains("function shouldDeferCsdAnalysis(text, options = {})", appJs);
        Assert.Contains("CSD 标签：按需检查", appJs);
        Assert.Contains("renderCsdTagStatus(editor.value, { force: true })", appJs);
        Assert.Contains("function shouldUseLargeTextFastOpen(text)", appJs);
        Assert.Contains("整文件 · 大文件模式", appJs);
    }

    [Fact]
    public void Replacement_resource_upload_uses_multipart_instead_of_base64_json()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));
        var functionStart = appJs.IndexOf("async function replaceResourceWithFile(file)", StringComparison.Ordinal);
        var functionEnd = appJs.IndexOf("async function inspectTable", functionStart, StringComparison.Ordinal);
        var replaceFunction = appJs[functionStart..functionEnd];

        Assert.Contains("const form = new FormData();", replaceFunction);
        Assert.Contains("form.append(\"file\", file, file.name || \"replacement.bin\");", replaceFunction);
        Assert.Contains("const result = await apiForm(\"/api/overlay/save-file\", form);", replaceFunction);
        Assert.DoesNotContain("readFileAsBase64(file)", replaceFunction);
        Assert.DoesNotContain("base64Content", replaceFunction);
    }

    [Fact]
    public void Large_text_editor_bundle_uses_valid_codemirror_extensions()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var bundle = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "vendor", "codemirror", "poe-codemirror.js"));

        Assert.Contains("createPoeEditor", bundle);
        Assert.DoesNotContain("lineWrapping.of", bundle);
    }

    [Fact]
    public void CodeMirror_readonly_reference_editor_keeps_focus_and_select_all_shortcut()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var bundle = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "vendor", "codemirror", "poe-codemirror.js"));

        Assert.Contains("\"Mod-a\"", bundle);
        Assert.Contains("editable.of(!0)", bundle);
        Assert.DoesNotContain("editable.of(e.readOnly!==!0)", bundle);
    }

    [Fact]
    public void Large_text_editor_reports_full_file_load_failures()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("try {", appJs);
        Assert.Contains("大文件编辑器加载失败", appJs);
        Assert.Contains("largeTextTargetStatus", appJs);
    }

    [Fact]
    public void Text_comparison_mode_does_not_duplicate_large_target_text_into_hidden_preview()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("function activeTextEditor()", appJs);
        Assert.Contains("renderPreview(preview, { deferText: isComparableTextResource(resource) })", appJs);
        Assert.Contains("activeTextEditor().value", appJs);
        Assert.DoesNotContain("$(\"previewText\").value = referenceText", appJs);
        Assert.DoesNotContain("$(\"previewText\").value = next", appJs);
        Assert.DoesNotContain("text: $(\"previewText\").value", appJs);
    }

    [Fact]
    public void Text_reference_preview_uses_resource_aware_limit()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("limit: previewLimitForResource(reference)", appJs);
        Assert.DoesNotContain("virtualPath: reference.virtualPath,\r\n    limit: defaultPreviewLimit", appJs);
    }

    [Fact]
    public void Datc64_table_inspection_uses_resource_sized_limit_for_full_table_display()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("function tableInspectLimitForResource(resource)", appJs);
        Assert.Contains("limit: tableInspectLimitForResource(state.selectedResource)", appJs);
        Assert.Contains("limit: tableInspectLimitForResource(reference)", appJs);
        Assert.DoesNotContain("virtualPath: reference.virtualPath,\r\n    oodlePath: $(\"oodlePathInput\").value.trim() || null,\r\n    limit: defaultPreviewLimit", appJs);
    }

    [Fact]
    public void Datc64_comparison_uses_grid_editor_backed_by_tsv_for_all_table_sizes()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));
        var styles = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "styles.css"));

        Assert.Contains("function renderDatc64TsvComparison(referenceResult, targetResult, editableIndexes, tableSummary = \"\")", appJs);
        Assert.Contains("function renderDatc64TsvGrid(side, tsvText, columns, editableIndexes, columnWidths, referenceRows = new Map(), targetRows = new Map())", appJs);
        Assert.Contains("function buildDatc64Tsv(inspection, rows, columns)", appJs);
        Assert.Contains("function parseDatc64Tsv(text, columns)", appJs);
        Assert.Contains("function collectDatc64TsvEdits()", appJs);
        Assert.Contains("const compareTable = renderDatc64AgGridComparison(referenceResult, result, editableIndexes, tableSummary);", appJs);
        Assert.Contains("collectDatc64AgGridEdits() || collectDatc64TsvEdits()", appJs);
        Assert.Contains("datc64-tsv-cell datc64-tsv-target-cell", appJs);
        Assert.Contains("data-tsv-cell", appJs);
        Assert.Contains(".datc64-tsv-table", styles);
        Assert.DoesNotContain("const compareTable = renderDatc64CompareTable(referenceResult, result, editableIndexes, tableSummary);", appJs);
    }

    [Fact]
    public void Datc64_grid_editor_keeps_column_alignment_and_cell_actions()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));
        var styles = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "styles.css"));

        Assert.Contains("function calculateDatc64TsvColumnWidths(columns, rows, referenceRows)", appJs);
        Assert.Contains("function datc64TsvGridColumnStyle(columns, columnWidths)", appJs);
        Assert.Contains("showDatc64ActionCard(event.currentTarget)", appJs);
        Assert.Contains("handleDatc64Action(button.dataset.action)", appJs);
        Assert.Contains(".datc64-tsv-target-cell, .datc64-tsv-head[data-side='target'].editable-column", appJs);
        Assert.Contains(".datc64-tsv-selected", styles);
        Assert.Contains("data-reference=", appJs);
        Assert.Contains("data-original=", appJs);
    }

    [Fact]
    public void Datc64_grid_editor_respects_light_theme_and_marks_diff_cells()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));
        var styles = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "styles.css"));

        Assert.Contains("const diffClass = changed ? \"datc64-tsv-diff-cell\" : \"\";", appJs);
        Assert.Contains("data-has-diff=", appJs);
        Assert.Contains(".datc64-tsv-diff-cell", styles);
        Assert.Contains(":root[data-theme=\"light\"] .datc64-tsv-cell", styles);
        Assert.Contains(":root[data-theme=\"light\"] .datc64-tsv-scroll", styles);
        Assert.Contains(":root[data-theme=\"light\"] .datc64-tsv-diff-cell", styles);
    }

    [Fact]
    public void Datc64_grid_editor_virtualizes_large_tables()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));
        var styles = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "styles.css"));

        Assert.Contains("const datc64VirtualRowHeight = 28;", appJs);
        Assert.Contains("const datc64VirtualOverscan = 12;", appJs);
        Assert.Contains("function datc64TsvVisibleRange(scrollTop, rowCount, viewportHeight)", appJs);
        Assert.Contains("function renderDatc64TsvVirtualRows()", appJs);
        Assert.Contains("function persistVisibleDatc64TsvEdits()", appJs);
        Assert.Contains("state.datc64Tsv.edits", appJs);
        Assert.Contains("paddingTop", appJs);
        Assert.Contains("paddingBottom", appJs);
        Assert.Contains(".datc64-tsv-virtual-spacer", styles);
    }

    [Fact]
    public void Datc64_comparison_uses_ag_grid_editor_shell()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));
        var indexHtml = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "index.html"));
        var styles = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "styles.css"));

        Assert.Contains("/vendor/ag-grid/ag-grid-community.min.noStyle.js", indexHtml);
        Assert.Contains("/vendor/ag-grid/ag-grid.min.css", indexHtml);
        Assert.Contains("/vendor/ag-grid/ag-theme-quartz.min.css", indexHtml);
        Assert.Contains("function renderDatc64AgGridComparison(referenceResult, targetResult, editableIndexes, tableSummary = \"\")", appJs);
        Assert.Contains("agGrid.createGrid", appJs);
        Assert.Contains("function buildDatc64AgGridRows(targetResult, referenceResult)", appJs);
        Assert.Contains("function buildDatc64AgGridColumnDefs(columns, editableIndexes, side = \"target\")", appJs);
        Assert.Contains("datc64-ag-grid", appJs);
        Assert.Contains(".datc64-ag-grid", styles);
        Assert.True(File.Exists(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "vendor", "ag-grid", "ag-grid-community.min.noStyle.js")));
    }

    [Fact]
    public void Datc64_ag_grid_restores_reference_target_layout_and_actions()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));
        var styles = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "styles.css"));

        Assert.Contains("datc64AgReferenceGridHost", appJs);
        Assert.Contains("datc64AgTargetGridHost", appJs);
        Assert.Contains("function createDatc64AgGrid(host, rows, columns, editableIndexes, side)", appJs);
        Assert.Contains("sortable: false", appJs);
        Assert.Contains("onCellClicked: (event) => showDatc64AgGridActionCard(event, side)", appJs);
        Assert.Contains("onGridReady: () => bindDatc64AgGridHeaderActions(host)", appJs);
        Assert.Contains("function showDatc64AgGridActionCard(event, side = \"target\")", appJs);
        Assert.Contains("function showDatc64AgGridColumnActionCard(event)", appJs);
        Assert.Contains("function bindDatc64AgGridHeaderActions(host)", appJs);
        Assert.Contains("setDatc64AgGridSelectedCellRange(anchor, focus, side);", appJs);
        Assert.Contains("datc64CellActions", appJs);
        Assert.Contains(".datc64-ag-grid", styles);
        Assert.Contains("--ag-font-size: 10.5px", styles);
        Assert.Contains("--ag-row-height: 18px", styles);
        Assert.Contains("--ag-cell-horizontal-border: solid 1px", styles);
        Assert.Contains("--ag-header-column-separator-display: block", styles);
        Assert.Contains("font-size: 10.5px", styles);
        Assert.Contains(".datc64-ag-grid-pair", styles);
    }

    [Fact]
    public void Datc64_ag_grid_reference_table_keeps_reference_only_extra_rows()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("function buildDatc64AgGridReferenceRows(targetResult, referenceResult)", appJs);
        Assert.Contains("const sourceRows = referenceResult?.rows?.length ? referenceResult.rows : (targetResult.rows || []);", appJs);
        Assert.Contains("return sourceRows.map((row) => {", appJs);
        Assert.DoesNotContain("return (targetResult.rows || []).map((row) => {\r\n    const referenceRow = referenceRows.get(row.rowNumber);", appJs);
        Assert.Contains("syncDatc64AgGridVerticalScrollByRatio", appJs);
        Assert.Contains("const ratio = source.scrollTop / sourceMax;", appJs);
    }

    [Fact]
    public void Datc64_ag_grid_supports_selection_sync_and_diff_filters()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));
        var styles = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "styles.css"));

        Assert.Contains("function syncDatc64AgGridScroll(sourceHost, targetHost)", appJs);
        Assert.Contains("querySelector(\".ag-body-viewport\")", appJs);
        Assert.Contains("querySelector(\".ag-body-horizontal-scroll-viewport\")", appJs);
        Assert.Contains("function bindDatc64ScrollPair(sourceElement, targetElement, axis, sync)", appJs);
        Assert.Contains("function applyDatc64AgGridDiffFilters()", appJs);
        Assert.Contains("function setDatc64AgGridSelectedCellRange(anchor, focus, side = \"target\")", appJs);
        Assert.Contains("selectedCellKeys: new Set()", appJs);
        Assert.Contains("selectionAnchor: null", appJs);
        Assert.Contains("event.event?.preventDefault?.()", appJs);
        Assert.Contains("window.getSelection?.()?.removeAllRanges?.()", appJs);
        Assert.Contains("function datc64SelectedAgGridCells()", appJs);
        Assert.Contains("for (const selectedCell of selectedCells)", appJs);
        Assert.Contains("doesExternalFilterPass", appJs);
        Assert.Contains("setColumnsVisible([field], !hide)", appJs);
        Assert.Contains(".datc64-ag-selected-cell", styles);
        Assert.Contains("--ag-font-size: 10.5px", styles);
        Assert.Contains("rowHeight: 18", appJs);
        Assert.Contains("border-right: 1px solid var(--border)", styles);
        Assert.Contains("font-weight: 400", styles);
        Assert.Contains(".datc64-ag-grid .ag-header-cell-text", styles);
    }

    [Fact]
    public void Datc64_ag_grid_supports_safe_excel_style_copy_paste_between_reference_and_target()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("onCellClicked: (event) => showDatc64AgGridActionCard(event, side)", appJs);
        Assert.Contains("function copyDatc64AgGridSelectionToClipboard(", appJs);
        Assert.Contains("function pasteDatc64AgGridClipboardToTarget(", appJs);
        Assert.Contains("function bindDatc64AgGridClipboardShortcuts(host)", appJs);
        Assert.Contains("clipboardText: \"\"", appJs);
        Assert.Contains("clipboardMatrix: null", appJs);
        Assert.Contains("selectionSide: null", appJs);
        Assert.Contains("pasteAnchor: null", appJs);
        Assert.Contains("state.datc64AgGrid.selectionSide !== \"target\"", appJs);
        Assert.Contains("if (!grid.editableIndexes?.has(targetColumnIndex)) continue;", appJs);
        Assert.Contains("if (!rowByNumber.has(targetRowNumber)) continue;", appJs);
        Assert.Contains("data[`c${targetColumnIndex}`] = value;", appJs);
        Assert.DoesNotContain("__originalCells[targetColumnIndex] = value", appJs);
        Assert.Contains("clipboardData?.setData(\"text/plain\", text)", appJs);
        Assert.Contains("const pastedText = event?.clipboardData?.getData(\"text/plain\") || \"\";", appJs);
        Assert.Contains("const isInternalClipboard = Boolean(grid.clipboardMatrix) && (!pastedText || pastedText === grid.clipboardText);", appJs);
        Assert.Contains("function datc64ClipboardCellToTsv(value)", appJs);
        Assert.Contains("function parseDatc64ClipboardTsv(text)", appJs);
        Assert.Contains("cell.includes(\"\\n\")", appJs);
        Assert.Contains("quote = !quote;", appJs);
        Assert.Contains("const rows = isInternalClipboard ? grid.clipboardMatrix : parseDatc64ClipboardTsv(text);", appJs);
        Assert.Contains("已复制", appJs);
        Assert.Contains("已粘贴", appJs);
    }

    [Fact]
    public void Table_csv_export_downloads_utf8_bom_for_excel_compatibility()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("downloadText(`${state.selectedResource.virtualPath.split(/[\\\\/]/).pop() || \"table\"}.csv`, result.csv, \"text/csv;charset=utf-8\", { utf8Bom: true });", appJs);
        Assert.Contains("function downloadText(fileName, text, contentType = \"text/plain;charset=utf-8\", options = {})", appJs);
        Assert.Contains("options.utf8Bom", appJs);
        Assert.Contains("`\\uFEFF${text || \"\"}`", appJs);
    }

    [Fact]
    public void Table_csv_import_does_not_block_on_immediate_large_table_reload()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));
        var importFunction = Regex.Match(
            appJs,
            @"async function importTableCsv\(file\) \{[\s\S]*?\n\}",
            RegexOptions.CultureInvariant).Value;

        Assert.Contains("tableCsvImporting: false", appJs);
        Assert.Contains("if (!state.selectedResource || !file || state.tableCsvImporting) return;", appJs);
        Assert.Contains("state.tableCsvImporting = true;", importFunction);
        Assert.Contains("const form = new FormData();", importFunction);
        Assert.Contains("form.append(\"csvFile\", file, file.name || \"table.csv\");", importFunction);
        Assert.Contains("const result = await apiForm(\"/api/tables/import-csv-file\", form);", importFunction);
        Assert.DoesNotContain("await file.text()", importFunction);
        Assert.DoesNotContain("csv,", importFunction);
        Assert.Contains("setStatus(\"CSV 已写入草稿，正在后台刷新当前表格...\");", importFunction);
        Assert.Contains("setTimeout(() => inspectTable({ auto: true })", importFunction);
        Assert.DoesNotContain("await inspectTable();", importFunction);
    }

    [Fact]
    public void Csd_files_have_dedicated_simplified_to_traditional_slot_action()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));
        var indexHtml = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "index.html"));

        Assert.Contains("csdAdoptTraditionalBtn", indexHtml);
        Assert.Contains("csdTagStatus", indexHtml);
        Assert.Contains("function isCsdResource(resource)", appJs);
        Assert.Contains("function analyzeCsdLanguageTags(text)", appJs);
        Assert.Contains("function renderCsdTagStatus(text, options = {})", appJs);
        Assert.Contains("function jumpToNextCsdTagIssue()", appJs);
        Assert.Contains("function jumpTextareaToPosition(textarea, position)", appJs);
        Assert.Contains("function adoptCsdSimplifiedChineseSlot()", appJs);
        Assert.Contains("lang \"#Traditional Chinese\"", appJs);
        Assert.Contains("lang \"Simplified Chinese\"", appJs);
        Assert.Contains("lang \"Traditional Chinese\"", appJs);
        Assert.Contains("csd: [\"quickMatchBtn\", \"csdAdoptTraditionalBtn\", \"saveOverlayBtn\", \"exportResourceBtn\", \"replaceResourceBtn\", \"patchDryRunBtn\"]", appJs);
        Assert.Contains("const visible = isTable ? groups.table : isCsd ? groups.csd : isCodeMirrorText", appJs);
        Assert.Contains("$(\"csdAdoptTraditionalBtn\").addEventListener(\"click\", adoptCsdSimplifiedChineseSlot)", appJs);
        Assert.Contains("renderCsdTagStatus($(\"targetPreviewText\").value)", appJs);
        Assert.Contains("renderCsdTagStatus(next)", appJs);
        Assert.Contains("processCsdDescriptionBlock", appJs);
        Assert.Contains("$(\"csdTagStatus\").addEventListener(\"click\", jumpToNextCsdTagIssue)", appJs);
        Assert.Contains("setSelectionRange(safePosition, safePosition)", appJs);
    }

    [Fact]
    public void Binary_dat_string_candidates_use_reference_comparison_workflow()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var appJs = File.ReadAllText(Path.Combine(repoRoot, "src", "PoeStudio.Api", "wwwroot", "app.js"));

        Assert.Contains("function isStringCandidateComparisonInspection(inspection)", appJs);
        Assert.Contains("isTableComparisonInspection(result)", appJs);
        Assert.Contains("isStringCandidateComparisonInspection(targetInspection)", appJs);
        Assert.Contains("renderComparisonTable(result, referenceResult)", appJs);
    }
}
