using PoeStudio.Contracts;
using PoeStudio.Core.Native;
using PoeStudio.Core.Workspace;

namespace PoeStudio.Core.Patching;

public sealed class NativeBundles2PackageWriter : IPatchPackageWriter
{
    private readonly string workspaceRoot;
    private readonly IPatchResourceLookup resourceLookup;
    private readonly INativeBundleCodec? codec;

    public NativeBundles2PackageWriter(string workspaceRoot, IPatchResourceLookup resourceLookup)
        : this(workspaceRoot, resourceLookup, null)
    {
    }

    public NativeBundles2PackageWriter(string workspaceRoot, IPatchResourceLookup resourceLookup, INativeBundleCodec? codec)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
        this.resourceLookup = resourceLookup;
        this.codec = codec;
    }

    public PatchPackageWriterKind Kind => PatchPackageWriterKind.NativeBundles2;

    public async Task<PatchPackageWriteResult> WriteAsync(PatchPackageWriterContext context, CancellationToken cancellationToken)
    {
        var requestCodec = CreateRequestCodec(context.Request.OodlePath);
        try
        {
            var activeCodec = requestCodec ?? codec;
            if (activeCodec is null || !activeCodec.IsAvailable)
            {
                throw new PatchBuildException("native_codec_unavailable", "Native bundle codec 不可用；正式 Bundles2 补丁需要用户提供可用的 oo2core.dll 压缩接口。");
            }

            var patchPlan = await BuildPatchPlanAsync(context, cancellationToken);
            var indexPlan = await BuildIndexPlanAsync(context.Request.ProfileId, patchPlan, cancellationToken);
            if (!indexPlan.Ready)
            {
                throw new PatchBuildException("native_index_plan_not_ready", string.Join(Environment.NewLine, indexPlan.Blockers));
            }

            var layout = WorkspaceLayout.ForProfile(workspaceRoot, context.Request.ProfileId);
            var sourceIndexPath = Path.Combine(layout.RawCacheRoot, "native", "bundles2", "index.decompressed.bin");
            if (!File.Exists(sourceIndexPath))
            {
                throw new PatchBuildException("native_index_cache_missing", "缺少解压后的 native index cache，请先建立真实索引。");
            }

            Directory.CreateDirectory(context.BundlesDirectory);
            var payload = await new NativePayloadBundleWriter().WriteAsync(
                context.BundlesDirectory,
                patchPlan,
                context.OverlayEntries,
                activeCodec,
                cancellationToken);

            var rewrittenPath = Path.Combine(context.BundlesDirectory, "_.index.rewritten.bin");
            var rewrite = await new NativeIndexRewriteDryRun().RewriteAsync(sourceIndexPath, rewrittenPath, indexPlan, cancellationToken);
            if (!rewrite.Ok)
            {
                throw new PatchBuildException("native_index_rewrite_failed", string.Join(Environment.NewLine, rewrite.Warnings));
            }

            var indexPath = Path.Combine(context.BundlesDirectory, "_.index.bin");
            await new NativeIndexBundleWriter().WriteAsync(rewrittenPath, indexPath, activeCodec, cancellationToken);
            File.Delete(rewrittenPath);
            var verification = await new PatchPackageVerifier(activeCodec).VerifyNativeAsync(
                context.BundlesDirectory,
                context.Request.BundleName,
                cancellationToken);
            if (!verification.Ok)
            {
                throw new PatchBuildException("native_package_verify_failed", string.Join(Environment.NewLine, verification.Warnings));
            }

            return new PatchPackageWriteResult(
                indexPath,
                payload.BundlePath,
                PatchBuildMode.NativeBundles2,
                [$"已生成并验证 Native Bundles2 补丁包：{verification.PatchedFileRecords} 条 index 记录指向 patch bundle。", "安装前仍建议备份原客户端 Bundles2。"]);
        }
        finally
        {
            if (requestCodec is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static Task<NativePatchPlanResponse> BuildPatchPlanAsync(PatchPackageWriterContext context, CancellationToken cancellationToken)
    {
        var items = new List<NativePatchPlanItemDto>(context.OverlayEntries.Count);
        var blockers = new List<string>();
        long offset = 0;
        foreach (var entry in context.OverlayEntries.OrderBy(item => item.NormalizedPath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var blocker = File.Exists(entry.OverlayPath) ? null : "overlay 文件不存在。";
            if (blocker is not null)
            {
                blockers.Add($"{entry.VirtualPath}: {blocker}");
            }

            items.Add(new NativePatchPlanItemDto(
                entry.VirtualPath,
                context.Request.BundleName,
                offset,
                entry.OverlaySize,
                entry.OverlayHash,
                RequiresIndexUpdate: true,
                blocker));
            offset += entry.OverlaySize;
        }

        return Task.FromResult(new NativePatchPlanResponse(
            context.Request.ProfileId,
            context.Request.BundleName,
            Ready: blockers.Count == 0 && items.Count > 0,
            items.Count,
            items,
            blockers,
            []));
    }

    private async Task<NativeIndexRewritePlanResponse> BuildIndexPlanAsync(
        string profileId,
        NativePatchPlanResponse patchPlan,
        CancellationToken cancellationToken)
    {
        var items = new List<NativeIndexRewriteItemDto>(patchPlan.Items.Count);
        var blockers = new List<string>(patchPlan.Blockers);
        foreach (var item in patchPlan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var blocker = item.Blocker;
            string? pathHash = null;
            string? originalBundleName = null;
            long? originalOffset = null;
            long? originalSize = null;
            if (blocker is null)
            {
                var resource = await resourceLookup.GetByPathAsync(profileId, item.VirtualPath, cancellationToken);
                if (resource is null)
                {
                    blocker = "资源索引中不存在该路径。";
                }
                else if (!NativeBundleLocationParser.TryParse(resource.PhysicalPath, out var location))
                {
                    blocker = "资源索引不是 Native Bundles2 位置。";
                }
                else
                {
                    pathHash = $"0x{NativeIndexPathResolver.MurmurHash64A(System.Text.Encoding.UTF8.GetBytes(resource.NormalizedPath)):x16}";
                    originalBundleName = location.BundleName;
                    originalOffset = location.Offset;
                    originalSize = location.Size;
                }
            }

            if (blocker is not null)
            {
                blockers.Add($"{item.VirtualPath}: {blocker}");
            }

            items.Add(new NativeIndexRewriteItemDto(
                item.VirtualPath,
                item.BundleName,
                item.Offset,
                item.Size,
                item.OverlayHash,
                pathHash,
                originalBundleName,
                originalOffset,
                originalSize,
                blocker));
        }

        return new NativeIndexRewritePlanResponse(profileId, patchPlan.Ready && blockers.Count == 0, items.Count, items, blockers, []);
    }

    private static INativeBundleCodec? CreateRequestCodec(string? oodlePath)
    {
        if (string.IsNullOrWhiteSpace(oodlePath))
        {
            return null;
        }

        if (string.Equals(oodlePath, "__copy__", StringComparison.Ordinal))
        {
            return new CopyNativeBundleCodec();
        }

        var codec = NativeOodleCompressCodec.TryCreate(oodlePath, out var warning);
        if (codec is null)
        {
            throw new PatchBuildException("native_codec_unavailable", warning);
        }

        return codec;
    }
}
