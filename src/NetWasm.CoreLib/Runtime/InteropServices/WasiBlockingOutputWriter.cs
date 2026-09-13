namespace System.Runtime.InteropServices;

internal sealed class WasiBlockingOutputWriter : IPlatformBlockingOutputWriter
{
    private readonly WasiOutputStreamResource _resource;
    private readonly IPlatformBlockingOutputWriterSource _writer;

    internal WasiBlockingOutputWriter(
        WasiOutputStreamResource resource,
        IPlatformBlockingOutputWriterSource writer)
    {
        _resource = resource ?? throw new ArgumentNullException();
        _writer = writer ?? throw new ArgumentNullException();
    }

    public void WriteBlocking(byte[] contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        _writer.WriteBlocking(_resource.Handle, contents);
    }
}

internal sealed class WasiBlockingOutputWriterSource : IPlatformBlockingOutputWriterSource
{
    private const int MaximumBlockingWriteLength = 4096;

    private readonly IWasiBlockingOutputInvoker _invoker;
    private readonly IPlatformOutputErrorThrower _errorThrower;

    internal WasiBlockingOutputWriterSource(
        IWasiBlockingOutputInvoker invoker,
        IPlatformOutputErrorThrower errorThrower)
    {
        _invoker = invoker ?? throw new ArgumentNullException();
        _errorThrower = errorThrower ?? throw new ArgumentNullException();
    }

    public void WriteBlocking(int handle, byte[] contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        using var buffer = WebAssembly.CanonicalAbi.AllocateElements(
            unchecked((nuint)contents.Length),
            1,
            1);
        for (var index = 0; index < contents.Length; index++)
        {
            WebAssembly.CanonicalAbi.WriteByte(
                buffer.Address,
                unchecked((nuint)index),
                contents[index]);
        }

        var result = WebAssembly.CanonicalAbi.Allocate(12, 4);
        try
        {
            if (contents.Length == 0)
            {
                InvokeChunk(handle, buffer.Address, 0, result);
                return;
            }

            for (var offset = 0; offset < contents.Length;)
            {
                var length = Math.Min(
                    MaximumBlockingWriteLength,
                    contents.Length - offset);
                InvokeChunk(
                    handle,
                    buffer.Address + unchecked((nuint)offset),
                    unchecked((nuint)length),
                    result);
                offset += length;
            }
        }
        finally
        {
            WebAssembly.CanonicalAbi.Free(result);
        }
    }

    private void InvokeChunk(
        int handle,
        nuint contents,
        nuint length,
        nuint result)
    {
        _invoker.Invoke(handle, contents, length, result);
        if (WebAssembly.CanonicalAbi.ReadByte(result, 0) != 0)
        {
            _errorThrower.Throw(result, 4);
        }
    }
}
