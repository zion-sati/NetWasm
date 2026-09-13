using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public sealed class RootDecisionClassifierFactory : IRootDecisionClassifierFactory
{
    public IRootDecisionClassifier Create(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        IReadOnlyDictionary<string, DispatchCallSiteModel> dispatchCallSites) =>
        new RootDecisionClassifier(types, fields, methods, dispatchCallSites);
}
