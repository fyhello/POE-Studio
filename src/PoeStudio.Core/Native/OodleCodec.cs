namespace PoeStudio.Core.Native;

public interface IOodleCodec
{
    bool IsAvailable { get; }

    int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, int compressor);
}

public sealed class MissingOodleCodec : IOodleCodec
{
    public bool IsAvailable => false;

    public int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, int compressor)
    {
        throw new NotSupportedException("Oodle codec is not available.");
    }
}
