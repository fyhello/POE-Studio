using System.Text;
using PoeStudio.Contracts;
using PoeStudio.Core.Resources;

namespace PoeStudio.Core.Native;

public sealed record GgpkResourceIndexResult(
    IReadOnlyList<ResourceSummaryDto> Resources,
    int TotalFiles,
    int DirectoryCount,
    GgpkBundles2CoverageDto? Bundles2Coverage,
    byte[]? DecompressedIndex,
    IReadOnlyList<string> Warnings);

public sealed class GgpkResourceIndexer
{
    private const int MaxNameBytes = 4096;
    private static readonly byte[] GgpkTag = Encoding.ASCII.GetBytes("GGPK");
    private static readonly byte[] PdirTag = Encoding.ASCII.GetBytes("PDIR");
    private static readonly byte[] FileTag = Encoding.ASCII.GetBytes("FILE");
    private static readonly byte[] FreeTag = Encoding.ASCII.GetBytes("FREE");
    private readonly IOodleCodec oodleCodec;

    public GgpkResourceIndexer()
        : this(new MissingOodleCodec())
    {
    }

    public GgpkResourceIndexer(IOodleCodec oodleCodec)
    {
        this.oodleCodec = oodleCodec;
    }

    public async Task<GgpkResourceIndexResult> IndexAsync(ClientProfileDto profile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.ContentGgpkPath) || !File.Exists(profile.ContentGgpkPath))
        {
            return new GgpkResourceIndexResult([], 0, 0, null, null, ["客户端配置缺少 Content.ggpk 路径。"]);
        }

        var warnings = new List<string>();
        var directories = new Dictionary<long, DirectoryRecord>();
        var files = new Dictionary<long, FileRecord>();
        var roots = new List<long>();
        await using (var stream = File.Open(profile.ContentGgpkPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new BinaryReader(stream, Encoding.Unicode, leaveOpen: true))
        {
            while (stream.Position + 8 <= stream.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var recordOffset = stream.Position;
                var length = reader.ReadInt32();
                var tag = reader.ReadBytes(4);
                if (length < 8 || recordOffset + length > stream.Length)
                {
                    warnings.Add($"GGPK 记录长度异常，offset={recordOffset}。");
                    break;
                }

                if (tag.SequenceEqual(GgpkTag))
                {
                    roots.AddRange(ReadGgpkRootOffsets(reader, recordOffset, length, stream.Length));
                }
                else if (tag.SequenceEqual(PdirTag))
                {
                    if (TryReadDirectory(reader, recordOffset, length, out var directory, out var warning))
                    {
                        directories[recordOffset] = directory;
                    }
                    else if (!string.IsNullOrWhiteSpace(warning))
                    {
                        warnings.Add(warning);
                    }
                }
                else if (tag.SequenceEqual(FileTag))
                {
                    if (TryReadFile(reader, recordOffset, length, out var file, out var warning))
                    {
                        files[recordOffset] = file;
                    }
                    else if (!string.IsNullOrWhiteSpace(warning))
                    {
                        warnings.Add(warning);
                    }
                }
                else if (!tag.SequenceEqual(FreeTag))
                {
                    warnings.Add($"跳过未知 GGPK 记录：{Encoding.ASCII.GetString(tag)} @ {recordOffset}。");
                }

                stream.Position = recordOffset + length;
            }
        }

        roots = roots
            .Where(directories.ContainsKey)
            .Distinct()
            .ToList();
        if (roots.Count == 0)
        {
            roots = directories
            .Where(item => string.Equals(item.Value.Name, "ROOT", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Key)
                .ToList();
        }

        if (roots.Count == 0 && directories.Count > 0)
        {
            roots = [directories.Keys.Min()];
            warnings.Add("未找到 ROOT 目录，已从首个目录记录开始解析。");
        }

        var paths = new List<(string Path, FileRecord File)>();
        foreach (var root in roots)
        {
            WalkDirectory(root, string.Empty, directories, files, paths, warnings, new HashSet<long>());
        }

        var indexedAt = DateTimeOffset.UtcNow;
        var filesByPath = paths
            .GroupBy(item => SafeNormalize(item.Path), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(group => group.Key!, group => group.First().File, StringComparer.OrdinalIgnoreCase);
        var resources = new List<ResourceSummaryDto>();
        foreach (var (path, file) in paths
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()))
        {
            string normalized;
            try
            {
                normalized = ResourcePath.Normalize(path);
            }
            catch (ArgumentException ex)
            {
                warnings.Add($"跳过无法识别的 GGPK 资源路径：{path} ({ex.Message})");
                continue;
            }

            var extension = Path.GetExtension(normalized).ToLowerInvariant();
            resources.Add(new ResourceSummaryDto(
                Id: CreateId(profile.Id, normalized),
                ProfileId: profile.Id,
                VirtualPath: normalized,
                NormalizedPath: normalized,
                Extension: extension,
                Kind: ResourceClassifier.Classify(normalized),
                Size: file.Size,
                PhysicalPath: $"ggpk://{profile.ContentGgpkPath}#offset={file.Offset}&size={file.Size}",
                SourceLayer: ResourceSourceLayer.Base,
                IndexedAt: indexedAt));
        }

        var bundles2 = await TryExpandBundles2Async(profile, filesByPath, indexedAt, warnings, cancellationToken);
        resources.AddRange(bundles2.Resources);

        return new GgpkResourceIndexResult(
            resources.OrderBy(resource => resource.NormalizedPath, StringComparer.OrdinalIgnoreCase).ToArray(),
            files.Count,
            directories.Count,
            bundles2.Coverage,
            bundles2.DecompressedIndex,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(200).ToArray());
    }

    private async Task<GgpkBundles2ExpansionResult> TryExpandBundles2Async(
        ClientProfileDto profile,
        IReadOnlyDictionary<string, FileRecord> filesByPath,
        DateTimeOffset indexedAt,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.ContentGgpkPath)
            || !filesByPath.TryGetValue("bundles2/_.index.bin", out var indexFile))
        {
            return new GgpkBundles2ExpansionResult([], null, null);
        }

        var indexBundle = await ReadSliceAsync(profile.ContentGgpkPath, indexFile, cancellationToken);
        var decompressedIndex = new NativeBundleDecompressor(oodleCodec).Decompress(indexBundle);
        if (!decompressedIndex.Ok)
        {
            warnings.AddRange(decompressedIndex.Warnings.Select(warning => $"GGPK 内嵌 Bundles2 index 不可用：{warning}"));
            return new GgpkBundles2ExpansionResult([], null, null);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), "poe-studio-ggpk-index", $"{Guid.NewGuid():N}.index.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
        try
        {
            await File.WriteAllBytesAsync(tempPath, decompressedIndex.Data, cancellationToken);
            var parsed = await new NativeIndexRecordParser().ParseAsync(tempPath, cancellationToken);
            if (!parsed.Ok)
            {
                warnings.AddRange(parsed.Warnings.Select(warning => $"GGPK 内嵌 Bundles2 index 解析失败：{warning}"));
                return new GgpkBundles2ExpansionResult([], null, null);
            }

            var directoryBundle = decompressedIndex.Data.AsSpan((int)parsed.DirectoryBundleDataOffset, (int)parsed.DirectoryBundleDataSize).ToArray();
            var directoryData = new NativeBundleDecompressor(oodleCodec).Decompress(directoryBundle);
            if (!directoryData.Ok)
            {
                warnings.AddRange(directoryData.Warnings.Select(warning => $"GGPK 内嵌 Bundles2 路径数据不可用：{warning}"));
                return new GgpkBundles2ExpansionResult([], null, null);
            }

            var paths = new NativeIndexPathResolver().Resolve(parsed.Files, parsed.Directories, directoryData.Data);
            var resources = new List<ResourceSummaryDto>(paths.ResolvedCount);
            var bundleUsage = new int[parsed.BundleCount];
            foreach (var file in parsed.Files)
            {
                if ((uint)file.BundleIndex < (uint)bundleUsage.Length)
                {
                    bundleUsage[file.BundleIndex]++;
                }
            }

            var existingBundleCount = 0;
            var missingBundleCount = 0;
            var resourcesInExistingBundles = 0;
            var resourcesInMissingBundles = 0;
            var missingByFileName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in parsed.Files)
            {
                if (!paths.Paths.TryGetValue(file.PathHash, out var virtualPath))
                {
                    continue;
                }

                string normalized;
                try
                {
                    normalized = ResourcePath.Normalize(virtualPath);
                }
                catch (ArgumentException ex)
                {
                    warnings.Add($"跳过无法识别的 GGPK 内嵌资源路径：{virtualPath} ({ex.Message})");
                    continue;
                }

                var bundle = parsed.Bundles[file.BundleIndex];
                var bundlePath = ResourcePath.Normalize($"bundles2/{bundle.Path}.bundle.bin");
                if (!filesByPath.TryGetValue(bundlePath, out var bundleFile))
                {
                    warnings.Add($"GGPK 内嵌 bundle 缺失：{bundlePath}");
                    AddMissingBundleUsage(missingByFileName, bundlePath, 1);
                    continue;
                }

                var extension = Path.GetExtension(normalized).ToLowerInvariant();
                resources.Add(new ResourceSummaryDto(
                    Id: CreateId(profile.Id, $"bundles2:{normalized}"),
                    ProfileId: profile.Id,
                    VirtualPath: normalized,
                    NormalizedPath: normalized,
                    Extension: extension,
                    Kind: ResourceClassifier.Classify(normalized),
                    Size: file.Size,
                    PhysicalPath: $"ggpk-bundles2://{profile.ContentGgpkPath}#bundleOffset={bundleFile.Offset}&bundleSize={bundleFile.Size}&offset={file.Offset}&size={file.Size}&bundlePath={Uri.EscapeDataString(bundlePath)}",
                    SourceLayer: ResourceSourceLayer.Base,
                    IndexedAt: indexedAt));
            }

            for (var i = 0; i < parsed.Bundles.Count; i++)
            {
                var bundlePath = ResourcePath.Normalize($"bundles2/{parsed.Bundles[i].Path}.bundle.bin");
                if (filesByPath.ContainsKey(bundlePath))
                {
                    existingBundleCount++;
                    resourcesInExistingBundles += bundleUsage[i];
                }
                else
                {
                    missingBundleCount++;
                    resourcesInMissingBundles += bundleUsage[i];
                }
            }

            if (paths.FailedCount > 0)
            {
                warnings.Add($"{paths.FailedCount} 个 GGPK 内嵌 Bundles2 文件路径未解析。");
            }

            if (missingBundleCount > 0)
            {
                warnings.Add($"GGPK 内嵌 Bundles2 缺失 {missingBundleCount}/{parsed.BundleCount} 个 bundle，对应 {resourcesInMissingBundles} 条 index 文件记录；多为 shader cache，可在覆盖统计中查看。");
            }

            var coverage = new GgpkBundles2CoverageDto(
                parsed.BundleCount,
                parsed.FileCount,
                parsed.DirectoryCount,
                paths.ResolvedCount,
                paths.FailedCount,
                existingBundleCount,
                missingBundleCount,
                resourcesInExistingBundles,
                resourcesInMissingBundles,
                missingByFileName
                    .OrderByDescending(item => item.Value)
                    .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .Select(item => new GgpkMissingBundleDto(item.Key, item.Value))
                    .ToArray());

            return new GgpkBundles2ExpansionResult(resources, coverage, decompressedIndex.Data);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }
    }

    private static void AddMissingBundleUsage(Dictionary<string, int> missingByFileName, string bundlePath, int count)
    {
        var fileName = Path.GetFileName(bundlePath);
        missingByFileName[fileName] = missingByFileName.TryGetValue(fileName, out var current)
            ? current + count
            : count;
    }

    private static async Task<byte[]> ReadSliceAsync(string path, FileRecord file, CancellationToken cancellationToken)
    {
        var data = new byte[file.Size];
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Position = file.Offset;
        var offset = 0;
        while (offset < data.Length)
        {
            var read = await stream.ReadAsync(data.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return offset == data.Length ? data : data.AsSpan(0, offset).ToArray();
    }

    private static string? SafeNormalize(string path)
    {
        try
        {
            return ResourcePath.Normalize(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool TryReadDirectory(
        BinaryReader reader,
        long recordOffset,
        long recordLength,
        out DirectoryRecord directory,
        out string? warning)
    {
        directory = default;
        warning = null;
        try
        {
            var nameLength = reader.ReadInt32();
            var childCount = reader.ReadInt32();
            _ = reader.ReadBytes(32);
            if (nameLength < 0 || nameLength > MaxNameBytes)
            {
                warning = $"PDIR 名称长度异常，offset={recordOffset}。";
                return false;
            }

            var name = ReadUtf16Name(reader, nameLength);
            if (childCount < 0 || childCount > 2_000_000)
            {
                warning = $"PDIR 子项数量异常，offset={recordOffset}。";
                return false;
            }

            var childTableOffset = recordOffset + recordLength - (childCount * 12L);
            if (childTableOffset < reader.BaseStream.Position || childTableOffset > recordOffset + recordLength)
            {
                warning = $"PDIR 子项表位置异常，offset={recordOffset}。";
                return false;
            }

            reader.BaseStream.Position = childTableOffset;
            var children = new List<long>(Math.Min(childCount, 4096));
            for (var index = 0; index < childCount; index++)
            {
                _ = reader.ReadUInt32();
                children.Add(unchecked((long)reader.ReadUInt64()));
            }

            directory = new DirectoryRecord(name, children);
            return true;
        }
        catch (EndOfStreamException)
        {
            warning = $"PDIR 记录不完整，offset={recordOffset}。";
            return false;
        }
    }

    private static bool TryReadFile(
        BinaryReader reader,
        long recordOffset,
        long recordLength,
        out FileRecord file,
        out string? warning)
    {
        file = default;
        warning = null;
        try
        {
            var nameLength = reader.ReadInt32();
            _ = reader.ReadBytes(32);
            if (nameLength < 0 || nameLength > MaxNameBytes)
            {
                warning = $"FILE 名称长度异常，offset={recordOffset}。";
                return false;
            }

            var name = ReadUtf16Name(reader, nameLength);
            var dataOffset = recordOffset + 8 + 4 + 32 + (nameLength * 2);
            var size = Math.Max(0, recordLength - (dataOffset - recordOffset));
            file = new FileRecord(name, dataOffset, size);
            return true;
        }
        catch (EndOfStreamException)
        {
            warning = $"FILE 记录不完整，offset={recordOffset}。";
            return false;
        }
    }

    private static IReadOnlyList<long> ReadGgpkRootOffsets(
        BinaryReader reader,
        long recordOffset,
        long recordLength,
        long fileLength)
    {
        var end = recordOffset + recordLength;
        if (recordLength < 20)
        {
            return [];
        }

        var roots = new List<long>();
        _ = reader.ReadInt32();
        var rootOffset = unchecked((long)reader.ReadUInt64());
        if (rootOffset > 0 && rootOffset < fileLength)
        {
            roots.Add(rootOffset);
        }

        while (reader.BaseStream.Position + 8 <= end)
        {
            var candidate = unchecked((long)reader.ReadUInt64());
            if (candidate > 0 && candidate < fileLength)
            {
                roots.Add(candidate);
            }
        }

        return roots;
    }

    private static string ReadUtf16Name(BinaryReader reader, int charCount)
    {
        var bytes = reader.ReadBytes(checked(charCount * 2));
        return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
    }

    private static void WalkDirectory(
        long directoryOffset,
        string prefix,
        IReadOnlyDictionary<long, DirectoryRecord> directories,
        IReadOnlyDictionary<long, FileRecord> files,
        List<(string Path, FileRecord File)> output,
        List<string> warnings,
        HashSet<long> seen)
    {
        if (!seen.Add(directoryOffset))
        {
            warnings.Add($"GGPK 目录循环引用，offset={directoryOffset}。");
            return;
        }

        if (!directories.TryGetValue(directoryOffset, out var directory))
        {
            warnings.Add($"GGPK 目录引用缺失，offset={directoryOffset}。");
            return;
        }

        var nextPrefix = string.IsNullOrEmpty(directory.Name) || string.Equals(directory.Name, "ROOT", StringComparison.OrdinalIgnoreCase)
            ? prefix
            : $"{prefix}{directory.Name}/";
        foreach (var child in directory.Children)
        {
            if (directories.ContainsKey(child))
            {
                WalkDirectory(child, nextPrefix, directories, files, output, warnings, seen);
            }
            else if (files.TryGetValue(child, out var file))
            {
                output.Add(($"{nextPrefix}{file.Name}", file));
            }
            else
            {
                warnings.Add($"GGPK 子项引用缺失，offset={child}。");
            }
        }

        seen.Remove(directoryOffset);
    }

    private static string CreateId(string profileId, string normalizedPath)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes($"{profileId}:ggpk:{normalizedPath}"))).ToLowerInvariant();
    }

    private readonly record struct DirectoryRecord(string Name, IReadOnlyList<long> Children);

    private readonly record struct FileRecord(string Name, long Offset, long Size);

    private sealed record GgpkBundles2ExpansionResult(
        IReadOnlyList<ResourceSummaryDto> Resources,
        GgpkBundles2CoverageDto? Coverage,
        byte[]? DecompressedIndex);
}
