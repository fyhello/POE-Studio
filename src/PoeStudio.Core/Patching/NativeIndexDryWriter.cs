using System.Text;
using PoeStudio.Contracts;

namespace PoeStudio.Core.Patching;

public sealed record NativeIndexDryDocument(
    string Magic,
    int Version,
    string ProfileId,
    IReadOnlyList<NativeIndexRewriteItemDto> Items);

public sealed class NativeIndexDryWriter
{
    private const string Magic = "POESTUDIO-NATIVE-DRY-INDEX";
    private const int Version = 1;

    public async Task WriteAsync(
        string path,
        NativeIndexRewritePlanResponse plan,
        CancellationToken cancellationToken)
    {
        if (!plan.Ready)
        {
            throw new InvalidOperationException("Native index 重写计划未就绪。");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        WriteString(writer, Magic);
        writer.Write(Version);
        WriteString(writer, plan.ProfileId);
        writer.Write(plan.Items.Count);

        foreach (var item in plan.Items
            .OrderBy(item => item.BundleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Offset)
            .ThenBy(item => item.VirtualPath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteString(writer, item.VirtualPath);
            WriteString(writer, item.BundleName);
            writer.Write(item.Offset);
            writer.Write(item.Size);
            WriteString(writer, item.OverlayHash);
        }
    }

    public static async Task<NativeIndexDryDocument> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        var magic = ReadString(reader);
        var version = reader.ReadInt32();
        var profileId = ReadString(reader);
        var count = reader.ReadInt32();
        if (count < 0)
        {
            throw new InvalidDataException("Native dry index item count is invalid.");
        }

        var items = new List<NativeIndexRewriteItemDto>(count);
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(new NativeIndexRewriteItemDto(
                ReadString(reader),
                ReadString(reader),
                reader.ReadInt64(),
                reader.ReadInt64(),
                ReadString(reader)));
        }

        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException("Native dry index contains trailing bytes.");
        }

        return new NativeIndexDryDocument(magic, version, profileId, items);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0)
        {
            throw new InvalidDataException("String length is invalid.");
        }

        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new EndOfStreamException("Unexpected end of native dry index.");
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
