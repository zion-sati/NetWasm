namespace System.Runtime.InteropServices.Input;

using System.Runtime.InteropServices.Output;
using System.Runtime.InteropServices.Streams;
using System.Runtime.InteropServices.WebAssembly;

// The wasi:cli/stdin bindings behind the standard input reader's invoker contracts, one
// contract each.
internal sealed class WasiStandardInputOpenInvoker : IWasiStandardInputOpenInvoker
{
    public WasiReadableStreamEnds Open()
    {
        var result = CanonicalAbi.Allocate(8, 4);
        try
        {
            WasiStandardInputImports.ReadViaStream(result);
            return new WasiReadableStreamEnds(
                CanonicalAbi.ReadInt32(result, 0),
                CanonicalAbi.ReadInt32(result, 4));
        }
        finally
        {
            CanonicalAbi.Free(result);
        }
    }
}

internal sealed class WasiStandardInputReadInvoker : IWasiByteStreamReadInvoker
{
    public int Read(int readable, nuint buffer, nuint length) =>
        WasiStandardInputImports.Read(readable, buffer, length);
}

internal sealed class WasiStandardInputReadableDropInvoker : IWasiByteStreamReadableDropInvoker
{
    public void Drop(int readable) => WasiStandardInputImports.DropReadable(readable);
}

internal sealed class WasiStandardInputResultReadInvoker : IWasiResultFutureReadInvoker
{
    public int Read(int readable, nuint result) => WasiStandardInputImports.ReadResult(readable, result);
}

internal sealed class WasiStandardInputResultDropInvoker : IWasiResultFutureDropInvoker
{
    public void Drop(int readable) => WasiStandardInputImports.DropResult(readable);
}
