using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IRuntimeIntrinsicRegistryFactory
{
    IRuntimeIntrinsicRegistry Create(
        ISymbolFormatter symbols,
        IEnumerable<MethodDefinitionModel> methods);
}
