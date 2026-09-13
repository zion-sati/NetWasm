using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IImplicitExceptionDiscoveryFactory
{
    IImplicitExceptionDiscovery Create(
        IMethodRepository methods,
        ISymbolFormatter symbols,
        IRuntimeIntrinsicRegistry intrinsics);
}
