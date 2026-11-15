// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Globalization;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

public sealed record BrowserLinkArtifactBinding(string LogicalPath, string PhysicalName);

public sealed record BrowserLinkToolInvocation(
    string ToolId,
    ImmutableArray<string> Arguments,
    ImmutableArray<BrowserLinkArtifactBinding> Inputs,
    ImmutableArray<BrowserLinkArtifactBinding> Outputs);

public interface IBrowserLinkToolMaterializer
{
    BrowserLinkToolInvocation Materialize(BrowserLinkOperation operation);
}

public sealed class BrowserLinkToolMaterializer(IBrowserLinkOperationInspector inspector)
    : IBrowserLinkToolMaterializer
{
    private readonly IBrowserLinkOperationInspector _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));

    public BrowserLinkToolInvocation Materialize(BrowserLinkOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var contract = _inspector.Inspect(operation);
        if (contract.ToolId is null || contract.InputArgumentIndices.Length != contract.Reads.Length ||
            contract.OutputArgumentIndices.Length != contract.Writes.Length)
            throw new InvalidOperationException("The tool inspector must bind every artifact argument.");
        var arguments = contract.Arguments.ToBuilder();
        var inputs = ImmutableArray.CreateBuilder<BrowserLinkArtifactBinding>();
        var outputs = ImmutableArray.CreateBuilder<BrowserLinkArtifactBinding>();
        for (var index = 0; index < contract.Reads.Length; index++)
        {
            var name = "input-" + index.ToString(CultureInfo.InvariantCulture);
            arguments[contract.InputArgumentIndices[index]] = name;
            inputs.Add(new(contract.Reads[index], name));
        }
        for (var index = 0; index < contract.Writes.Length; index++)
        {
            var name = "output-" + index.ToString(CultureInfo.InvariantCulture);
            arguments[contract.OutputArgumentIndices[index]] = name;
            outputs.Add(new(contract.Writes[index], name));
        }
        return new(contract.ToolId, arguments.ToImmutable(), inputs.ToImmutable(), outputs.ToImmutable());
    }
}
