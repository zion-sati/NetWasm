// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Output;

// The wasi:cli/stderr bindings behind the output stream actors' invoker
// contracts, one contract each.
internal sealed class WasiStandardErrorStreamCreateInvoker : IWasiOutputStreamCreateInvoker<Streams.StandardErrorStreamKind>
{
    public long Create() => WasiStandardErrorImports.CreateStream();
}

internal sealed class WasiStandardErrorHandOffInvoker : IWasiOutputStreamHandOffInvoker<Streams.StandardErrorStreamKind>
{
    public int HandOff(int readable) => WasiStandardErrorImports.WriteViaStream(readable);
}

internal sealed class WasiStandardErrorWriteInvoker : IWasiByteStreamWriteInvoker<Streams.StandardErrorStreamKind>
{
    public int Write(int writable, nuint contents, nuint length) =>
        WasiStandardErrorImports.Write(writable, contents, length);
}

internal sealed class WasiStandardErrorWritableDropInvoker : IWasiByteStreamWritableDropInvoker<Streams.StandardErrorStreamKind>
{
    public void Drop(int writable) => WasiStandardErrorImports.DropWritable(writable);
}

internal sealed class WasiStandardErrorResultReadInvoker : IWasiResultFutureReadInvoker<Streams.StandardErrorStreamKind>
{
    public int Read(int readable, nuint result) =>
        WasiStandardErrorImports.ReadResult(readable, result);
}

internal sealed class WasiStandardErrorResultDropInvoker : IWasiResultFutureDropInvoker<Streams.StandardErrorStreamKind>
{
    public void Drop(int readable) => WasiStandardErrorImports.DropResult(readable);
}
