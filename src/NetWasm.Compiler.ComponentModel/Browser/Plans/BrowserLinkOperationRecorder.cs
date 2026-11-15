// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

internal interface IBrowserLinkOperationRecorder
{
    void Record(BrowserLinkOperation operation);
}

internal sealed class BrowserLinkOperationRecorder(BrowserLinkCaptureState state,
    IBrowserLinkOperationInspector inspector) : IBrowserLinkOperationRecorder
{
    private readonly BrowserLinkCaptureState _state = state ?? throw new ArgumentNullException(nameof(state));
    private readonly IBrowserLinkOperationInspector _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));

    public void Record(BrowserLinkOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var contract = _inspector.Inspect(operation);
        foreach (var read in contract.Reads)
        {
            if (!_state.Available.Contains(read))
                throw new InvalidOperationException("The link producer reads an unavailable artifact.");
        }
        var writes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var write in contract.Writes)
        {
            if ((!_state.Outputs.Contains(write) && !_state.TemporaryPaths.Contains(write)) || !writes.Add(write))
                throw new InvalidOperationException("The link producer writes an unowned or repeated artifact.");
        }
        foreach (var consumed in contract.Consumes)
        {
            if (!_state.TemporaryPaths.Contains(consumed) || !contract.Reads.Contains(consumed))
                throw new InvalidOperationException("Only a read owned temporary may be consumed.");
        }
        _state.Operations.Add(operation);
        _state.Available.ExceptWith(contract.Consumes);
        _state.Available.UnionWith(writes);
    }
}
