using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public interface IRootMapAnalyzerFactory
{
    IRootMapAnalyzer Create(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        IValueLayoutProvider layouts,
        IReadOnlyDictionary<string, DispatchCallSiteModel> dispatchCallSites);
}

public sealed class RootMapAnalyzerFactory(
    IRootDecisionClassifierFactory rootDecisions) : IRootMapAnalyzerFactory
{
    private readonly IRootDecisionClassifierFactory _rootDecisions =
        rootDecisions ?? throw new ArgumentNullException(nameof(rootDecisions));

    public IRootMapAnalyzer Create(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        IValueLayoutProvider layouts,
        IReadOnlyDictionary<string, DispatchCallSiteModel> dispatchCallSites) =>
        new RootMapAnalyzer(
            types,
            methods,
            layouts,
            _rootDecisions.Create(types, fields, methods, dispatchCallSites));
}
