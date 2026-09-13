using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class CalledMethodResolverFactory : ICalledMethodResolverFactory
{
    public ICalledMethodResolver Create(
        IMethodRepository methods,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols) =>
        new CalledMethodResolver(methods, methodInstances, symbols);
}
