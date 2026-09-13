using System;

namespace NetWasm.Compiler.ControlFlow;

public sealed class ControlFlowGraphBuilderFactory : IControlFlowGraphBuilderFactory
{
    private readonly ICilExceptionRegionValidator _exceptionRegions;

    public ControlFlowGraphBuilderFactory()
        : this(new CilExceptionRegionValidator())
    {
    }

    public ControlFlowGraphBuilderFactory(ICilExceptionRegionValidator exceptionRegions)
    {
        _exceptionRegions = exceptionRegions ??
            throw new ArgumentNullException(nameof(exceptionRegions));
    }

    public IControlFlowGraphBuilder Create() =>
        new CilControlFlowGraphBuilder(_exceptionRegions);
}
