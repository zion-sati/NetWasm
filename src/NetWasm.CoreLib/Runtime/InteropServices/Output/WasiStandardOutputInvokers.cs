namespace System.Runtime.InteropServices.Output;

// The wasi:cli/stdout bindings behind the output stream actors' invoker
// contracts, one contract each.
internal sealed class WasiStandardOutputStreamCreateInvoker : IWasiOutputStreamCreateInvoker
{
    public long Create() => WasiStandardOutputImports.CreateStream();
}

internal sealed class WasiStandardOutputHandOffInvoker : IWasiOutputStreamHandOffInvoker
{
    public int HandOff(int readable) => WasiStandardOutputImports.WriteViaStream(readable);
}

internal sealed class WasiStandardOutputWriteInvoker : IWasiByteStreamWriteInvoker
{
    public int Write(int writable, nuint contents, nuint length) =>
        WasiStandardOutputImports.Write(writable, contents, length);
}

internal sealed class WasiStandardOutputWritableDropInvoker : IWasiByteStreamWritableDropInvoker
{
    public void Drop(int writable) => WasiStandardOutputImports.DropWritable(writable);
}

internal sealed class WasiStandardOutputResultReadInvoker : IWasiResultFutureReadInvoker
{
    public int Read(int readable, nuint result) =>
        WasiStandardOutputImports.ReadResult(readable, result);
}

internal sealed class WasiStandardOutputResultDropInvoker : IWasiResultFutureDropInvoker
{
    public void Drop(int readable) => WasiStandardOutputImports.DropResult(readable);
}
