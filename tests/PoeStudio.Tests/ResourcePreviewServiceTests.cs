using PoeStudio.Contracts;
using PoeStudio.Core.Native;
using PoeStudio.Core.Preview;

namespace PoeStudio.Tests;

public sealed class ResourcePreviewServiceTests
{
    [Fact]
    public async Task BuildPreviewAsync_returns_text_preview_for_json()
    {
        var file = Path.Combine(Path.GetTempPath(), "poe-studio-preview-tests", Guid.NewGuid().ToString("N"), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "{\"name\":\"Gem\"}");
        var service = new ResourcePreviewService();

        var result = await service.BuildPreviewAsync(Resource("config/config.json", ResourceKind.Text, file), 100, CancellationToken.None);

        Assert.Equal(PreviewKind.Text, result.Kind);
        Assert.Equal("json", result.Language);
        Assert.Contains("\"Gem\"", result.Text);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task BuildPreviewAsync_returns_hex_preview_for_binary()
    {
        var file = Path.Combine(Path.GetTempPath(), "poe-studio-preview-tests", Guid.NewGuid().ToString("N"), "blob.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, [0, 1, 2, 255]);
        var service = new ResourcePreviewService();

        var result = await service.BuildPreviewAsync(Resource("data/blob.bin", ResourceKind.Binary, file), 2, CancellationToken.None);

        Assert.Equal(PreviewKind.Hex, result.Kind);
        Assert.Equal("00 01", result.Hex);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task BuildPreviewAsync_returns_image_media_preview_for_png()
    {
        var file = Path.Combine(Path.GetTempPath(), "poe-studio-preview-tests", Guid.NewGuid().ToString("N"), "icon.png");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, [0x89, 0x50, 0x4E, 0x47]);
        var service = new ResourcePreviewService();

        var result = await service.BuildPreviewAsync(Resource("art/icon.png", ResourceKind.Image, file), 100, CancellationToken.None);

        Assert.Equal(PreviewKind.Image, result.Kind);
        Assert.Equal("image/png", result.MediaType);
        Assert.Equal("iVBORw==", result.Base64Content);
    }

    [Fact]
    public async Task BuildPreviewAsync_returns_audio_media_preview_for_ogg()
    {
        var file = Path.Combine(Path.GetTempPath(), "poe-studio-preview-tests", Guid.NewGuid().ToString("N"), "click.ogg");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, [79, 103, 103, 83]);
        var service = new ResourcePreviewService();

        var result = await service.BuildPreviewAsync(Resource("audio/click.ogg", ResourceKind.Audio, file), 100, CancellationToken.None);

        Assert.Equal(PreviewKind.Audio, result.Kind);
        Assert.Equal("audio/ogg", result.MediaType);
        Assert.Equal("T2dnUw==", result.Base64Content);
    }

    [Fact]
    public async Task BuildPreviewAsync_returns_atlas_inspection_summary()
    {
        var file = Path.Combine(Path.GetTempPath(), "poe-studio-preview-tests", Guid.NewGuid().ToString("N"), "sprites.atlas");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, """
        atlas.png
        size: 512,256
        format: RGBA8888
        filter: Linear,Linear
        icon_sword
          rotate: false
          xy: 10, 20
          size: 64, 64
        icon_bow
          rotate: false
          xy: 80, 20
          size: 48, 48
        """);
        var service = new ResourcePreviewService();

        var result = await service.BuildPreviewAsync(Resource("ui/sprites.atlas", ResourceKind.Ui, file), 2048, CancellationToken.None);

        Assert.Equal(PreviewKind.Text, result.Kind);
        Assert.NotNull(result.Inspection);
        Assert.Equal("atlas", result.Inspection.Format);
        Assert.Equal("2 个图块 · 512x256", result.Inspection.Summary);
        Assert.Equal("2", result.Inspection.Properties["regions"]);
        Assert.Equal("512x256", result.Inspection.Properties["size"]);
    }

    [Fact]
    public async Task BuildPreviewAsync_returns_dds_header_inspection_summary()
    {
        var file = Path.Combine(Path.GetTempPath(), "poe-studio-preview-tests", Guid.NewGuid().ToString("N"), "icon.dds");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, BuildDdsHeader(width: 128, height: 64, mipMaps: 5, fourCc: "DXT5"));
        var service = new ResourcePreviewService();

        var result = await service.BuildPreviewAsync(Resource("art/icon.dds", ResourceKind.Image, file), 256, CancellationToken.None);

