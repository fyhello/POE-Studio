using System.Text;

namespace PoeStudio.Tests;

internal static class GgpkTestData
{
    public static async Task WriteTinyGgpkAsync(string path)
    {
        await using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);
        var ggpkOffset = WriteGgpk(writer);
        var rootOffset = stream.Position;
        WriteDirectoryPlaceholder(writer, "ROOT", childCount: 1, out var rootChildOffsetPosition);
        var metadataOffset = stream.Position;
        WriteDirectoryPlaceholder(writer, "metadata", childCount: 1, out var metadataChildOffsetPosition);
        var itemsOffset = stream.Position;
        WriteDirectoryPlaceholder(writer, "items", childCount: 1, out var itemsChildOffsetPosition);
        var fileOffset = stream.Position;
        WriteFile(writer, "amulet.ot", Encoding.UTF8.GetBytes("hello"));

        stream.Position = rootChildOffsetPosition;
        writer.Write(metadataOffset);
        stream.Position = metadataChildOffsetPosition;
        writer.Write(itemsOffset);
        stream.Position = itemsChildOffsetPosition;
        writer.Write(fileOffset);
        stream.Position = ggpkOffset;
        writer.Write(rootOffset);
    }

    public static async Task WriteTinyGgpkWithBundles2Async(string path, byte[] indexBundle, byte[] payloadBundle)
    {
        await using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);
        var ggpkOffset = WriteGgpk(writer);
        var rootOffset = stream.Position;
        WriteDirectoryPlaceholder(writer, "ROOT", childCount: 1, out var rootChildOffsetPosition);
        var bundles2Offset = stream.Position;
        WriteDirectoryPlaceholder(writer, "bundles2", childCount: 2, out var bundles2ChildOffsetPosition);
        var indexOffset = stream.Position;
        WriteFile(writer, "_.index.bin", indexBundle);
        var bundleOffset = stream.Position;
        WriteFile(writer, "metadata/items.bundle.bin", payloadBundle);

        stream.Position = rootChildOffsetPosition;
        writer.Write(bundles2Offset);
        stream.Position = bundles2ChildOffsetPosition;
        writer.Write(indexOffset);
        stream.Position = bundles2ChildOffsetPosition + 12;
        writer.Write(bundleOffset);
        stream.Position = ggpkOffset;
        writer.Write(rootOffset);
    }

    private static long WriteGgpk(BinaryWriter writer)
    {
        writer.Write(28);
        writer.Write(Encoding.ASCII.GetBytes("GGPK"));
        writer.Write(2);
        var rootOffsetPosition = writer.BaseStream.Position;
        writer.Write(0L);
        writer.Write(0L);
        return rootOffsetPosition;
    }

    private static void WriteDirectoryPlaceholder(
        BinaryWriter writer,
        string name,
        int childCount,
        out long childOffsetPosition)
    {
        var nameBytes = Encoding.Unicode.GetBytes(name);
        var length = 8 + 4 + 4 + 32 + nameBytes.Length + childCount * 12;
        writer.Write(length);
        writer.Write(Encoding.ASCII.GetBytes("PDIR"));
        writer.Write(name.Length);
        writer.Write(childCount);
        writer.Write(new byte[32]);
        writer.Write(nameBytes);
        childOffsetPosition = 0;
        for (var index = 0; index < childCount; index++)
        {
            writer.Write(0);
            if (index == 0)
            {
                childOffsetPosition = writer.BaseStream.Position;
            }

            writer.Write(0L);
        }
    }

    private static void WriteFile(BinaryWriter writer, string name, byte[] data)
    {
        var nameBytes = Encoding.Unicode.GetBytes(name);
        var length = 8 + 4 + 32 + nameBytes.Length + data.Length;
        writer.Write(length);
        writer.Write(Encoding.ASCII.GetBytes("FILE"));
        writer.Write(name.Length);
        writer.Write(new byte[32]);
        writer.Write(nameBytes);
        writer.Write(data);
    }
}
