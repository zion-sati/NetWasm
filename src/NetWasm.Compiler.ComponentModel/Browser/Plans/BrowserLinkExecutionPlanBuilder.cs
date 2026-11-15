// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

public sealed record BrowserLinkExecutionPlan(BrowserLinkPlan Plan,
    ImmutableDictionary<int, BrowserLinkToolInvocation> Invocations);

public interface IBrowserLinkExecutionPlanBuilder
{
    BrowserLinkExecutionPlan Build(BrowserLinkPlan plan);
}

public sealed class BrowserLinkExecutionPlanBuilder(IBrowserLinkPlanValidator validator,
    IBrowserLinkOperationInspector inspector, IBrowserLinkToolMaterializer materializer) : IBrowserLinkExecutionPlanBuilder
{
    private readonly IBrowserLinkPlanValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    private readonly IBrowserLinkOperationInspector _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    private readonly IBrowserLinkToolMaterializer _materializer = materializer ?? throw new ArgumentNullException(nameof(materializer));

    public BrowserLinkExecutionPlan Build(BrowserLinkPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _validator.Validate(plan);
        var invocations = ImmutableDictionary.CreateBuilder<int, BrowserLinkToolInvocation>();
        for (var index = 0; index < plan.Operations.Length; index++)
        {
            var operation = plan.Operations[index];
            if (_inspector.Inspect(operation).ToolId is not null)
                invocations.Add(index, _materializer.Materialize(operation));
        }
        return new(plan, invocations.ToImmutable());
    }
}
