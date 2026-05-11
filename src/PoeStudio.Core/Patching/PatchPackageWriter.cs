using System.Text;
using PoeStudio.Contracts;

namespace PoeStudio.Core.Patching;

public interface IPatchPackageWriter
{
    PatchPackageWriterKind Kind { get; }

    Task<PatchPackageWriteResult> WriteAsync(PatchPackageWriterContext context, CancellationToken cancellationToken);
}

public sealed record PatchPackageWriterContext(
    ClientProfileDto Profile,
    PatchBuildRequest Request,
    string BundlesDirectory,
    IReadOnlyList<OverlayEntryDto> OverlayEntries,
    IReadOnlyList<PatchChangeDto> Changes);

public sealed record PatchPackageWriteResult(
    string IndexPath,
    string BundlePath,
    PatchBuildMode BuildMode,
    IReadOnlyList<string> Warnings);

public sealed class MvpPatchPackageWriter : IPatchPackageWriter
{
    public PatchPackageWriterKind Kind => PatchPackageWriterKind.Mvp;

    public async Task<PatchPackageWriteResult> WriteAsync(PatchPackageWriterContext context, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(context.BundlesDirectory);
        var indexPath = Path.Combine(context.BundlesDirectory, "_.index.bin");
        if (!string.IsNullOrWhiteSpace(context.Profile.IndexPath) && File.Exists(context.Profile.IndexPath))
        {
            File.Copy(context.Profile.IndexPath, indexPath, overwrite: true);
        }
        else
        {
            await File.WriteAllBytesAsync(indexPath, Array.Empty<byte>(), cancellationToken);
        }

        var bundlePath = Path.Combine(context.BundlesDirectory, context.Request.BundleName);
        await WriteMvpBundleAsync(bundlePath, context.OverlayEntries, cancellationToken);

        return new PatchPackageWriteResult(
            indexPath,
            bundlePath,
            PatchBuildMode.OverlayBundleMvp,
            ["当前构建模式为 OverlayBundleMvp，用于工作流验证和审计；真实 Bundles2 index 重写将在 Native Kernel/LibGGPK3 Adapter 阶段接入。"]);
    }

    private static async Task WriteMvpBundleAsync(
        string bundlePath,
        IReadOnlyList<OverlayEntryDto> entries,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(bundlePath);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("POESTUDIO-MVP-BUNDLE\0"));
        writer.Write(entries.Count);

        foreach (var entry in entries.OrderBy(item => item.NormalizedPath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pathBytes = Encoding.UTF8.GetBytes(entry.NormalizedPath);
            var content = await File.ReadAllBytesAsync(entry.OverlayPath, cancellationToken);
            writer.Write(pathBytes.Length);
            writer.Write(pathBytes);
            writer.Write(content.Length);
            writer.Write(content);
        }
    }
}

public sealed class UnavailablePatchPackageWriter(PatchPackageWriterKind kind, string errorCode, string message) : IPatchPackageWriter
{
    public PatchPackageWriterKind Kind { get; } = kind;

    public Task<PatchPackageWriteResult> WriteAsync(PatchPackageWriterContext context, CancellationToken cancellationToken)
    {
        throw new PatchBuildException(errorCode, message);
    }
}

public sealed class PatchBuildException(string errorCode, string message) : InvalidOperationException(message)
{
    public string ErrorCode { get; } = errorCode;
}
