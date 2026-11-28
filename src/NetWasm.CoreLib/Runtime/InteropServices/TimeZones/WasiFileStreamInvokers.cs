// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    using System.Runtime.InteropServices.Streams;
    using System.Runtime.InteropServices.Output;

    // The wasi:filesystem read-via-stream bindings behind the file reader's
    // invoker contracts, one contract each.
    internal sealed class WasiFileStreamReadInvoker : IWasiByteStreamReadInvoker<Streams.ReadOnlyFileStreamKind>
    {
        public int Read(int readable, nuint buffer, nuint length) =>
            WasiTimeZoneImports.Read(readable, buffer, length);
    }

    internal sealed class WasiFileStreamReadableDropInvoker : IWasiByteStreamReadableDropInvoker<Streams.ReadOnlyFileStreamKind>
    {
        public void Drop(int readable) => WasiTimeZoneImports.DropReadable(readable);
    }

    internal sealed class WasiFileStreamResultReadInvoker : IWasiResultFutureReadInvoker<Streams.ReadOnlyFileStreamKind>
    {
        public int Read(int readable, nuint result) =>
            WasiTimeZoneImports.ReadResult(readable, result);
    }

    internal sealed class WasiFileStreamResultDropInvoker : IWasiResultFutureDropInvoker<Streams.ReadOnlyFileStreamKind>
    {
        public void Drop(int readable) => WasiTimeZoneImports.DropResult(readable);
    }
}
