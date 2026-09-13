using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public interface IRootDecisionClassifierFactory
{
    IRootDecisionClassifier Create(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        IReadOnlyDictionary<string, DispatchCallSiteModel> dispatchCallSites);
}
