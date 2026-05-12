using PoeStudio.Core.Native;
using PoeStudio.Core.Patching;

namespace PoeStudio.Tests;

public sealed class NativeIndexBundleWriterTests
{
    [Fact]
    public async Task WriteAsync_compresses_decompressed_index_into_index_bundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-native-index-bundle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "index.rewritten.bin");
        var output = Path.Combine(root, "_.index.bin");
        var payload = Enumerable.Range(0, 512).Select(i => (byte)(i % 251)).ToArray();
        await File.WriteAllBytesAsync(source, payload);

        var result = await new NativeIndexBundleWriter().WriteAsync(source, output, new CopyNativeBundleCodec(), CancellationToken.None);

        Assert.Equal(output, result.IndexPath);
        Assert.Equal(payload.Length, result.UncompressedSize);
        Assert.True(result.CompressedSize > 0);
        var bundle = await File.ReadAllBytesAsync(output);
        var decompressed = new NativeBundleDecompressor(new CopyNativeBundleCodec()).Decompress(bundle);
        Assert.True(decompressed.Ok);
        Assert.Equal(payload, decompressed.Data);
    }
}
