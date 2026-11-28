// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Output;

// Ends the stream for a JavaScript host that runs a raw module's event loop. That host cannot
// suspend the program, so it finishes the write after the export returns, settling the write's
// future itself: the program drops both ends without reading the future, where waiting would
// throw (issue 224's exit wait).
internal sealed class WasiOutputStreamNonblockingCloser<TStream> : IWasiOutputStreamCloser<TStream>
{
    private readonly IWasiByteStreamWritableDropInvoker<TStream> _dropWritable;
    private readonly IWasiResultFutureDropInvoker<TStream> _dropResult;

    internal WasiOutputStreamNonblockingCloser(
        IWasiByteStreamWritableDropInvoker<TStream> dropWritable,
        IWasiResultFutureDropInvoker<TStream> dropResult)
    {
        _dropWritable = dropWritable ?? throw new ArgumentNullException();
        _dropResult = dropResult ?? throw new ArgumentNullException();
    }

    public void Close(WasiOutputStreamEnds ends)
    {
        _dropWritable.Drop(ends.Writable);
        _dropResult.Drop(ends.Completion);
    }
}
