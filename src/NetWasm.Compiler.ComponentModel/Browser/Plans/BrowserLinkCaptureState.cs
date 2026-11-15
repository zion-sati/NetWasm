// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

internal sealed class BrowserLinkCaptureState(
    ImmutableArray<string> inputs, ImmutableArray<string> outputs, ImmutableArray<string> temporaryPaths)
{
    public List<BrowserLinkOperation> Operations { get; } = [];
    public HashSet<string> Available { get; } = new(inputs, System.StringComparer.Ordinal);
    public HashSet<string> Outputs { get; } = new(outputs, System.StringComparer.Ordinal);
    public HashSet<string> TemporaryPaths { get; } = new(temporaryPaths, System.StringComparer.Ordinal);
}
