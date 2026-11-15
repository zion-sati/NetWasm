// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

internal interface IBrowserLinkNativeToolBuilder
{
    BrowserLinkToolOperation Build(ImmutableArray<string> arguments);
}

internal sealed class BrowserLinkNativeToolCapture(IBrowserLinkOperationRecorder recorder,
    FrozenDictionary<string, IBrowserLinkNativeToolBuilder> builders) : IBinaryenToolRunner
{
    private readonly IBrowserLinkOperationRecorder _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
    private readonly FrozenDictionary<string, IBrowserLinkNativeToolBuilder> _builders = builders ?? throw new ArgumentNullException(nameof(builders));

    public ToolResult Run(string toolId, ImmutableArray<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        if (!_builders.TryGetValue(toolId, out var builder))
            throw new InvalidOperationException("The browser link tool is not supported.");
        _recorder.Record(builder.Build(arguments));
        return new(0, string.Empty, string.Empty);
    }
}
