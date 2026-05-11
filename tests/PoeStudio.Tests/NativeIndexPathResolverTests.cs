using System.Text;
using PoeStudio.Core.Native;

namespace PoeStudio.Tests;

public sealed class NativeIndexPathResolverTests
{
    [Fact]
    public void MurmurHash64A_matches_root_sentinel_for_empty_path()
    {
        Assert.Equal(0xF42A94E69CFF42FEUL, NativeIndexPathResolver.MurmurHash64A(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Resolve_maps_direct_and_prefixed_paths_to_hashes()
    {
        var directPath = "metadata/items/ring.ot";
        var prefixedPath = "metadata/items/amulet.ot";
        var files = new[]
        {
            new NativeFileRecord(NativeIndexPathResolver.MurmurHash64A(Encoding.UTF8.GetBytes(directPath)), 0, 0, 10),
            new NativeFileRecord(NativeIndexPathResolver.MurmurHash64A(Encoding.UTF8.GetBytes(prefixedPath)), 0, 10, 20)
        };
        var directories = new[]
        {
            new NativeDirectoryRecord(0xF42A94E69CFF42FEUL, 0, 0, 0)
        };
        var directoryData = BuildDirectoryData();
        var resolver = new NativeIndexPathResolver();

        var result = resolver.Resolve(files, directories, directoryData);

        Assert.Equal(2, result.ResolvedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(directPath, result.Paths[files[0].PathHash]);
        Assert.Equal(prefixedPath, result.Paths[files[1].PathHash]);
    }

    [Fact]
    public void Resolve_counts_failed_paths_when_hash_not_found()
    {
        var files = Array.Empty<NativeFileRecord>();
        var directories = new[]
        {
            new NativeDirectoryRecord(0xF42A94E69CFF42FEUL, 0, 0, 0)
        };
        var resolver = new NativeIndexPathResolver();

        var result = resolver.Resolve(files, directories, BuildDirectoryData());

        Assert.Equal(0, result.ResolvedCount);
        Assert.Equal(2, result.FailedCount);
    }

    [Fact]
    public void Resolve_treats_negative_reference_as_direct_path()
    {
        var directPath = "metadata/items/ring.ot";
        var files = new[]
        {
            new NativeFileRecord(NativeIndexPathResolver.MurmurHash64A(Encoding.UTF8.GetBytes(directPath)), 0, 0, 10)
        };
        var directories = new[]
        {
            new NativeDirectoryRecord(0xF42A94E69CFF42FEUL, 0, 0, 0)
        };
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(-1);
            WriteNullTerminated(writer, directPath);
        }

        var result = new NativeIndexPathResolver().Resolve(files, directories, stream.ToArray());

        Assert.Equal(1, result.ResolvedCount);
        Assert.Equal(directPath, result.Paths[files[0].PathHash]);
    }

    private static byte[] BuildDirectoryData()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(0);
        writer.Write(1);
        WriteNullTerminated(writer, "metadata/items/");
        writer.Write(0);
        writer.Write(1);
        WriteNullTerminated(writer, "amulet.ot");
        writer.Write(2);
        WriteNullTerminated(writer, "metadata/items/ring.ot");
        return stream.ToArray();
    }

    private static void WriteNullTerminated(BinaryWriter writer, string value)
    {
        writer.Write(Encoding.UTF8.GetBytes(value));
        writer.Write((byte)0);
    }
}
