namespace System.Runtime.InteropServices.Output;

// The wasi:cli/stderr bindings behind the output stream actors' invoker
// contracts, one contract each.
internal sealed class WasiStandardErrorStreamCreateInvoker : IWasiOutputStreamCreateInvoker
{
    public long Create() => WasiStandardErrorImports.CreateStream();
}

internal sealed class WasiStandardErrorHandOffInvoker : IWasiOutputStreamHandOffInvoker
{
    public int HandOff(int readable) => WasiStandardErrorImports.WriteViaStream(readable);
}

internal sealed class WasiStandardErrorWriteInvoker : IWasiByteStreamWriteInvoker
{
    public int Write(int writable, nuint contents, nuint length) =>
        WasiStandardErrorImports.Write(writable, contents, length);
}

internal sealed class WasiStandardErrorWritableDropInvoker : IWasiByteStreamWritableDropInvoker
{
    public void Drop(int writable) => WasiStandardErrorImports.DropWritable(writable);
}

internal sealed class WasiStandardErrorResultReadInvoker : IWasiResultFutureReadInvoker
{
    public int Read(int readable, nuint result) =>
        WasiStandardErrorImports.ReadResult(readable, result);
}

internal sealed class WasiStandardErrorResultDropInvoker : IWasiResultFutureDropInvoker
{
    public void Drop(int readable) => WasiStandardErrorImports.DropResult(readable);
}
