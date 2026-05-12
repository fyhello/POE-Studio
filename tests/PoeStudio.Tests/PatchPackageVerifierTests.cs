using System.Text;
using PoeStudio.Core.Native;
using PoeStudio.Core.Patching;

namespace PoeStudio.Tests;

public sealed class PatchPackageVerifierTests
{
    [Fact]
    public async Task VerifyNativeAsync_checks_index_and_patch_bundle_are_readable()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-package-verify-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(bundles);
        var hash = NativeIndexPathResolver.MurmurHash64A(Encoding.UTF8.GetBytes("text/sample.txt"));
        var indexPayload = await BuildIndexPayloadAsync(root, hash);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "_.index.bin"), new NativeBundleCompressor(new CopyNativeBundleCodec()).Compress(indexPayload));
        await File.WriteAllBytesAsync(Path.Combine(bundles, "PoeStudio.NativePatch.bundle.bin"), new NativeBundleCompressor(new CopyNativeBundleCodec()).Compress(new byte[] { 1, 2, 3 }));

        var result = await new PatchPackageVerifier(new CopyNativeBundleCodec()).VerifyNativeAsync(
            bundles,
            "PoeStudio.NativePatch.bundle.bin",
            CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(1, result.PatchedFileRecords);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task VerifyNativeAsync_reports_missing_patch_bundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-package-verify-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(bundles);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "_.index.bin"), new byte[] { 1, 2, 3 });

        var result = await new PatchPackageVerifier(new CopyNativeBundleCodec()).VerifyNativeAsync(
            bundles,
            "PoeStudio.NativePatch.bundle.bin",
            CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains(result.Warnings, warning => warning.Contains("patch bundle", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<byte[]> BuildIndexPayloadAsync(string root, ulong hash)
    {
        var path = Path.Combine(root, "index.payload.bin");
        await using (var stream = File.Create(path))
        await using (var writer = new BinaryWriter(stream))
        {
            writer.Write(2);
            WriteBundle(writer, "Base", 4096);
            WriteBundle(writer, "PoeStudio.NativePatch", 3);
            writer.Write(1);
            writer.Write(hash);
            writer.Write(1);
            writer.Write(0);
            writer.Write(3);
            writer.Write(0);
        }

        return await File.ReadAllBytesAsync(path);
    }

    private static void WriteBundle(BinaryWriter writer, string path, int uncompressedSize)
    {
        var bytes = Encoding.UTF8.GetBytes(path);
        writer.Write(bytes.Length);
        writer.Write(bytes);
        writer.Write(uncompressedSize);
    }
}
