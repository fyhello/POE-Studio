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
        var requestCodec = CreateRequestCodec(context.Request.OodlePath, NativeOodleCompressCodec.MermaidCompressorId);
        var indexRequestCodec = CreateRequestCodec(context.Request.OodlePath, NativeOodleCompressCodec.KrakenCompressorId);
        try
        {
            var payloadCodec = requestCodec ?? codec;
            if (payloadCodec is null || !payloadCodec.IsAvailable)
            {
                throw new PatchBuildException("native_codec_unavailable", "Native bundle codec 不可用；正式 Bundles2 补丁需要用户提供可用的 oo2core.dll 压缩接口。");
            }

            var indexCodec = indexRequestCodec ?? payloadCodec;

            var existingBundle = await TryReadExistingTargetBundleAsync(context, payloadCodec, cancellationToken);
            var patchPlan = await BuildPatchPlanAsync(context, existingBundle?.Data.LongLength ?? 0, cancellationToken);
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
                payloadCodec,
                existingBundle?.Data,
                cancellationToken);

            var rewrittenPath = Path.Combine(context.BundlesDirectory, "_.index.rewritten.bin");
            var rewrite = await new NativeIndexRewriteDryRun().RewriteAsync(sourceIndexPath, rewrittenPath, indexPlan, cancellationToken);
            if (!rewrite.Ok)
            {
                throw new PatchBuildException("native_index_rewrite_failed", string.Join(Environment.NewLine, rewrite.Warnings));
            }

            var indexPath = Path.Combine(context.BundlesDirectory, "_.index.bin");
            await new NativeIndexBundleWriter().WriteAsync(rewrittenPath, indexPath, indexCodec, cancellationToken);
            File.Delete(rewrittenPath);
            var verification = await new PatchPackageVerifier(payloadCodec).VerifyNativeAsync(
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

            if (indexRequestCodec is IDisposable indexDisposable)
            {
                indexDisposable.Dispose();
            }
        }
    }

    private static Task<NativePatchPlanResponse> BuildPatchPlanAsync(
        PatchPackageWriterContext context,
        long baseOffset,
        CancellationToken cancellationToken)
    {
        var items = new List<NativePatchPlanItemDto>(context.OverlayEntries.Count);
        var blockers = new List<string>();
        long offset = baseOffset;
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

    private async Task<ExistingNativeBundlePayload?> TryReadExistingTargetBundleAsync(
        PatchPackageWriterContext context,
        INativeBundleCodec codec,
        CancellationToken cancellationToken)
    {
        if (context.Changes.Count == 0)
        {
            return null;
        }

        foreach (var change in context.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resource = await resourceLookup.GetByPathAsync(context.Request.ProfileId, change.VirtualPath, cancellationToken);
            if (resource is null || !NativeBundleLocationParser.TryParse(resource.PhysicalPath, out var location))
            {
                continue;
            }

            if (!string.Equals(NormalizeBundleName(location.BundleName), NormalizeBundleName(context.Request.BundleName), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var compressed = await ReadPhysicalBundleBytesAsync(context.Profile, resource.PhysicalPath, location.BundleName, cancellationToken);
            var decompressed = new NativeBundleDecompressor(codec).Decompress(compressed);
            if (!decompressed.Ok)
            {
                throw new PatchBuildException("native_existing_bundle_decompress_failed", decompressed.Warnings.FirstOrDefault() ?? $"无法解压原始 bundle：{context.Request.BundleName}");
            }

            return new ExistingNativeBundlePayload(decompressed.Data);
        }

        if (resourceLookup is IPatchBundleResourceLookup bundleLookup)
        {
            var bundleResource = await bundleLookup.FindByBundleNameAsync(context.Request.ProfileId, context.Request.BundleName, cancellationToken);
            if (bundleResource is not null)
            {
                var compressed = await ReadPhysicalBundleBytesAsync(context.Profile, bundleResource.PhysicalPath, context.Request.BundleName, cancellationToken);
                var decompressed = new NativeBundleDecompressor(codec).Decompress(compressed);
                if (!decompressed.Ok)
                {
                    throw new PatchBuildException("native_existing_bundle_decompress_failed", decompressed.Warnings.FirstOrDefault() ?? $"无法解压原始 bundle：{context.Request.BundleName}");
                }

                return new ExistingNativeBundlePayload(decompressed.Data);
            }
        }

        return null;
    }

    private static async Task<byte[]> ReadPhysicalBundleBytesAsync(
        ClientProfileDto profile,
        string? physicalPath,
        string bundleName,
        CancellationToken cancellationToken)
    {
        if (TryParseGgpkFileSlice(physicalPath, out var ggpkFilePath, out var fileOffset, out var fileSize))
        {
            return await ReadFileSliceAsync(ggpkFilePath, fileOffset, fileSize, bundleName, cancellationToken);
        }

        if (TryParseGgpkBundleSlice(physicalPath, out var ggpkPath, out var bundleOffset, out var bundleSize))
        {
            return await ReadFileSliceAsync(ggpkPath, bundleOffset, bundleSize, bundleName, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(profile.Bundles2Path))
        {
            throw new PatchBuildException("native_existing_bundle_missing", $"目标 bundle 已存在于 index，但客户端缺少 Bundles2 路径：{bundleName}");
        }

        var localBundlePath = Path.Combine(profile.Bundles2Path, bundleName.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(localBundlePath))
        {
            throw new PatchBuildException("native_existing_bundle_missing", $"找不到原始 bundle：{localBundlePath}");
        }

        return await File.ReadAllBytesAsync(localBundlePath, cancellationToken);
    }

    private static async Task<byte[]> ReadFileSliceAsync(
        string filePath,
        long offset,
        int size,
        string bundleName,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new PatchBuildException("native_existing_bundle_missing", $"找不到原始 bundle 所在文件：{filePath}");
        }

        var data = new byte[size];
        await using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Position = offset;
        var read = 0;
        while (read < data.Length)
        {
            var count = await stream.ReadAsync(data.AsMemory(read), cancellationToken);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        if (read != data.Length)
        {
            throw new PatchBuildException("native_existing_bundle_read_failed", $"原始 bundle 读取不完整：{bundleName}");
        }

        return data;
    }

    private static bool TryParseGgpkFileSlice(string? physicalPath, out string ggpkPath, out long offset, out int size)
    {
        ggpkPath = string.Empty;
        offset = 0;
        size = 0;
        const string scheme = "ggpk://";
        if (string.IsNullOrWhiteSpace(physicalPath) || !physicalPath.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = physicalPath[scheme.Length..];
        var hashIndex = rest.IndexOf('#');
        if (hashIndex <= 0 || hashIndex == rest.Length - 1)
        {
            return false;
        }

        ggpkPath = rest[..hashIndex];
        var query = rest[(hashIndex + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

        return query.TryGetValue("offset", out var offsetText)
            && query.TryGetValue("size", out var sizeText)
            && long.TryParse(offsetText, out offset)
            && int.TryParse(sizeText, out size)
            && offset >= 0
            && size >= 0
            && !string.IsNullOrWhiteSpace(ggpkPath);
    }

    private static bool TryParseGgpkBundleSlice(string? physicalPath, out string ggpkPath, out long bundleOffset, out int bundleSize)
    {
        ggpkPath = string.Empty;
        bundleOffset = 0;
        bundleSize = 0;
        const string scheme = "ggpk-bundles2://";
        if (string.IsNullOrWhiteSpace(physicalPath) || !physicalPath.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = physicalPath[scheme.Length..];
        var hashIndex = rest.IndexOf('#');
        if (hashIndex <= 0 || hashIndex == rest.Length - 1)
        {
            return false;
        }

        ggpkPath = rest[..hashIndex];
        var query = rest[(hashIndex + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

        return query.TryGetValue("bundleOffset", out var offsetText)
            && query.TryGetValue("bundleSize", out var sizeText)
            && long.TryParse(offsetText, out bundleOffset)
            && int.TryParse(sizeText, out bundleSize)
            && bundleOffset >= 0
            && bundleSize >= 0
            && !string.IsNullOrWhiteSpace(ggpkPath);
    }

    private static string NormalizeBundleName(string bundleName)
    {
        return bundleName.Replace('\\', '/')
            .TrimStart('/')
            .StartsWith("bundles2/", StringComparison.OrdinalIgnoreCase)
            ? bundleName.Replace('\\', '/').TrimStart('/')["bundles2/".Length..]
            : bundleName.Replace('\\', '/').TrimStart('/');
    }

    private sealed record ExistingNativeBundlePayload(byte[] Data);

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

    private static INativeBundleCodec? CreateRequestCodec(string? oodlePath, int compressorId)
    {
        if (string.IsNullOrWhiteSpace(oodlePath))
        {
            return null;
        }

        if (string.Equals(oodlePath, "__copy__", StringComparison.Ordinal))
        {
            return new CopyNativeBundleCodec();
        }

        var codec = NativeOodleCompressCodec.TryCreate(oodlePath, compressorId, out var warning);
        if (codec is null)
        {
            throw new PatchBuildException("native_codec_unavailable", warning);
        }

        return codec;
    }
}
