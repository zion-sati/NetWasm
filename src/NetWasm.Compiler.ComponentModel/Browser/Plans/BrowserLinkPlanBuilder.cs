// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

public interface IBrowserLinkPlanBuilder
{
    BrowserLinkPlan Build(ImmutableArray<BrowserLinkOperation> operations, ImmutableArray<string> inputs,
        ImmutableArray<string> outputs, ImmutableArray<string> temporaryPaths);
}

public sealed class BrowserLinkPlanBuilder(IBrowserLinkOperationInspector inspector, IBrowserLinkPlanValidator validator)
    : IBrowserLinkPlanBuilder
{
    private readonly IBrowserLinkOperationInspector _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    private readonly IBrowserLinkPlanValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));

    public BrowserLinkPlan Build(ImmutableArray<BrowserLinkOperation> operations, ImmutableArray<string> inputs,
        ImmutableArray<string> outputs, ImmutableArray<string> temporaryPaths)
    {
        if (operations.IsDefaultOrEmpty)
            throw new ArgumentException("A captured operation sequence is required.", nameof(operations));
        var capabilities = operations.Select(operation => _inspector.Inspect(operation).Capability).Distinct(StringComparer.Ordinal);
        var plan = new BrowserLinkPlan(BrowserLinkPlan.CurrentSchemaVersion, [.. capabilities], inputs, outputs,
            temporaryPaths, operations);
        _validator.Validate(plan);
        return plan;
    }
}
