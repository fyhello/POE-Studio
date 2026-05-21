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

    [Fact]
    public async Task VerifyNativeAsync_rejects_patch_bundle_records_that_exceed_payload_length()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-package-verify-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(bundles);
        var hash = NativeIndexPathResolver.MurmurHash64A(Encoding.UTF8.GetBytes("data/balance/itemclasses.datc64"));
        var indexPayload = await BuildIndexPayloadAsync(root, hash, "Tiny.V0.1", offset: 3, size: 2);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "_.index.bin"), new NativeBundleCompressor(new CopyNativeBundleCodec()).Compress(indexPayload));
        await File.WriteAllBytesAsync(Path.Combine(bundles, "Tiny.V0.1.bundle.bin"), new NativeBundleCompressor(new CopyNativeBundleCodec()).Compress(new byte[] { 1, 2, 3 }));

        var result = await new PatchPackageVerifier(new CopyNativeBundleCodec()).VerifyNativeAsync(
            bundles,
            "Tiny.V0.1.bundle.bin",
            CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(1, result.PatchedFileRecords);
        Assert.Contains(result.Warnings, warning => warning.Contains("超出 patch bundle 解压长度", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyNativeAsync_rejects_patch_bundle_record_with_stale_uncompressed_size()
    {
        var root = Path.Combine(Path.GetTempPath(), "poe-studio-package-verify-tests", Guid.NewGuid().ToString("N"));
        var bundles = Path.Combine(root, "Bundles2");
        Directory.CreateDirectory(bundles);
        var hash = NativeIndexPathResolver.MurmurHash64A(Encoding.UTF8.GetBytes("data/balance/baseitemtypes.datc64"));
        var indexPayload = await BuildIndexPayloadAsync(root, hash, "Tiny.V0.1", offset: 0, size: 3, bundleUncompressedSize: 3);
        await File.WriteAllBytesAsync(Path.Combine(bundles, "_.index.bin"), new NativeBundleCompressor(new CopyNativeBundleCodec()).Compress(indexPayload));
        await File.WriteAllBytesAsync(Path.Combine(bundles, "Tiny.V0.1.bundle.bin"), new NativeBundleCompressor(new CopyNativeBundleCodec()).Compress(new byte[] { 1, 2, 3, 4, 5 }));

        var result = await new PatchPackageVerifier(new CopyNativeBundleCodec()).VerifyNativeAsync(
            bundles,
            "Tiny.V0.1.bundle.bin",
            CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains(result.Warnings, warning => warning.Contains("bundle record 解压长度不匹配", StringComparison.Ordinal));
    }

    private static Task<byte[]> BuildIndexPayloadAsync(string root, ulong hash)
    {
        return BuildIndexPayloadAsync(root, hash, "PoeStudio.NativePatch", offset: 0, size: 3);
    }

    private static Task<byte[]> BuildIndexPayloadAsync(string root, ulong hash, string bundleName, int offset, int size)
    {
        return BuildIndexPayloadAsync(root, hash, bundleName, offset, size, Math.Max(offset + size, 3));
    }

    private static async Task<byte[]> BuildIndexPayloadAsync(string root, ulong hash, string bundleName, int offset, int size, int bundleUncompressedSize)
    {
        var path = Path.Combine(root, "index.payload.bin");
        await using (var stream = File.Create(path))
        await using (var writer = new BinaryWriter(stream))
        {
            writer.Write(2);
            WriteBundle(writer, "Base", 4096);
            WriteBundle(writer, bundleName, bundleUncompressedSize);
            writer.Write(1);
            writer.Write(hash);
            writer.Write(1);
            writer.Write(offset);
            writer.Write(size);
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
