using System.Text;

namespace PoeStudio.Core.Native;

public enum NativeIndexParseStatus
{
    Parsed = 0,
    Missing = 1,
    InvalidData = 2
}

public sealed record NativeIndexParseResult(
    bool Ok,
    NativeIndexParseStatus Status,
    int BundleCount,
    int FileCount,
    int DirectoryCount,
    IReadOnlyList<NativeBundleRecord> Bundles,
    IReadOnlyList<NativeFileRecord> Files,
    IReadOnlyList<NativeDirectoryRecord> Directories,
    long DirectoryBundleDataOffset,
    long DirectoryBundleDataSize,
    IReadOnlyList<string> Warnings);

public sealed record NativeBundleRecord(
    int Index,
    string Path,
    int UncompressedSize);

public sealed record NativeFileRecord(
    ulong PathHash,
    int BundleIndex,
    int Offset,
    int Size);

public sealed record NativeDirectoryRecord(
    ulong PathHash,
    int Offset,
    int Size,
    int RecursiveSize);

public sealed class NativeIndexRecordParser
{
    public async Task<NativeIndexParseResult> ParseAsync(string decompressedIndexPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(decompressedIndexPath) || !File.Exists(decompressedIndexPath))
        {
            return Invalid(NativeIndexParseStatus.Missing, ["解压后的 index cache 不存在。"]);
        }

        try
        {
            await using var stream = File.Open(decompressedIndexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream, Encoding.UTF8);

            var bundleCount = ReadNonNegativeInt32(reader, stream, "bundle count");
            var bundles = new List<NativeBundleRecord>(bundleCount);
            for (var i = 0; i < bundleCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pathLength = ReadNonNegativeInt32(reader, stream, "bundle path length");
                EnsureAvailable(stream, pathLength + sizeof(int), "bundle record");
                var pathBytes = reader.ReadBytes(pathLength);
                var path = Encoding.UTF8.GetString(pathBytes);
                var uncompressedSize = reader.ReadInt32();
                bundles.Add(new NativeBundleRecord(i, path, uncompressedSize));
            }

            var fileCount = ReadNonNegativeInt32(reader, stream, "file count");
            var files = new List<NativeFileRecord>(fileCount);
            for (var i = 0; i < fileCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureAvailable(stream, sizeof(ulong) + sizeof(int) * 3, "file record");
                var hash = reader.ReadUInt64();
                var bundleIndex = reader.ReadInt32();
                var offset = reader.ReadInt32();
                var size = reader.ReadInt32();
                if (bundleIndex < 0 || bundleIndex >= bundleCount || offset < 0 || size < 0)
                {
                    return Invalid(NativeIndexParseStatus.InvalidData, [$"file record 不合法：bundleIndex={bundleIndex}, offset={offset}, size={size}。"]);
                }

                files.Add(new NativeFileRecord(hash, bundleIndex, offset, size));
            }

            var directoryCount = ReadNonNegativeInt32(reader, stream, "directory count");
            var directories = new List<NativeDirectoryRecord>(directoryCount);
            for (var i = 0; i < directoryCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureAvailable(stream, sizeof(ulong) + sizeof(int) * 3, "directory record");
                directories.Add(new NativeDirectoryRecord(reader.ReadUInt64(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
            }

            var directoryBundleDataOffset = stream.Position;
            var directoryBundleDataSize = stream.Length - stream.Position;
            return new NativeIndexParseResult(
                Ok: true,
                NativeIndexParseStatus.Parsed,
                bundleCount,
                fileCount,
                directoryCount,
                bundles,
                files,
                directories,
                directoryBundleDataOffset,
                directoryBundleDataSize,
                Warnings: []);
        }
        catch (EndOfStreamException ex)
        {
            return Invalid(NativeIndexParseStatus.InvalidData, [ex.Message]);
        }
        catch (InvalidDataException ex)
        {
            return Invalid(NativeIndexParseStatus.InvalidData, [ex.Message]);
        }
    }

    private static int ReadNonNegativeInt32(BinaryReader reader, Stream stream, string label)
    {
        EnsureAvailable(stream, sizeof(int), label);
        var value = reader.ReadInt32();
        if (value < 0)
        {
            throw new InvalidDataException($"{label} 不能为负数。");
        }

        return value;
    }

    private static void EnsureAvailable(Stream stream, int byteCount, string label)
    {
        if (byteCount < 0 || stream.Length - stream.Position < byteCount)
        {
            throw new EndOfStreamException($"{label} 数据不完整。");
        }
    }

    private static NativeIndexParseResult Invalid(NativeIndexParseStatus status, IReadOnlyList<string> warnings)
    {
        return new NativeIndexParseResult(
            Ok: false,
            status,
            BundleCount: 0,
            FileCount: 0,
            DirectoryCount: 0,
            Bundles: [],
            Files: [],
            Directories: [],
            DirectoryBundleDataOffset: 0,
            DirectoryBundleDataSize: 0,
            warnings);
    }
}
