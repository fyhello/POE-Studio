using System.Security.Cryptography;
using System.Text.Json;
using PoeStudio.Contracts;
using PoeStudio.Core.Native;
using PoeStudio.Core.Resources;
using PoeStudio.Core.Workspace;

namespace PoeStudio.Core.Patching;

public interface IPatchOverlayWriter
{
    Task<OverlayEntryDto> SaveBytesAsync(
        string profileId,
        string virtualPath,
        byte[] content,
        string? BasePhysicalPath,
        bool HasBasePhysicalPath,
        CancellationToken cancellationToken);
}

public interface IPatchPathHashLookup
{
    Task<string?> FindPathByHashAsync(string profileId, ulong pathHash, CancellationToken cancellationToken);
}

public sealed class PatchOverlayDraftService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string workspaceRoot;
    private readonly IPatchOverlayWriter overlayStore;
    private readonly IPatchPathHashLookup? pathHashLookup;

    public PatchOverlayDraftService(string workspaceRoot, IPatchOverlayWriter overlayStore)
        : this(workspaceRoot, overlayStore, null)
    {
    }

    public PatchOverlayDraftService(string workspaceRoot, IPatchOverlayWriter overlayStore, IPatchPathHashLookup? pathHashLookup)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
        this.overlayStore = overlayStore;
        this.pathHashLookup = pathHashLookup;
    }

    public async Task<PatchOverlayDraftResponse> ImportDraftAsync(
        PatchOverlayDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (request.BuildId.Any(ch => !char.IsDigit(ch)))
        {
            throw new PatchBuildException("invalid_build_id", "构建编号不合法。");
        }

        var layout = WorkspaceLayout.ForProfile(workspaceRoot, request.ProfileId);
        var buildDirectory = Path.Combine(layout.BuildsRoot, request.BuildId);
        var bundlesDirectory = FindBundlesDirectory(buildDirectory);
        if (bundlesDirectory is null)
        {
            throw new PatchBuildException("build_bundles_missing", "构建输出中未找到 Bundles2 目录。");
        }

        var bundleName = ResolveBundleName(bundlesDirectory, request.BundleName);
        var indexPath = Path.Combine(bundlesDirectory, "_.index.bin");
        var bundlePath = Path.Combine(bundlesDirectory, bundleName);
        if (!File.Exists(indexPath) || !File.Exists(bundlePath))
        {
            throw new PatchBuildException("patch_files_missing", "补丁缺少 _.index.bin 或 patch bundle。");
        }

        using var codec = CreateCodec(request.OodlePath);
        if (!codec.IsAvailable)
        {
            throw new PatchBuildException("native_codec_unavailable", "Native bundle codec 不可用，无法把外部补丁转成 overlay 草稿。");
        }

        var decompressor = new NativeBundleDecompressor(codec);
        var indexBundle = decompressor.Decompress(await File.ReadAllBytesAsync(indexPath, cancellationToken));
        if (!indexBundle.Ok)
        {
            throw new PatchBuildException("patch_index_decompress_failed", string.Join(Environment.NewLine, indexBundle.Warnings));
        }

        var payloadBundle = decompressor.Decompress(await File.ReadAllBytesAsync(bundlePath, cancellationToken));
        if (!payloadBundle.Ok)
        {
            throw new PatchBuildException("patch_bundle_decompress_failed", string.Join(Environment.NewLine, payloadBundle.Warnings));
        }

        var tempIndexPath = Path.Combine(Path.GetTempPath(), "poe-studio-overlay-draft", $"{Guid.NewGuid():N}.index.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(tempIndexPath)!);
        try
        {
            await File.WriteAllBytesAsync(tempIndexPath, indexBundle.Data, cancellationToken);
            var parsed = await new NativeIndexRecordParser().ParseAsync(tempIndexPath, cancellationToken);
            if (!parsed.Ok)
            {
                throw new PatchBuildException("patch_index_parse_failed", string.Join(Environment.NewLine, parsed.Warnings));
            }

            var bundleRecordPath = BundleFileNameToRecordPath(bundleName);
            var patchBundleIndexes = parsed.Bundles
                .Where(bundle => string.Equals(bundle.Path, bundleRecordPath, StringComparison.OrdinalIgnoreCase))
                .Select(bundle => bundle.Index)
                .ToHashSet();
            var records = parsed.Files
                .Where(file => patchBundleIndexes.Contains(file.BundleIndex))
                .OrderBy(file => file.Offset)
                .Take(Math.Clamp(request.Take, 1, 5000))
                .ToArray();

            var warnings = new List<string>();
            var items = new List<PatchOverlayDraftItemDto>();
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (record.Offset < 0 || record.Size < 0 || record.Offset + record.Size > payloadBundle.Data.Length)
                {
                    warnings.Add($"跳过非法 payload 切片：offset={record.Offset}, size={record.Size}。");
                    continue;
                }

                var virtualPath = await ResolveVirtualPathAsync(request.ProfileId, record.PathHash, cancellationToken);
                var content = payloadBundle.Data.AsSpan(record.Offset, record.Size).ToArray();
                var entry = await overlayStore.SaveBytesAsync(
                    request.ProfileId,
                    virtualPath,
                    content,
                    BasePhysicalPath: null,
                    HasBasePhysicalPath: false,
                    cancellationToken);
                items.Add(new PatchOverlayDraftItemDto(
                    virtualPath,
                    record.Offset,
                    record.Size,
                    entry.OverlayPath,
                    entry.OverlayHash,
                    PatchRiskClassifier.Classify(virtualPath)));
            }

            var kindCounts = items
                .GroupBy(item => ResourceClassifier.Classify(item.VirtualPath))
                .ToDictionary(group => group.Key, group => group.Count());
            var riskCounts = items
                .GroupBy(item => item.RiskLevel)
                .ToDictionary(group => group.Key, group => group.Count());
            var draftReportPath = Path.Combine(buildDirectory, "overlay_draft_report.json");
            await WriteJsonAsync(
                draftReportPath,
                new PatchOverlayDraftReportDto(
                    request.ProfileId,
                    request.BuildId,
                    DateTimeOffset.UtcNow,
                    records.Length,
                    items.Count,
                    kindCounts,
                    riskCounts,
                    items,
                    warnings),
                cancellationToken);

            return new PatchOverlayDraftResponse(
                request.ProfileId,
                request.BuildId,
                records.Length,
                items.Count,
                kindCounts,
                riskCounts,
                draftReportPath,
                items,
                warnings);
        }
        finally
        {
            if (File.Exists(tempIndexPath))
            {
                File.Delete(tempIndexPath);
            }
        }
    }

    private async Task<string> ResolveVirtualPathAsync(string profileId, ulong pathHash, CancellationToken cancellationToken)
    {
        if (pathHashLookup is not null)
        {
            var indexedPath = await pathHashLookup.FindPathByHashAsync(profileId, pathHash, cancellationToken);
            if (!string.IsNullOrWhiteSpace(indexedPath))
            {
                return indexedPath;
            }
        }

        return $"patch/unknown/0x{pathHash:x16}.bin";
    }

    private static string? FindBundlesDirectory(string buildDirectory)
    {
        var direct = Path.Combine(buildDirectory, "Bundles2");
        if (Directory.Exists(direct))
        {
            return direct;
        }

        return Directory.Exists(buildDirectory)
            ? Directory.EnumerateDirectories(buildDirectory, "Bundles2", SearchOption.AllDirectories).OrderBy(path => path.Length).FirstOrDefault()
            : null;
    }

    private static string ResolveBundleName(string bundlesDirectory, string fallbackBundleName)
    {
        if (!string.IsNullOrWhiteSpace(fallbackBundleName) && File.Exists(Path.Combine(bundlesDirectory, fallbackBundleName)))
        {
            return fallbackBundleName;
        }

        return Directory.EnumerateFiles(bundlesDirectory, "*.bundle.bin", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderByDescending(name => string.Equals(name, "Tiny.V0.1.bundle.bin", StringComparison.OrdinalIgnoreCase))
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? fallbackBundleName;
    }

    private static IDisposableNativeBundleCodec CreateCodec(string? oodlePath)
    {
        if (string.Equals(oodlePath, "__copy__", StringComparison.Ordinal))
        {
            return new IDisposableNativeBundleCodec(new CopyNativeBundleCodec());
        }

        var codec = NativeOodleCompressCodec.TryCreate(oodlePath, out _);
        INativeBundleCodec active = codec is null ? new UnavailableNativeBundleCodec() : codec;
        return new IDisposableNativeBundleCodec(active);
    }

    private static string BundleFileNameToRecordPath(string bundleName)
    {
        return bundleName.EndsWith(".bundle.bin", StringComparison.OrdinalIgnoreCase)
            ? bundleName[..^".bundle.bin".Length]
            : bundleName;
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
    }

    private sealed class IDisposableNativeBundleCodec(INativeBundleCodec inner) : INativeBundleCodec, IDisposable
    {
        public bool IsAvailable => inner.IsAvailable;

        public int CompressorId => inner.CompressorId;

        public byte[] Compress(ReadOnlySpan<byte> input)
        {
            return inner.Compress(input);
        }

        public int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, int compressor)
        {
            return inner.Decompress(compressed, output, compressor);
        }

        public void Dispose()
        {
            if (inner is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private sealed class UnavailableNativeBundleCodec : INativeBundleCodec
    {
        public bool IsAvailable => false;

        public int CompressorId => 0;

        public byte[] Compress(ReadOnlySpan<byte> input)
        {
            throw new NotSupportedException("Native bundle codec is not available.");
        }

        public int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, int compressor)
        {
            throw new NotSupportedException("Native bundle codec is not available.");
        }
    }
}
