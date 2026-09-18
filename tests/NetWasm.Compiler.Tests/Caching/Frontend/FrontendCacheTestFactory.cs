using NetWasm.Compiler.Caching.Frontend;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Tests.Caching.Frontend;

internal static class FrontendCacheTestFactory
{
    internal static FrontendArtifactHydrator Hydrator(IControlFlowGraphBuilder graphBuilder) =>
        new(graphBuilder, new StructuredMethodFactory(
            new StructuredMethodValidatorFactory().Create()));

    internal static IStructuredMethodFactory StructuredMethods() =>
        new StructuredMethodFactory(new StructuredMethodValidatorFactory().Create());
}
