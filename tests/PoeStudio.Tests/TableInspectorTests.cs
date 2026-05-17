using System.Text;
using PoeStudio.Contracts;
using PoeStudio.Core.Tables;

namespace PoeStudio.Tests;

public sealed class TableInspectorTests
{
    [Fact]
    public void Inspect_binary_datc64_returns_string_candidates_header_values_and_layout_hints()
    {
        var data = BuildBinaryTableData();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "metadata/items/sample.datc64",
            NormalizedPath: "metadata/items/sample.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var result = inspector.Inspect(resource, data, 4096);

        Assert.False(result.Structured);
        Assert.Equal("datc64", result.Format);
        Assert.NotNull(result.HeaderFields);
        Assert.NotNull(result.Strings);
        Assert.NotNull(result.LayoutHints);
        Assert.Equal("3", result.HeaderFields["u32_0"]);
        Assert.Equal("16", result.HeaderFields["u32_1"]);
        Assert.Contains(result.Strings, item => item.Value == "Sword");
        Assert.Contains(result.Strings, item => item.Value == "Shield");
        Assert.Contains(result.LayoutHints, item => item.Contains("可能行宽", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Inspect_binary_datc64_with_schema_returns_structured_rows()
    {
        var data = BuildSchemaTableData();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "metadata/items/schema.datc64",
            NormalizedPath: "metadata/items/schema.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var schema = new TableSchemaDto(
            RecordSize: 16,
            HeaderSize: 8,
            Fields:
            [
                new TableSchemaFieldDto("id", 0, "u32", Length: null),
                new TableSchemaFieldDto("name", 4, "ascii", Length: 8)
            ]);
        var inspector = new TableInspector();

        var result = inspector.Inspect(resource, data, 4096, schema);

        Assert.True(result.Structured);
        Assert.Equal("schema", result.Delimiter);
        Assert.Equal(["id", "name"], result.Columns);
        Assert.Equal(2, result.PreviewRowCount);
        Assert.Equal(["1", "Sword"], result.Rows[0].Cells);
        Assert.Equal(["2", "Shield"], result.Rows[1].Cells);
        Assert.Contains(result.LayoutHints ?? [], item => item.Contains("schema", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Inspect_binary_datc64_with_schema_returns_all_rows_by_default()
    {
        const int rowCount = 75;
        const int recordSize = 16;
        var data = new byte[8 + rowCount * recordSize];
        WriteUInt32(data, 0, rowCount);
        WriteUInt32(data, 4, recordSize);
        for (var index = 0; index < rowCount; index++)
        {
            var rowOffset = 8 + index * recordSize;
            WriteUInt32(data, rowOffset, (uint)(index + 1));
            WriteAscii(data, rowOffset + 4, $"Item{index + 1}");
        }

        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "metadata/items/schema.datc64",
            NormalizedPath: "metadata/items/schema.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var schema = new TableSchemaDto(
            RecordSize: recordSize,
            HeaderSize: 8,
            Fields:
            [
                new TableSchemaFieldDto("id", 0, "u32", Length: null),
                new TableSchemaFieldDto("name", 4, "ascii", Length: 8)
            ]);
        var inspector = new TableInspector();

        var result = inspector.Inspect(resource, data, data.Length, schema);

        Assert.Equal(rowCount, result.PreviewRowCount);
        Assert.Equal(rowCount, result.Rows.Count);
        Assert.Equal(["75", "Item75"], result.Rows[^1].Cells);
    }

    [Fact]
    public void Inspect_catalog_datc64_uses_full_file_before_falling_back_to_binary_words()
    {
        var data = BuildLargeDatc64CatalogData();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/bestiaryfamilies.datc64",
            NormalizedPath: "data/balance/bestiaryfamilies.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var result = inspector.Inspect(resource, data, 65536);

        Assert.True(result.Structured);
        Assert.Equal("datc64-schema", result.Delimiter);
        Assert.Contains(result.LayoutHints ?? [], item => item.Contains("BestiaryFamilies", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Rows.SelectMany(row => row.Cells), cell => cell == "Mammals");
        Assert.DoesNotContain(result.Warnings, item => item.Contains("未识别到表结构", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Inspect_catalog_datc64_ignores_early_padding_marker_inside_fixed_rows()
    {
        const int expectedRowLength = 1024 * 1024 + 128;
        var data = BuildLargeDatc64CatalogData();
        Array.Fill(data, (byte)0xbb, 4 + 64, 8);
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/bestiaryfamilies.datc64",
            NormalizedPath: "data/balance/bestiaryfamilies.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var result = inspector.Inspect(resource, data, 65536);

        Assert.True(result.Structured);
        Assert.Equal("datc64-schema", result.Delimiter);
        Assert.Equal(expectedRowLength.ToString(), result.HeaderFields?["rowLength"]);
        Assert.Equal("Mammals", result.Rows[0].Cells[0]);
        Assert.Equal("The Wilds", result.Rows[0].Cells[1]);
    }

    [Fact]
    public void Inspect_catalog_datc64_empty_table_keeps_schema_columns()
    {
        var data = BuildEmptyDatc64CatalogData();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/achievementsetrewards.datc64",
            NormalizedPath: "data/balance/achievementsetrewards.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var result = inspector.Inspect(resource, data, 4096);

        Assert.True(result.Structured);
        Assert.Equal("datc64-schema", result.Delimiter);
        Assert.Equal(0, result.PreviewRowCount);
        Assert.NotEmpty(result.Columns ?? []);
        Assert.Contains(result.Columns ?? [], item => item.StartsWith("SetId @0", StringComparison.Ordinal));
        Assert.Contains(result.Columns ?? [], item => item.StartsWith("Rewards @8", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Warnings, item => item.Contains("未识别到表结构", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_catalog_datc64_cell_edits_rebuilds_variable_string_pool()
    {
        var data = BuildLargeDatc64CatalogData();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/bestiaryfamilies.datc64",
            NormalizedPath: "data/balance/bestiaryfamilies.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var edited = inspector.ApplyDatc64CatalogCellEdits(resource, data, [new TableCellEditDto(1, 1, "野兽族")]);
        var result = inspector.Inspect(resource, edited, 65536);

        Assert.True(result.Structured);
        Assert.Equal("datc64-schema", result.Delimiter);
        Assert.Equal("Mammals", result.Rows[0].Cells[0]);
        Assert.Equal("野兽族", result.Rows[0].Cells[1]);
        Assert.Equal("Art/2DItems/Bestiary/Mammals.dds", result.Rows[0].Cells[2]);
        Assert.True(edited.Length > data.Length);
    }

    [Fact]
    public void Apply_catalog_datc64_cell_edits_skips_unsafe_markup_changes()
    {
        var data = BuildCharacterPanelStatsLikeData();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/traditional chinese/characterpanelstats.datc64",
            NormalizedPath: "data/balance/traditional chinese/characterpanelstats.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var result = inspector.ApplyDatc64CatalogCellEditsWithReport(resource, data, [
            new TableCellEditDto(1, 1, "每秒回复"),
            new TableCellEditDto(2, 1, "预期[EffectiveChance|实际][Evasion|闪避]几率"),
            new TableCellEditDto(3, 1, "预期[Chill|冰缓][BuffMagnitude|强度]")
        ]);
        var inspected = inspector.Inspect(resource, result.Data, 4096);

        Assert.Equal(1, result.Applied);
        Assert.Equal(2, result.Skipped.Count);
        Assert.Equal("每秒回复", inspected.Rows[0].Cells[1]);
        Assert.Equal("預期[Evasion|閃避][EffectiveChance|有效機率]", inspected.Rows[1].Cells[1]);
        Assert.Equal("", inspected.Rows[2].Cells[1]);
    }

    [Fact]
    public void Apply_catalog_datc64_cell_edits_ignores_unchanged_values_without_growing_file()
    {
        var data = BuildIncursionRoomPerLevelLikeData();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/traditional chinese/incursion2roomperlevel.datc64",
            NormalizedPath: "data/balance/traditional chinese/incursion2roomperlevel.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();
        var before = inspector.Inspect(resource, data, 4096);

        var result = inspector.ApplyDatc64CatalogCellEditsWithReport(resource, data, [
            new TableCellEditDto(1, 3, before.Rows[0].Cells[3])
        ]);

        Assert.Equal(0, result.Applied);
        Assert.Empty(result.Skipped);
        Assert.Equal(data.Length, result.Data.Length);
        Assert.Equal(data, result.Data);
    }

    [Fact]
    public void Apply_catalog_datc64_cell_edits_relaxes_all_string_cells_for_incursion_room_table()
    {
        var data = BuildIncursionRoomPerLevelLikeData();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/traditional chinese/incursion2roomperlevel.datc64",
            NormalizedPath: "data/balance/traditional chinese/incursion2roomperlevel.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var id = "中文测试id";
        var value = "内含灵核灌注器，可用于改造[SoulCore|灵核]\r\n使用该装置后，房间将[IncursionDestabilization|瓦解]";
        var icon = "图标路径测试";
        var result = inspector.ApplyDatc64CatalogCellEditsWithReport(resource, data, [
            new TableCellEditDto(1, 2, id),
            new TableCellEditDto(1, 3, value),
            new TableCellEditDto(1, 5, icon)
        ]);
        var inspected = inspector.Inspect(resource, result.Data, 4096);

        Assert.Equal(3, result.Applied);
        Assert.Empty(result.Skipped);
        Assert.Equal(id, inspected.Rows[0].Cells[2]);
        Assert.Equal(value, inspected.Rows[0].Cells[3]);
        Assert.Equal(icon, inspected.Rows[0].Cells[5]);
    }

    [Fact]
    public void Apply_catalog_datc64_cell_edits_rejects_non_string_columns()
    {
        var data = BuildLargeDatc64CatalogData();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/bestiaryfamilies.datc64",
            NormalizedPath: "data/balance/bestiaryfamilies.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var error = Assert.Throws<InvalidOperationException>(() =>
            inspector.ApplyDatc64CatalogCellEdits(resource, data, [new TableCellEditDto(1, 7, "true")]));

        Assert.Contains("string", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspect_catalog_datc64_reads_string_pointer_from_second_slot_when_first_slot_is_empty()
    {
        var data = BuildAlternatePassiveSkillsDatc64Data();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/traditional chinese/alternatepassiveskills.datc64",
            NormalizedPath: "data/balance/traditional chinese/alternatepassiveskills.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var result = inspector.Inspect(resource, data, 4096);

        Assert.Equal("datc64-schema", result.Delimiter);
        Assert.Equal("vaal_keystone_1", result.Rows[0].Cells[0]);
        Assert.Equal("神圣血肉", result.Rows[0].Cells[2]);
        Assert.Equal("", result.Rows[0].Cells[14]);
        Assert.Equal("Art/2DItems/Keystones/VaalKeystone.dds", result.Rows[0].Cells[15]);
        Assert.DoesNotContain(result.Rows[0].Cells, cell => cell.Contains('\uBBBB'));
    }

    [Fact]
    public void Apply_catalog_datc64_cell_edits_preserves_second_slot_string_pointer_columns()
    {
        var data = BuildAlternatePassiveSkillsDatc64Data();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/traditional chinese/alternatepassiveskills.datc64",
            NormalizedPath: "data/balance/traditional chinese/alternatepassiveskills.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var edited = inspector.ApplyDatc64CatalogCellEdits(resource, data, [new TableCellEditDto(1, 15, "Art/2DItems/NewIcon.dds")]);
        var result = inspector.Inspect(resource, edited, 4096);

        Assert.Equal("Art/2DItems/NewIcon.dds", result.Rows[0].Cells[15]);
        Assert.Equal("神圣血肉", result.Rows[0].Cells[2]);
    }

    [Fact]
    public void Apply_catalog_datc64_cell_edits_skips_internal_ids_and_file_paths()
    {
        var data = BuildAlternatePassiveSkillsDatc64Data();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/traditional chinese/alternatepassiveskills.datc64",
            NormalizedPath: "data/balance/traditional chinese/alternatepassiveskills.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var editResult = inspector.ApplyDatc64CatalogCellEditsWithReport(resource, data, [
            new TableCellEditDto(1, 0, "瓦尔核心天赋"),
            new TableCellEditDto(1, 15, "图标路径")
        ]);
        var result = inspector.Inspect(resource, editResult.Data, 4096);

        Assert.Equal(0, editResult.Applied);
        Assert.Equal(2, editResult.Skipped.Count);
        Assert.Equal("vaal_keystone_1", result.Rows[0].Cells[0]);
        Assert.Equal("Art/2DItems/Keystones/VaalKeystone.dds", result.Rows[0].Cells[15]);
    }

    [Fact]
    public void Apply_datc64_cell_edits_falls_back_to_inferred_layout_when_catalog_schema_does_not_match()
    {
        var data = BuildDatc64InferredPointerTableData(
        [
            ("MineEntrance", "矿脉入口", "Art/2DArt/UIImages/InGame/Delve/MapEncounters/MineEntrance", "ObstructionNorth", "MineEntrance", "矿脉入口", "MineEntrance"),
            ("Azurite1_Q", "碧蓝矿脉", "Art/2DArt/UIImages/InGame/Delve/MapEncounters/AzuriteVein", "包含碧蓝矿", "MineEntrance", "包含碧蓝矿", "MineEntrance")
        ]);
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/traditional chinese/delvefeatures.datc64",
            NormalizedPath: "data/balance/traditional chinese/delvefeatures.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var edited = inspector.ApplyDatc64CatalogCellEdits(resource, data, [new TableCellEditDto(2, 2, "碧蓝矿点")]);
        var result = inspector.Inspect(resource, edited, 4096);

        Assert.Equal("datc64-auto", result.Delimiter);
        Assert.Equal("碧蓝矿点", result.Rows[1].Cells[2]);
        Assert.Equal("Azurite1_Q", result.Rows[1].Cells[0]);
        Assert.Equal("Art/2DArt/UIImages/InGame/Delve/MapEncounters/AzuriteVein", result.Rows[1].Cells[12]);
    }

    [Fact]
    public void Apply_binary_schema_cell_edits_preserves_length_and_updates_fixed_fields()
    {
        var data = BuildSchemaTableData();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "metadata/items/schema.datc64",
            NormalizedPath: "metadata/items/schema.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var schema = new TableSchemaDto(
            RecordSize: 16,
            HeaderSize: 8,
            Fields:
            [
                new TableSchemaFieldDto("id", 0, "u32", Length: null),
                new TableSchemaFieldDto("name", 4, "ascii", Length: 8)
            ]);
        var inspector = new TableInspector();

        var edited = inspector.ApplyCellEdits(resource, data, [
            new TableCellEditDto(2, 0, "99"),
            new TableCellEditDto(2, 1, "Axe")
        ], schema);
        var result = inspector.Inspect(resource, edited, 4096, schema);

        Assert.Equal(data.Length, edited.Length);
        Assert.Equal(["99", "Axe"], result.Rows[1].Cells);
        Assert.Equal(2, result.PreviewRowCount);
    }

    [Fact]
    public void Inspect_and_edit_binary_schema_supports_common_numeric_and_utf8_fields()
    {
        var data = new byte[64];
        WriteUInt32(data, 0, 2);
        WriteUInt32(data, 4, 28);
        WriteInt16(data, 8, -7);
        WriteUInt64(data, 10, 1234567890123);
        WriteFloat(data, 18, 1.5f);
        WriteUtf8(data, 22, 14, "火");
        WriteInt16(data, 36, 9);
        WriteUInt64(data, 38, 777);
        WriteFloat(data, 46, 2.25f);
        WriteUtf8(data, 50, 14, "Ice");
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "metadata/items/extended.datc64",
            NormalizedPath: "metadata/items/extended.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var schema = new TableSchemaDto(
            RecordSize: 28,
            HeaderSize: 8,
            Fields:
            [
                new TableSchemaFieldDto("delta", 0, "i16"),
                new TableSchemaFieldDto("hash", 2, "u64"),
                new TableSchemaFieldDto("weight", 10, "float"),
                new TableSchemaFieldDto("name", 14, "utf8z", 14)
            ]);
        var inspector = new TableInspector();

        var preview = inspector.Inspect(resource, data, 4096, schema);
        var edited = inspector.ApplyCellEdits(resource, data, [
            new TableCellEditDto(1, 0, "-12"),
            new TableCellEditDto(1, 2, "3.5"),
            new TableCellEditDto(1, 3, "火焰")
        ], schema);
        var editedPreview = inspector.Inspect(resource, edited, 4096, schema);

        Assert.Equal(["-7", "1234567890123", "1.5", "火"], preview.Rows[0].Cells);
        Assert.Equal(["-12", "1234567890123", "3.5", "火焰"], editedPreview.Rows[0].Cells);
        Assert.Equal(["9", "777", "2.25", "Ice"], editedPreview.Rows[1].Cells);
        Assert.Equal(data.Length, edited.Length);
    }

    [Fact]
    public void Inspect_binary_datc64_extracts_utf8_and_utf16le_chinese_string_candidates()
    {
        var data = new byte[160];
        WriteUInt32(data, 0, 3);
        WriteUInt32(data, 4, 32);
        WriteAscii(data, 16, "Sword");
        WriteUtf8(data, 48, 24, "火焰伤害");
        WriteUtf16Le(data, 96, 32, "简体中文");
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "metadata/items/chinese.datc64",
            NormalizedPath: "metadata/items/chinese.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var result = inspector.Inspect(resource, data, 4096);

        Assert.Contains(result.Strings ?? [], item => item.Value == "Sword" && item.Encoding == "ascii");
        Assert.Contains(result.Strings ?? [], item => item.Value == "火焰伤害" && item.Encoding == "utf-8");
        Assert.Contains(result.Strings ?? [], item => item.Value == "简体中文" && item.Encoding == "utf-16le");
        Assert.Equal(["#", "offset", "bytes", "encoding", "text"], result.Columns);
        Assert.Contains(result.Rows, row => row.Cells.Contains("简体中文"));
        Assert.DoesNotContain(result.Strings ?? [], item => item.Value.Contains('\uFFFD'));
        Assert.Contains(result.LayoutHints ?? [], item => item.Contains("UTF-8", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.LayoutHints ?? [], item => item.Contains("UTF-16LE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Inspect_languages_dat_returns_legacy_dat_schema_rows()
    {
        var data = BuildLegacyLanguagesDatData();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/languages.dat",
            NormalizedPath: "data/balance/languages.dat",
            Extension: ".dat",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var result = inspector.Inspect(resource, data, 4096);

        Assert.True(result.Structured);
        Assert.Equal("legacy-dat-schema", result.Delimiter);
        Assert.Equal(["Index @0", "Id @4", "Text", "Tag1", "Tag2", "Unknown0", "IsEnabled", "Unknown1"], result.Columns);
        Assert.Equal(3, result.PreviewRowCount);
        Assert.Equal(["0", "English", "English", "en", "en", "0", "1", "0"], result.Rows[0].Cells);
        Assert.Equal(["1", "French", "French", "fr", "fr", "0", "1", "0"], result.Rows[1].Cells);
        Assert.Equal(["6", "Traditional Chinese", "繁體中文", "zhTW", "zhTW", "0", "1", "0"], result.Rows[2].Cells);
        Assert.Equal([1, 2, 3, 4], result.EditableColumnIndexes);
        Assert.DoesNotContain(result.Warnings, item => item.Contains("未识别到表结构", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_languages_dat_cell_edits_preserves_legacy_dat_rows()
    {
        var data = BuildLegacyLanguagesDatData();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/languages.dat",
            NormalizedPath: "data/balance/languages.dat",
            Extension: ".dat",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var edited = inspector.ApplyLegacyDatCatalogCellEdits(resource, data, [new TableCellEditDto(3, 2, "Traditional Chinese")]);
        var result = inspector.Inspect(resource, edited, 4096);

        Assert.Equal(data.Length + Encoding.Unicode.GetByteCount("Traditional Chinese") - Encoding.Unicode.GetByteCount("繁體中文"), edited.Length);
        Assert.Equal("legacy-dat-schema", result.Delimiter);
        Assert.Equal(["6", "Traditional Chinese", "Traditional Chinese", "zhTW", "zhTW", "0", "1", "0"], result.Rows[2].Cells);
        Assert.Equal(["0", "English", "English", "en", "en", "0", "1", "0"], result.Rows[0].Cells);
    }

    [Fact]
    public void Inspect_datc64_without_catalog_infers_rows_from_string_pointers()
    {
        var data = BuildDatc64PointerTableData([
            ("NoMana", "法力不足"),
            ("OnCooldown", "冷却中"),
            ("NoSpirit", "精魂不足")
        ]);
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/traditional chinese/combatuiprompts.datc64",
            NormalizedPath: "data/balance/traditional chinese/combatuiprompts.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var result = inspector.Inspect(resource, data, 4096);

        Assert.True(result.Structured);
        Assert.Equal("datc64-auto", result.Delimiter);
        Assert.Equal(["u32_0 @0","text_1 @4","u32_2 @8","text_3 @12","u32_4 @16","u32_5 @20","u32_6 @24","u32_7 @28"], result.Columns);
        Assert.Equal(3, result.PreviewRowCount);
        Assert.Equal("NoMana", result.Rows[0].Cells[1]);
        Assert.Equal("法力不足", result.Rows[0].Cells[3]);
        Assert.Equal("OnCooldown", result.Rows[1].Cells[1]);
        Assert.Equal([1, 3], result.EditableColumnIndexes);
        Assert.Contains(result.Warnings, item => item.Contains("自动推断", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_inferred_datc64_text_edits_rebuilds_variable_string_pool()
    {
        var data = BuildDatc64PointerTableData([
            ("NoMana", "法力不足"),
            ("OnCooldown", "冷却中")
        ]);
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/traditional chinese/combatuiprompts.datc64",
            NormalizedPath: "data/balance/traditional chinese/combatuiprompts.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var edited = inspector.ApplyDatc64CatalogCellEdits(resource, data, [new TableCellEditDto(1, 3, "魔力不足")]);
        var result = inspector.Inspect(resource, edited, 4096);

        Assert.Equal("datc64-auto", result.Delimiter);
        Assert.Equal("NoMana", result.Rows[0].Cells[1]);
        Assert.Equal("魔力不足", result.Rows[0].Cells[3]);
        Assert.Equal("冷却中", result.Rows[1].Cells[3]);
        Assert.True(edited.Length > data.Length);
    }

    [Fact]
    public void Apply_inferred_datc64_text_edits_rejects_numeric_columns()
    {
        var data = BuildDatc64PointerTableData([
            ("NoMana", "法力不足")
        ]);
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/traditional chinese/combatuiprompts.datc64",
            NormalizedPath: "data/balance/traditional chinese/combatuiprompts.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var error = Assert.Throws<InvalidOperationException>(() =>
            inspector.ApplyDatc64CatalogCellEdits(resource, data, [new TableCellEditDto(1, 0, "999")]));

        Assert.Contains("文本列", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspect_binary_datc64_does_not_report_short_numeric_words_as_utf16le_text()
    {
        var data = new byte[4096];
        WriteUInt32(data, 0, 62);
        WriteUInt32(data, 4, 8);
        WriteUInt32(data, 12, 46);
        WriteUInt32(data, 20, 3);
        WriteUInt32(data, 24, 82);
        WriteUInt32(data, 32, 119724);

        for (var offset = 96; offset + 12 < data.Length; offset += 74)
        {
            WriteUInt32(data, offset, 0x00010000);
            WriteUInt32(data, offset + 8, 0xA6000100);
            WriteUInt32(data, offset + 16, 0x0000A600);
        }

        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/achievements.datc64",
            NormalizedPath: "data/balance/achievements.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var result = inspector.Inspect(resource, data, 4096);

        Assert.False(result.Structured);
        Assert.DoesNotContain(result.Strings ?? [], item => item.Encoding == "utf-16le");
        Assert.DoesNotContain(result.Strings ?? [], item => item.Value.Contains('Ā'));
        Assert.DoesNotContain(result.LayoutHints ?? [], item => item.Contains("UTF-16LE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, item => item.Contains("未识别到表结构", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Inspect_binary_datc64_without_strings_returns_numeric_word_table()
    {
        var data = new byte[32];
        WriteUInt32(data, 0, 35);
        WriteUInt32(data, 4, 6474);
        WriteUInt32(data, 20, 1);
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/additionalmonsterpacksfromstats.datc64",
            NormalizedPath: "data/balance/additionalmonsterpacksfromstats.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var result = inspector.Inspect(resource, data, 4096);

        Assert.False(result.Structured);
        Assert.Empty(result.Strings ?? []);
        Assert.Equal(["word", "offset", "u32", "i32", "hex"], result.Columns);
        Assert.Equal(["0", "0", "35", "35", "23 00 00 00"], result.Rows[0].Cells);
        Assert.Equal(["1", "4", "6474", "6474", "4A 19 00 00"], result.Rows[1].Cells);
        Assert.Contains(result.LayoutHints ?? [], item => item.Contains("二进制字段概览", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Inspect_datc64_without_strings_still_returns_real_rows()
    {
        var data = BuildDatc64NumericTableData();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/additionallifescalingperlevel.datc64",
            NormalizedPath: "data/balance/additionallifescalingperlevel.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var result = inspector.Inspect(resource, data, 4096);

        Assert.True(result.Structured);
        Assert.Equal("datc64-auto", result.Delimiter);
        Assert.Equal(["u32_0 @0", "u32_1 @4"], result.Columns);
        Assert.Equal(2, result.PreviewRowCount);
        Assert.Equal(["10", "20"], result.Rows[0].Cells);
        Assert.Equal(["30", "40"], result.Rows[1].Cells);
        Assert.Contains(result.Warnings, item => item.Contains("自动推断", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Inspect_empty_datc64_without_catalog_returns_empty_table_not_binary_words()
    {
        var data = new byte[12];
        WriteUInt32(data, 0, 0);
        Array.Fill(data, (byte)0xbb, 4, 8);
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/additionallifescalingperlevel.datc64",
            NormalizedPath: "data/balance/additionallifescalingperlevel.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var result = inspector.Inspect(resource, data, 4096);

        Assert.True(result.Structured);
        Assert.Equal("datc64-auto", result.Delimiter);
        Assert.Equal(["empty"], result.Columns);
        Assert.Empty(result.Rows);
        Assert.DoesNotContain(result.Rows, row => row.Cells.Contains("BB BB BB BB"));
    }

    [Fact]
    public void Inspect_binary_datc64_rejects_random_cjk_utf16le_candidates()
    {
        var data = new byte[220];
        WriteUInt32(data, 0, 5);
        WriteUInt32(data, 4, 8);
        var offset = 64;
        foreach (var value in new ushort[]
        {
            0x9632, 0x6E3F, 0x95F7, 0x89E3, 0x8FDB, 0x5C11, 0x6F38, 0x5947,
            0x6A2A, 0x8F7B, 0x52BF, 0x731B, 0x7D22, 0x5AE9, 0x9C9C, 0x92A0,
            0x5C3D, 0x6B8B, 0x9B54, 0x8E81, 0x811A, 0x9F99, 0x706D, 0x83BA
        })
        {
            data[offset++] = (byte)(value & 0xff);
            data[offset++] = (byte)(value >> 8);
        }

        data[offset++] = 0;
        data[offset] = 0;
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "data/balance/bestiaryfamilies.datc64",
            NormalizedPath: "data/balance/bestiaryfamilies.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var result = inspector.Inspect(resource, data, 4096);

        Assert.Empty(result.Strings ?? []);
        Assert.Equal(["word", "offset", "u32", "i32", "hex"], result.Columns);
        Assert.DoesNotContain(result.Rows.SelectMany(row => row.Cells), cell => cell.Contains("防妨", StringComparison.Ordinal));
    }

    [Fact]
    public void Inspect_text_table_decodes_utf16le_with_bom_and_keeps_chinese_cells_aligned()
    {
        var text = "id\tname\r\n1\t简体中文\r\n2\t繁體中文\r\n";
        var data = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "metadata/text/chinese.tdt",
            NormalizedPath: "metadata/text/chinese.tdt",
            Extension: ".tdt",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var result = inspector.Inspect(resource, data, 4096);

        Assert.True(result.Structured);
        Assert.Equal("\\t", result.Delimiter);
        Assert.Equal(["id", "name"], result.Rows[0].Cells);
        Assert.Equal(["1", "简体中文"], result.Rows[1].Cells);
        Assert.Equal(["2", "繁體中文"], result.Rows[2].Cells);
        Assert.Equal("UTF-16LE", result.TextEncoding);
        Assert.DoesNotContain(result.Rows.SelectMany(row => row.Cells), cell => cell.Contains('\uFFFD'));
        Assert.Contains(result.LayoutHints ?? [], item => item.Contains("UTF-16LE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_text_table_edits_preserves_utf16le_encoding_and_cell_offsets()
    {
        var text = "id\tname\r\n1\t简体中文\r\n2\t繁體中文\r\n";
        var data = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "metadata/text/chinese.tdt",
            NormalizedPath: "metadata/text/chinese.tdt",
            Extension: ".tdt",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var inspector = new TableInspector();

        var edited = inspector.ApplyCellEditsToBytes(resource, data, [new TableCellEditDto(2, 1, "简体文本")]);
        var result = inspector.Inspect(resource, edited, 4096);

        var preamble = Encoding.Unicode.GetPreamble();
        Assert.True(edited.AsSpan(0, preamble.Length).SequenceEqual(preamble));
        Assert.True(result.Structured);
        Assert.Equal("UTF-16LE", result.TextEncoding);
        Assert.Equal(["1", "简体文本"], result.Rows[1].Cells);
        Assert.Equal(["2", "繁體中文"], result.Rows[2].Cells);
    }

    [Fact]
    public void Inspect_and_edit_binary_schema_supports_utf16le_fields_without_shifted_offsets()
    {
        var data = new byte[56];
        WriteUInt32(data, 0, 2);
        WriteUInt32(data, 4, 24);
        WriteUInt32(data, 8, 1);
        WriteUtf16Le(data, 12, 16, "简体");
        WriteUInt32(data, 32, 2);
        WriteUtf16Le(data, 36, 16, "繁體");
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "metadata/items/utf16.datc64",
            NormalizedPath: "metadata/items/utf16.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var schema = new TableSchemaDto(
            RecordSize: 24,
            HeaderSize: 8,
            Fields:
            [
                new TableSchemaFieldDto("id", 0, "u32"),
                new TableSchemaFieldDto("name", 4, "utf16z", 16)
            ]);
        var inspector = new TableInspector();

        var preview = inspector.Inspect(resource, data, 4096, schema);
        var edited = inspector.ApplyCellEdits(resource, data, [new TableCellEditDto(1, 1, "简体文本")], schema);
        var editedPreview = inspector.Inspect(resource, edited, 4096, schema);

        Assert.True(preview.Structured);
        Assert.Equal(["1", "简体"], preview.Rows[0].Cells);
        Assert.Equal(["2", "繁體"], preview.Rows[1].Cells);
        Assert.Equal(data.Length, edited.Length);
        Assert.Equal(["1", "简体文本"], editedPreview.Rows[0].Cells);
        Assert.Equal(["2", "繁體"], editedPreview.Rows[1].Cells);
    }

    [Fact]
    public void Inspect_binary_schema_rejects_overlapping_fields_to_prevent_shifted_display()
    {
        var data = BuildSchemaTableData();
        var resource = new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: "metadata/items/overlap.datc64",
            NormalizedPath: "metadata/items/overlap.datc64",
            Extension: ".datc64",
            Kind: ResourceKind.Table,
            Size: data.Length,
            PhysicalPath: null,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
        var schema = new TableSchemaDto(
            RecordSize: 16,
            HeaderSize: 8,
            Fields:
            [
                new TableSchemaFieldDto("id", 0, "u32"),
                new TableSchemaFieldDto("bad_name", 2, "ascii", 8)
            ]);
        var inspector = new TableInspector();

        var result = inspector.Inspect(resource, data, 4096, schema);

        Assert.False(result.Structured);
        Assert.Contains(result.Warnings, item => item.Contains("重叠", StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] BuildBinaryTableData()
    {
        var data = new byte[96];
        WriteUInt32(data, 0, 3);
        WriteUInt32(data, 4, 16);
        WriteAscii(data, 16, "Sword");
        WriteAscii(data, 40, "Shield");
        WriteAscii(data, 72, "Armour");
        return data;
    }

    private static byte[] BuildSchemaTableData()
    {
        var data = new byte[40];
        WriteUInt32(data, 0, 2);
        WriteUInt32(data, 4, 16);
        WriteUInt32(data, 8, 1);
        WriteAscii(data, 12, "Sword");
        WriteUInt32(data, 24, 2);
        WriteAscii(data, 28, "Shield");
        return data;
    }

    private static byte[] BuildLargeDatc64CatalogData()
    {
        const int rowCount = 1;
        const int rowLength = 1024 * 1024 + 128;
        var variable = BuildDatc64VariableData([
            (0, "Mammals"),
            (32, "The Wilds"),
            (80, "Art/2DItems/Bestiary/Mammals.dds"),
            (160, "IconSmall.dds"),
            (220, "Illustration.dds"),
            (280, "PageArt.dds"),
            (340, "Humid bushland and moonlit caves.")
        ]);
        var data = new byte[4 + rowLength + 8 + variable.Length];
        WriteUInt32(data, 0, rowCount);
        WriteUInt32(data, 4, 8);
        WriteUInt32(data, 12, 40);
        WriteUInt32(data, 20, 88);
        WriteUInt32(data, 28, 168);
        WriteUInt32(data, 36, 228);
        WriteUInt32(data, 44, 288);
        WriteUInt32(data, 52, 348);
        data[60] = 1;
        Array.Fill(data, (byte)0xbb, 4 + rowLength, 8);
        variable.CopyTo(data, 4 + rowLength + 8);
        return data;
    }

    private static byte[] BuildAlternatePassiveSkillsDatc64Data()
    {
        const int rowCount = 1;
        const int rowLength = 140;
        using var variable = new MemoryStream();
        variable.Write(new byte[] { 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb });
        var id = AppendDatc64String(variable, "vaal_keystone_1");
        var name = AppendDatc64String(variable, "神圣血肉");
        var icon = AppendDatc64String(variable, "Art/2DItems/Keystones/VaalKeystone.dds");

        var data = new byte[4 + rowLength + variable.Length];
        WriteUInt32(data, 0, rowCount);
        WriteUInt32(data, 4, id);
        WriteUInt32(data, 4 + 8, 1);
        WriteUInt32(data, 4 + 28, name);
        WriteUInt32(data, 4 + 32, 1);
        WriteUInt32(data, 4 + 48, 1);
        WriteUInt32(data, 4 + 56, 58);
        WriteUInt32(data, 4 + 112, icon);
        WriteUInt32(data, 4 + 116, 1);
        WriteUInt32(data, 4 + 136, 118);
        variable.ToArray().CopyTo(data, 4 + rowLength);
        return data;
    }

    private static byte[] BuildEmptyDatc64CatalogData()
    {
        var data = new byte[12];
        WriteUInt32(data, 0, 0);
        Array.Fill(data, (byte)0xbb, 4, 8);
        return data;
    }

    private static byte[] BuildCharacterPanelStatsLikeData()
    {
        const int rowCount = 3;
        const int rowLength = 167;
        using var variable = new MemoryStream();
        variable.Write(new byte[] { 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb });
        var id1 = AppendDatc64String(variable, "life_regen");
        var text1 = AppendDatc64String(variable, "每秒回復量");
        var id2 = AppendDatc64String(variable, "evasion_effective");
        var text2 = AppendDatc64String(variable, "預期[Evasion|閃避][EffectiveChance|有效機率]");
        var id3 = AppendDatc64String(variable, "empty_chill");

        var data = new byte[4 + rowCount * rowLength + variable.Length];
        WriteUInt32(data, 0, rowCount);
        WriteUInt32(data, 4, id1);
        WriteUInt32(data, 4 + 8, text1);
        WriteUInt32(data, 4 + rowLength, id2);
        WriteUInt32(data, 4 + rowLength + 8, text2);
        WriteUInt32(data, 4 + rowLength * 2, id3);
        WriteUInt32(data, 4 + rowLength * 2 + 8, 0xfefefefe);
        variable.ToArray().CopyTo(data, 4 + rowCount * rowLength);
        return data;
    }

    private static byte[] BuildIncursionRoomPerLevelLikeData()
    {
        const int rowCount = 1;
        const int rowLength = 92;
        using var variable = new MemoryStream();
        variable.Write(new byte[] { 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb });
        var id = AppendDatc64String(variable, "alchemylab_lvl3");
        var description = AppendDatc64String(variable, "包含一個汙染靈魂核心的裝置\r\n在使用後房間會[IncursionDestabilization|不穩定]");
        var name = AppendDatc64String(variable, "宏伟命匣");
        var icon = AppendDatc64String(variable, "Art/2DArt/UIImages/InGame/Incursion/Rooms/AlchemicalLab.dds");
        var description2 = AppendDatc64String(variable, "備用[IncursionDestabilization|不穩定]描述");

        var data = new byte[4 + rowCount * rowLength + variable.Length];
        WriteUInt32(data, 0, rowCount);
        WriteUInt32(data, 4 + 16, 3);
        WriteUInt32(data, 4 + 20, id);
        WriteUInt32(data, 4 + 28, description);
        WriteUInt32(data, 4 + 36, name);
        WriteUInt32(data, 4 + 44, icon);
        WriteUInt32(data, 4 + 84, description2);
        variable.ToArray().CopyTo(data, 4 + rowCount * rowLength);
        return data;
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

    private static byte[] BuildDatc64NumericTableData()
    {
        const int rowCount = 2;
        const int rowLength = 8;
        var data = new byte[4 + rowCount * rowLength + 8];
        WriteUInt32(data, 0, rowCount);
        WriteUInt32(data, 4, 10);
        WriteUInt32(data, 8, 20);
        WriteUInt32(data, 12, 30);
        WriteUInt32(data, 16, 40);
        Array.Fill(data, (byte)0xbb, 4 + rowCount * rowLength, 8);
        return data;
    }

    private static byte[] BuildDatc64InferredPointerTableData(IReadOnlyList<(string Text0, string Text2, string Text12, string Text16, string Text24, string Text26, string Text31)> rows)
    {
        const int rowLength = 180;
        var fixedData = new byte[rows.Count * rowLength];
        using var variable = new MemoryStream();
        variable.Write(new byte[] { 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb });

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var rowOffset = rowIndex * rowLength;
            var row = rows[rowIndex];
            WriteUInt32(fixedData, rowOffset, AppendDatc64String(variable, row.Text0));
            WriteUInt32(fixedData, rowOffset + 8, AppendDatc64String(variable, row.Text2));
            WriteUInt32(fixedData, rowOffset + 48, AppendDatc64String(variable, row.Text12));
            WriteUInt32(fixedData, rowOffset + 64, AppendDatc64String(variable, row.Text16));
            WriteUInt32(fixedData, rowOffset + 96, AppendDatc64String(variable, row.Text24));
            WriteUInt32(fixedData, rowOffset + 104, AppendDatc64String(variable, row.Text26));
            WriteUInt32(fixedData, rowOffset + 124, AppendDatc64String(variable, row.Text31));
        }

        var variableData = variable.ToArray();
        var data = new byte[4 + fixedData.Length + variableData.Length];
        WriteUInt32(data, 0, checked((uint)rows.Count));
        fixedData.CopyTo(data, 4);
        variableData.CopyTo(data, 4 + fixedData.Length);
        return data;
    }

    private static byte[] BuildLegacyLanguagesDatData()
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, 3);
        WriteLegacyLanguageRow(stream, 0, "English", "English", "en", "en", 0, 1, 0);
        WriteLegacyLanguageRow(stream, 1, "French", "French", "fr", "fr", 0, 1, 0);
        WriteLegacyLanguageRow(stream, 6, "Traditional Chinese", "繁體中文", "zhTW", "zhTW", 0, 1, 0);
        stream.Write(new byte[] { 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb });
        return stream.ToArray();
    }

    private static void WriteLegacyLanguageRow(
        Stream stream,
        int index,
        string id,
        string text,
        string tag1,
        string tag2,
        int unknown0,
        int isEnabled,
        int unknown1)
    {
        WriteInt32(stream, index);
        WriteUtf16ZeroTerminated(stream, id);
        WriteUtf16ZeroTerminated(stream, text);
        WriteUtf16ZeroTerminated(stream, tag1);
        WriteUtf16ZeroTerminated(stream, tag2);
        WriteInt32(stream, unknown0);
        WriteInt32(stream, isEnabled);
        WriteInt32(stream, unknown1);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value & 0xff));
        stream.WriteByte((byte)((value >> 8) & 0xff));
        stream.WriteByte((byte)((value >> 16) & 0xff));
        stream.WriteByte((byte)((value >> 24) & 0xff));
    }

    private static void WriteInt32(Stream stream, int value)
    {
        WriteUInt32(stream, unchecked((uint)value));
    }

    private static void WriteUtf16ZeroTerminated(Stream stream, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
        stream.WriteByte(0);
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

    private static byte[] BuildDatc64VariableData(IEnumerable<(int Offset, string Value)> strings)
    {
        var items = strings.ToArray();
        var length = items.Max(item => item.Offset + Encoding.Unicode.GetByteCount(item.Value) + 4);
        var data = new byte[length];
        foreach (var (offset, value) in items)
        {
            var bytes = Encoding.Unicode.GetBytes(value);
            bytes.CopyTo(data, offset);
        }

        return data;
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value & 0xff);
        data[offset + 1] = (byte)((value >> 8) & 0xff);
        data[offset + 2] = (byte)((value >> 16) & 0xff);
        data[offset + 3] = (byte)((value >> 24) & 0xff);
    }

    private static void WriteUInt64(byte[] data, int offset, ulong value)
    {
        for (var index = 0; index < 8; index++)
        {
            data[offset + index] = (byte)((value >> (index * 8)) & 0xff);
        }
    }

    private static void WriteInt16(byte[] data, int offset, short value)
    {
        data[offset] = (byte)(value & 0xff);
        data[offset + 1] = (byte)((value >> 8) & 0xff);
    }

    private static void WriteFloat(byte[] data, int offset, float value)
    {
        BitConverter.GetBytes(value).CopyTo(data, offset);
    }

    private static void WriteUtf8(byte[] data, int offset, int length, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        bytes.CopyTo(data, offset);
        data[offset + Math.Min(bytes.Length, length - 1)] = 0;
    }

    private static void WriteUtf16Le(byte[] data, int offset, int length, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        bytes.AsSpan(0, Math.Min(bytes.Length, length - 2)).CopyTo(data.AsSpan(offset));
        data[offset + Math.Min(bytes.Length, length - 2)] = 0;
        data[offset + Math.Min(bytes.Length, length - 2) + 1] = 0;
    }

    private static void WriteAscii(byte[] data, int offset, string value)
    {
        Encoding.ASCII.GetBytes(value).CopyTo(data, offset);
    }
}
