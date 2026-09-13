using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class RuntimeIntrinsicRegistryFactory : IRuntimeIntrinsicRegistryFactory
{
    public IRuntimeIntrinsicRegistry Create(
        ISymbolFormatter symbols,
        IEnumerable<MethodDefinitionModel> methods) =>
        new RuntimeIntrinsicRegistry(symbols, methods);
}
