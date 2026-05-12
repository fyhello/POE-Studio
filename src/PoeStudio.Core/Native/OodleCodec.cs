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

public sealed class NativeOodleCompressCodec : INativeBundleCodec, IDisposable
{
    private readonly NativeOodleCodec decompressCodec;
    private readonly nint libraryHandle;
    private readonly OodleLzCompress compress;
    private readonly OodleLzGetCompressedBufferSize getCompressedBufferSize;
    private bool disposed;

    private NativeOodleCompressCodec(string libraryPath)
    {
        decompressCodec = new NativeOodleCodec(libraryPath);
        libraryHandle = NativeLibrary.Load(libraryPath);
        compress = Marshal.GetDelegateForFunctionPointer<OodleLzCompress>(NativeLibrary.GetExport(libraryHandle, "OodleLZ_Compress"));
        getCompressedBufferSize = Marshal.GetDelegateForFunctionPointer<OodleLzGetCompressedBufferSize>(NativeLibrary.GetExport(libraryHandle, "OodleLZ_GetCompressedBufferSize"));
    }

    public bool IsAvailable => !disposed && decompressCodec.IsAvailable;

    public int CompressorId => 13;

    public static NativeOodleCompressCodec? TryCreate(string? libraryPath, out string warning)
    {
        warning = string.Empty;
        if (string.IsNullOrWhiteSpace(libraryPath) || !File.Exists(libraryPath))
        {
            warning = $"oo2core.dll 不存在：{libraryPath}";
            return null;
        }

        try
        {
            return new NativeOodleCompressCodec(libraryPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or EntryPointNotFoundException or BadImageFormatException or DllNotFoundException)
        {
            warning = $"无法加载 Oodle 压缩接口：{ex.Message}";
            return null;
        }
    }

    public byte[] Compress(ReadOnlySpan<byte> input)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        unsafe
        {
            var maxSize = checked((int)getCompressedBufferSize(input.Length));
            var output = new byte[maxSize];
            fixed (byte* inputPtr = input)
            fixed (byte* outputPtr = output)
            {
                var actual = (int)compress(
                    compressor: CompressorId,
                    inputPtr,
                    input.Length,
                    outputPtr,
                    level: 4,
                    opts: null,
                    dictionaryBase: null,
                    lrm: null,
                    scratchMem: null,
                    scratchSize: 0);
                if (actual <= 0 || actual > output.Length)
                {
                    throw new InvalidOperationException($"Oodle 压缩失败：{actual}");
                }

                Array.Resize(ref output, actual);
                return output;
            }
        }
    }

    public int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, int compressor)
    {
        return decompressCodec.Decompress(compressed, output, compressor);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        decompressCodec.Dispose();
        NativeLibrary.Free(libraryHandle);
        disposed = true;
    }

    private unsafe delegate nint OodleLzCompress(
        int compressor,
        byte* rawBuf,
        nint rawLen,
        byte* compBuf,
        int level,
        void* opts,
        byte* dictionaryBase,
        void* lrm,
        void* scratchMem,
        nint scratchSize);

    private delegate nint OodleLzGetCompressedBufferSize(nint rawSize);
}
