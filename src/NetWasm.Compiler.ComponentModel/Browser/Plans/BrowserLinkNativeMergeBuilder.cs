// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

internal sealed class BrowserLinkNativeMergeBuilder(IBrowserLinkOperationInspector inspector) : IBrowserLinkNativeToolBuilder
{
    private readonly IBrowserLinkOperationInspector _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    public BrowserLinkToolOperation Build(ImmutableArray<string> arguments)
    {
        var outputIndex = arguments.IsDefault ? -1 : arguments.IndexOf("--output");
        if (outputIndex < 2 || outputIndex % 2 != 0 || outputIndex + 1 >= arguments.Length)
            throw new ArgumentException("A merge invocation requires explicit path operands.", nameof(arguments));
        var operation = new BrowserLinkToolOperation("wasm-merge", arguments,
            [.. arguments.Take(outputIndex).Where((_, index) => index % 2 == 0)], [arguments[outputIndex + 1]]);
        _inspector.Inspect(operation);
        return operation;
    }
}
