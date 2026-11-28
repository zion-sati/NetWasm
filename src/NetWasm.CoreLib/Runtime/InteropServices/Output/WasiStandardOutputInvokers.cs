// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Output;

// The wasi:cli/stdout bindings behind the output stream actors' invoker
// contracts, one contract each.
internal sealed class WasiStandardOutputStreamCreateInvoker : IWasiOutputStreamCreateInvoker<Streams.StandardOutputStreamKind>
{
    public long Create() => WasiStandardOutputImports.CreateStream();
}

internal sealed class WasiStandardOutputHandOffInvoker : IWasiOutputStreamHandOffInvoker<Streams.StandardOutputStreamKind>
{
    public int HandOff(int readable) => WasiStandardOutputImports.WriteViaStream(readable);
}

internal sealed class WasiStandardOutputWriteInvoker : IWasiByteStreamWriteInvoker<Streams.StandardOutputStreamKind>
{
    public int Write(int writable, nuint contents, nuint length) =>
        WasiStandardOutputImports.Write(writable, contents, length);
}

internal sealed class WasiStandardOutputWritableDropInvoker : IWasiByteStreamWritableDropInvoker<Streams.StandardOutputStreamKind>
{
    public void Drop(int writable) => WasiStandardOutputImports.DropWritable(writable);
}

internal sealed class WasiStandardOutputResultReadInvoker : IWasiResultFutureReadInvoker<Streams.StandardOutputStreamKind>
{
    public int Read(int readable, nuint result) =>
        WasiStandardOutputImports.ReadResult(readable, result);
}

internal sealed class WasiStandardOutputResultDropInvoker : IWasiResultFutureDropInvoker<Streams.StandardOutputStreamKind>
{
    public void Drop(int readable) => WasiStandardOutputImports.DropResult(readable);
}
