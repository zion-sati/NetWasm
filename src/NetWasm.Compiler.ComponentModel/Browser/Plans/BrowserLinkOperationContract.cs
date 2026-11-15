// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

public sealed record BrowserLinkOperationContract(
    string Capability,
    ImmutableArray<string> Reads,
    ImmutableArray<string> Writes,
    ImmutableArray<string> Consumes,
    bool PreservesValidation = false,
    bool ValidatesInputs = false)
{
    public string? ToolId { get; init; }
    public ImmutableArray<string> Arguments { get; init; } = [];
    public ImmutableArray<int> InputArgumentIndices { get; init; } = [];
    public ImmutableArray<int> OutputArgumentIndices { get; init; } = [];
}

public interface IBrowserLinkOperationInspector
{
    BrowserLinkOperationContract Inspect(BrowserLinkOperation operation);
}
