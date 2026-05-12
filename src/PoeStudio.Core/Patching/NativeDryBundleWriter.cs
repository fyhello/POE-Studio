using System.Text;
using System.Text.Json;
using PoeStudio.Contracts;

namespace PoeStudio.Core.Patching;

public sealed record NativeDryBundleWriteResult(
    string BundlePath,
    string ManifestPath,
    long Size);

public sealed class NativeDryBundleWriter
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("POESTUDIO-NATIVE-DRY-BUNDLE\0");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<NativeDryBundleWriteResult> WriteAsync(
        string outputDirectory,
        NativePatchPlanResponse plan,
        IReadOnlyList<OverlayEntryDto> entries,
        CancellationToken cancellationToken)
    {
        if (!plan.Ready)
        {
            throw new InvalidOperationException("Native 写包计划未就绪。");
        }

        Directory.CreateDirectory(outputDirectory);
        var bundlePath = Path.Combine(outputDirectory, plan.BundleName);
        var manifestPath = Path.Combine(outputDirectory, "native_patch_plan.json");
        var byPath = entries.ToDictionary(item => item.NormalizedPath, StringComparer.OrdinalIgnoreCase);

        await using (var stream = File.Create(bundlePath))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
        {
            writer.Write(Magic);
            writer.Write(plan.Items.Count);
            foreach (var item in plan.Items.OrderBy(item => item.Offset))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!byPath.TryGetValue(item.VirtualPath, out var entry))
                {
                    throw new InvalidOperationException($"找不到 overlay entry：{item.VirtualPath}");
                }

                var pathBytes = Encoding.UTF8.GetBytes(item.VirtualPath);
                var hashBytes = Encoding.UTF8.GetBytes(item.OverlayHash);
                var content = await File.ReadAllBytesAsync(entry.OverlayPath, cancellationToken);
                writer.Write(pathBytes.Length);
                writer.Write(pathBytes);
                writer.Write(item.Offset);
                writer.Write(item.Size);
                writer.Write(hashBytes.Length);
                writer.Write(hashBytes);
                writer.Write(content.Length);
                writer.Write(content);
            }
        }

        await using (var manifest = File.Create(manifestPath))
        {
            await JsonSerializer.SerializeAsync(manifest, plan, JsonOptions, cancellationToken);
        }

        return new NativeDryBundleWriteResult(bundlePath, manifestPath, new FileInfo(bundlePath).Length);
    }
}
