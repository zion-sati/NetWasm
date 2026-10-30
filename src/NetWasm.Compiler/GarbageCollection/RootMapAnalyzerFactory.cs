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
        IReadOnlyDictionary<string, DispatchCallSiteModel> dispatchCallSites,
        ITypeClassifier typeClassifier);
}

public sealed class RootMapAnalyzerFactory(
    IRootDecisionClassifierFactory rootDecisions,
    ICalleeArgumentRootingClassifierFactory calleeArguments,
    IRootSlotAllocator rootSlots) : IRootMapAnalyzerFactory
{
    private readonly IRootDecisionClassifierFactory _rootDecisions =
        rootDecisions ?? throw new ArgumentNullException(nameof(rootDecisions));
    private readonly ICalleeArgumentRootingClassifierFactory _calleeArguments =
        calleeArguments ?? throw new ArgumentNullException(nameof(calleeArguments));
    private readonly IRootSlotAllocator _rootSlots =
        rootSlots ?? throw new ArgumentNullException(nameof(rootSlots));

    public IRootMapAnalyzer Create(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        IValueLayoutProvider layouts,
        IReadOnlyDictionary<string, DispatchCallSiteModel> dispatchCallSites,
        ITypeClassifier typeClassifier)
    {
        var continuations = new StackStorageContinuationAnalyzer();
        return new RootMapAnalyzer(
            layouts,
            new StackStorageTypeAnalyzer(types, fields, methods, layouts, continuations),
            continuations,
            new StackStorageAnchorPlanner(continuations),
            _rootDecisions.Create(types, fields, methods, dispatchCallSites, typeClassifier),
            _calleeArguments.Create(types, methods, dispatchCallSites, typeClassifier),
            _rootSlots);
    }
}
