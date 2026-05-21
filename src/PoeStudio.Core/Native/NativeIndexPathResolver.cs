using System.Buffers.Binary;
using System.Text;

namespace PoeStudio.Core.Native;

public sealed record NativePathResolveResult(
    int ResolvedCount,
    int FailedCount,
    IReadOnlyDictionary<ulong, string> Paths,
    IReadOnlyList<string> Warnings);

public sealed class NativeIndexPathResolver
{
    private const ulong MurmurRootSentinel = 0xF42A94E69CFF42FEUL;

    public NativePathResolveResult Resolve(
        IReadOnlyList<NativeFileRecord> files,
        IReadOnlyList<NativeDirectoryRecord> directories,
        ReadOnlySpan<byte> directoryData)
    {
        var fileHashes = files.ToDictionary(file => file.PathHash);
        var paths = new Dictionary<ulong, string>();
        var failed = 0;
        var warnings = new List<string>();

        foreach (var directory in directories)
        {
            if (directory.Offset < 0 || directory.Size < 0 || directory.Offset + directory.Size > directoryData.Length)
            {
                warnings.Add($"跳过非法 directory record：offset={directory.Offset}, size={directory.Size}。");
                continue;
            }

            var temp = new List<byte[]>();
            var baseMode = false;
            var span = directoryData.Slice(directory.Offset, directory.Size == 0 ? directoryData.Length - directory.Offset : directory.Size);
            var cursor = 0;

            while (cursor <= span.Length - sizeof(int))
            {
                var index = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(cursor, sizeof(int)));
                cursor += sizeof(int);
                if (index == 0)
                {
                    baseMode = !baseMode;
                    if (baseMode)
                    {
                        temp.Clear();
                    }

                    continue;
                }

                var strEnd = span[cursor..].IndexOf((byte)0);
                if (strEnd < 0)
                {
                    warnings.Add("directory path stream 缺少 null terminator。");
                    break;
                }

                var raw = span.Slice(cursor, strEnd).ToArray();
                cursor += strEnd + 1;
                index -= 1;

                byte[] pathBytes;
                if (index >= 0 && index < temp.Count)
                {
                    pathBytes = [.. temp[index], .. raw];
                    if (baseMode)
                    {
                        temp.Add(pathBytes);
                        continue;
                    }
                }
                else
                {
                    pathBytes = raw;
                    if (baseMode)
                    {
                        temp.Add(pathBytes);
                        continue;
                    }
                }

                var hash = MurmurHash64A(pathBytes);
                if (fileHashes.ContainsKey(hash))
                {
                    paths[hash] = Encoding.UTF8.GetString(pathBytes);
                }
                else
                {
                    failed++;
                }
            }
        }

        return new NativePathResolveResult(paths.Count, failed, paths, warnings);
    }

    public static ulong MurmurHash64A(ReadOnlySpan<byte> utf8Name, ulong seed = 0x1337B33F)
    {
        if (utf8Name.IsEmpty)
        {
            return MurmurRootSentinel;
        }

        if (utf8Name[^1] == (byte)'/')
        {
            utf8Name = utf8Name[..^1];
        }

        const ulong m = 0xC6A4A7935BD1E995UL;
        const int r = 47;

        unchecked
        {
            seed ^= (ulong)utf8Name.Length * m;
            var offset = 0;
            while (utf8Name.Length - offset >= sizeof(ulong))
            {
                var k = BinaryPrimitives.ReadUInt64LittleEndian(utf8Name.Slice(offset, sizeof(ulong)));
                k *= m;
                k ^= k >> r;
                k *= m;
                seed ^= k;
                seed *= m;
                offset += sizeof(ulong);
            }

            var remaining = utf8Name.Length - offset;
            if (remaining != 0)
            {
                ulong tail = 0;
                for (var i = 0; i < remaining; i++)
                {
                    tail |= (ulong)utf8Name[offset + i] << (i * 8);
                }

                seed ^= tail;
                seed *= m;
            }

            seed ^= seed >> r;
            seed *= m;
            seed ^= seed >> r;
            return seed;
        }
    }
}
