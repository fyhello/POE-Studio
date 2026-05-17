using PoeStudio.Core.Native;

namespace PoeStudio.Tests;

public sealed class NativeBundleCompressorTests
{
    [Fact]
    public void Compress_with_copy_codec_can_be_decompressed_by_existing_decompressor()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes("native bundle payload");
        var codec = new CopyNativeBundleCodec();
        var bundle = new NativeBundleCompressor(codec).Compress(payload);

        var result = new NativeBundleDecompressor(codec).Decompress(bundle);

        Assert.True(result.Ok);
        Assert.Equal(payload, result.Data);
    }

    [Fact]
    public void Compress_uses_expected_native_header_fields_for_game_bundles()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        var bundle = new NativeBundleCompressor(new HeaderProbeCodec(9)).Compress(payload, headerUnknown: 1);

        Assert.Equal(4, BitConverter.ToInt32(bundle, 0));
        Assert.Equal(9, BitConverter.ToInt32(bundle, 12));
        Assert.Equal(1, BitConverter.ToInt32(bundle, 16));
    }

    [Fact]
    public void TryCreateOodleCompressCodec_reports_missing_library_without_throwing()
    {
        var codec = NativeOodleCompressCodec.TryCreate("Z:\\missing\\oo2core.dll", out var warning);

        Assert.Null(codec);
        Assert.Contains("不存在", warning);
    }

    [Fact]
    public void TryCreateOodleCompressCodec_accepts_dll_without_buffer_size_export_when_compress_exists()
    {
        const string oodlePath = "E:\\VisualGGPK3_ascii\\oo2core.dll";
        if (!File.Exists(oodlePath))
        {
            return;
        }

        using var codec = NativeOodleCompressCodec.TryCreate(oodlePath, out var warning);

        Assert.NotNull(codec);
        Assert.True(codec.IsAvailable);
        Assert.True(string.IsNullOrWhiteSpace(warning), warning);
    }
}

file sealed class HeaderProbeCodec : INativeBundleCodec
{
    public HeaderProbeCodec(int compressorId)
    {
        CompressorId = compressorId;
    }

    public bool IsAvailable => true;

    public int CompressorId { get; }

    public byte[] Compress(ReadOnlySpan<byte> input)
    {
        return input.ToArray();
    }

    public int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, int compressor)
    {
        compressed.CopyTo(output);
        return compressed.Length;
    }
}
