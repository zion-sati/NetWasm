using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal interface ICalledMethodResolverFactory
{
    ICalledMethodResolver Create(
        IMethodRepository methods,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols);
}
