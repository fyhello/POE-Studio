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
}