        Assert.Equal(PreviewKind.Hex, result.Kind);
        Assert.NotNull(result.Inspection);
        Assert.Equal("dds", result.Inspection.Format);
        Assert.Equal("128x64 · DXT5 · 5 mips", result.Inspection.Summary);
        Assert.Equal("128", result.Inspection.Properties["width"]);
        Assert.Equal("64", result.Inspection.Properties["height"]);
        Assert.Equal("DXT5", result.Inspection.Properties["format"]);
    }

    [Fact]
    public async Task BuildPreviewAsync_returns_ui_inspection_summary_for_fonts_textures_and_text()
    {
        var file = Path.Combine(Path.GetTempPath(), "poe-studio-preview-tests", Guid.NewGuid().ToString("N"), "panel.ui");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, """
        <Panel>
          <Label text="Play" font="FontinSmallCaps" />
          <Image texture="ui/sprites.atlas#button" />
        </Panel>
        """);
        var service = new ResourcePreviewService();

        var result = await service.BuildPreviewAsync(Resource("ui/panel.ui", ResourceKind.Ui, file), 2048, CancellationToken.None);

        Assert.Equal(PreviewKind.Text, result.Kind);
        Assert.NotNull(result.Inspection);
        Assert.Equal("ui", result.Inspection.Format);
        Assert.Equal("3 个标签 · 1 文本 · 1 字体 · 1 贴图", result.Inspection.Summary);
        Assert.Equal("1", result.Inspection.Properties["text_fields"]);
        Assert.Equal("1", result.Inspection.Properties["font_refs"]);
        Assert.Equal("1", result.Inspection.Properties["texture_refs"]);
    }

    [Fact]
    public async Task BuildPreviewAsync_decodes_utf16_little_endian_text()
    {
        var file = Path.Combine(Path.GetTempPath(), "poe-studio-preview-tests", Guid.NewGuid().ToString("N"), "panel.ui");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, System.Text.Encoding.Unicode.GetPreamble()
            .Concat(System.Text.Encoding.Unicode.GetBytes("<DEFINITIONS>\r\nbegin Label\r\nend"))
            .ToArray());
        var service = new ResourcePreviewService();

        var result = await service.BuildPreviewAsync(Resource("ui/panel.ui", ResourceKind.Ui, file), 2048, CancellationToken.None);

        Assert.Equal(PreviewKind.Text, result.Kind);
        Assert.NotNull(result.Text);
        Assert.Contains("<DEFINITIONS>", result.Text);
        Assert.DoesNotContain(result.Text, character => character == '\0');
    }

    [Fact]
    public async Task BuildPreviewAsync_returns_text_preview_for_csd_stat_descriptions()
    {
        var file = Path.Combine(Path.GetTempPath(), "poe-studio-preview-tests", Guid.NewGuid().ToString("N"), "stat_descriptions.csd");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, System.Text.Encoding.Unicode.GetPreamble()
            .Concat(System.Text.Encoding.Unicode.GetBytes("""
            description
            skill_stat_descriptions
            "Monsters have #% increased Attack Speed"
            """))
            .ToArray());
        var service = new ResourcePreviewService();

        var result = await service.BuildPreviewAsync(Resource("data/statdescriptions/stat_descriptions.csd", ResourceKind.Text, file), 2048, CancellationToken.None);

        Assert.Equal(PreviewKind.Text, result.Kind);
        Assert.Equal("text", result.Language);
        Assert.Contains("Monsters have", result.Text);
        Assert.DoesNotContain(result.Text!, character => character == '\0');
        Assert.Null(result.Hex);
    }

    [Fact]
    public async Task BuildPreviewAsync_returns_complete_large_csd_text_when_limit_covers_file()
    {
        var file = Path.Combine(Path.GetTempPath(), "poe-studio-preview-tests", Guid.NewGuid().ToString("N"), "tablet_stat_descriptions.csd");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tail = "lang \"Traditional Chinese\"\r\n\t2\r\n\t\t1|# \"尾部完整\"\r\n";
        var text = "description\r\n"
            + new string('A', 15 * 1024 * 1024)
            + "\r\n"
            + tail;
        await File.WriteAllBytesAsync(file, System.Text.Encoding.Unicode.GetPreamble()
            .Concat(System.Text.Encoding.Unicode.GetBytes(text))
            .ToArray());
        var service = new ResourcePreviewService();

        var result = await service.BuildPreviewAsync(
            Resource("data/statdescriptions/tablet_stat_descriptions.csd", ResourceKind.Text, file),
            (int)new FileInfo(file).Length + 16,
            CancellationToken.None);

        Assert.Equal(PreviewKind.Text, result.Kind);
        Assert.False(result.Truncated);
        Assert.EndsWith(tail, result.Text);
        Assert.Contains("尾部完整", result.Text);
    }

    [Fact]
    public async Task BuildPreviewAsync_can_preview_native_bundle_resource_when_profile_is_supplied()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-preview-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(Path.Combine(bundles, "Metadata"));
        var payload = System.Text.Encoding.UTF8.GetBytes("bundle text payload");
        await File.WriteAllBytesAsync(Path.Combine(bundles, "Metadata", "Text.bundle.bin"), NativeBundleTestData.CreateBundle(payload));
        var profile = CreateProfile(root, bundles);
        var resource = Resource(
            "metadata/text/sample.txt",
            ResourceKind.Text,
            "native-bundles2://Metadata/Text.bundle.bin#offset=7&size=4");
        var service = new ResourcePreviewService(new NativeBundleResourceContentResolver(new CopyOodleCodec()));

        var result = await service.BuildPreviewAsync(resource, profile, 100, CancellationToken.None);

        Assert.Equal(PreviewKind.Text, result.Kind);
        Assert.Equal("text", result.Text);
    }

    [Fact]
    public async Task BuildPreviewAsync_reports_missing_file()
    {
        var service = new ResourcePreviewService();

        var result = await service.BuildPreviewAsync(Resource("missing.txt", ResourceKind.Text, null), 100, CancellationToken.None);

        Assert.Equal(PreviewKind.Unavailable, result.Kind);
        Assert.Equal("resource_file_missing", result.ErrorCode);
    }

    private static ResourceSummaryDto Resource(string path, ResourceKind kind, string? physicalPath)
    {
        return new ResourceSummaryDto(
            Id: Guid.NewGuid().ToString("N"),
            ProfileId: "profile",
            VirtualPath: path,
            NormalizedPath: path,
            Extension: Path.GetExtension(path),
            Kind: kind,
            Size: physicalPath is not null && File.Exists(physicalPath) ? new FileInfo(physicalPath).Length : 0,
            PhysicalPath: physicalPath,
            SourceLayer: ResourceSourceLayer.Base,
            IndexedAt: DateTimeOffset.UtcNow);
    }

    private static ClientProfileDto CreateProfile(string root, string bundles)
    {
        var now = DateTimeOffset.UtcNow;
        return new ClientProfileDto(
            Id: "profile",
            DisplayName: "POE2",
            Platform: ClientPlatform.Official,
            EntryKind: ClientEntryKind.Bundles2,
            RootPath: root,
            ContentGgpkPath: null,
            Bundles2Path: bundles,
            IndexPath: Path.Combine(bundles, "_.index.bin"),
            OodleStatus: OodleStatus.Found,
            ClientFingerprint: "fingerprint",
            CreatedAt: now,
            UpdatedAt: now);
    }

    private static byte[] BuildDdsHeader(int width, int height, int mipMaps, string fourCc)
    {
        var bytes = new byte[128];
        bytes[0] = (byte)'D';
        bytes[1] = (byte)'D';
        bytes[2] = (byte)'S';
        bytes[3] = (byte)' ';
        WriteUInt(bytes, 4, 124);
        WriteUInt(bytes, 12, (uint)height);
        WriteUInt(bytes, 16, (uint)width);
        WriteUInt(bytes, 28, (uint)mipMaps);
        WriteUInt(bytes, 76, 32);
        WriteUInt(bytes, 80, 0x00000004);
        var fourCcBytes = System.Text.Encoding.ASCII.GetBytes(fourCc);
        Array.Copy(fourCcBytes, 0, bytes, 84, Math.Min(4, fourCcBytes.Length));
        return bytes;
    }

    private static void WriteUInt(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value & 0xff);
        bytes[offset + 1] = (byte)((value >> 8) & 0xff);
        bytes[offset + 2] = (byte)((value >> 16) & 0xff);
        bytes[offset + 3] = (byte)((value >> 24) & 0xff);
    }

    private sealed class CopyOodleCodec : IOodleCodec
    {
        public bool IsAvailable => true;

        public int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, int compressor)
        {
            compressed.CopyTo(output);
            return compressed.Length;
        }
    }
}
