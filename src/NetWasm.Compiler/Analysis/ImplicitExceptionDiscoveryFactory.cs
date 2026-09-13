using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class ImplicitExceptionDiscoveryFactory(
    IStringConstructionExceptionRequirementProvider stringConstruction) :
    IImplicitExceptionDiscoveryFactory
{
    public IImplicitExceptionDiscovery Create(
        IMethodRepository methods,
        ISymbolFormatter symbols,
        IRuntimeIntrinsicRegistry intrinsics) =>
        new ImplicitExceptionDiscovery(
            methods,
            symbols,
            stringConstruction,
            new RuntimeIntrinsicExceptionRequirementProvider(intrinsics));
}
