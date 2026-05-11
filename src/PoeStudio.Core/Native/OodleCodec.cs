namespace PoeStudio.Core.Native;

using System.Runtime.InteropServices;

public interface IOodleCodec
{
    bool IsAvailable { get; }

    int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, int compressor);
}

public delegate IOodleCodec OodleCodecFactory(string oodlePath);

public sealed class MissingOodleCodec : IOodleCodec
{
    public bool IsAvailable => false;

    public int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, int compressor)
    {
        throw new NotSupportedException("Oodle codec is not available.");
    }
}

public sealed class NativeOodleCodec : IOodleCodec, IDisposable
{
    private readonly nint libraryHandle;
    private readonly OodleLzDecompress decompress;
    private bool disposed;

    public NativeOodleCodec(string libraryPath)
    {
        if (string.IsNullOrWhiteSpace(libraryPath) || !File.Exists(libraryPath))
        {
            throw new FileNotFoundException("oo2core.dll 不存在。", libraryPath);
        }

        libraryHandle = NativeLibrary.Load(libraryPath);
        var symbol = NativeLibrary.GetExport(libraryHandle, "OodleLZ_Decompress");
        decompress = Marshal.GetDelegateForFunctionPointer<OodleLzDecompress>(symbol);
    }

    public bool IsAvailable => !disposed;

    public int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, int compressor)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        unsafe
        {
            fixed (byte* compressedPtr = compressed)
            fixed (byte* outputPtr = output)
            {
                return (int)decompress(
                    compressedPtr,
                    compressed.Length,
                    outputPtr,
                    output.Length,
                    fuzzSafe: 1,
                    checkCrc: 0,
                    verbosity: 0,
                    dictionaryBase: null,
                    dictionarySize: 0,
                    fpCallback: null,
                    callbackUserData: null,
                    decoderMemory: null,
                    decoderMemorySize: 0,
                    threadPhase: 3);
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        NativeLibrary.Free(libraryHandle);
        disposed = true;
    }

    private unsafe delegate nint OodleLzDecompress(
        byte* buffer,
        nint bufferSize,
        byte* output,
        nint outputSize,
        int fuzzSafe,
        int checkCrc,
        int verbosity,
        byte* dictionaryBase,
        nint dictionarySize,
        void* fpCallback,
        void* callbackUserData,
        void* decoderMemory,
        nint decoderMemorySize,
        int threadPhase);
}
