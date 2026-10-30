using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.GarbageCollection;

public sealed class CalleeArgumentRootingClassifierFactory(
    IRuntimeAllocationSafepointClassifier runtimeSafepoints) :
    ICalleeArgumentRootingClassifierFactory
{
    private readonly IRuntimeAllocationSafepointClassifier _runtimeSafepoints =
        runtimeSafepoints ?? throw new ArgumentNullException(nameof(runtimeSafepoints));

    public ICalleeArgumentRootingClassifier Create(
        ITypeRepository types,
        IMethodRepository methods,
        IReadOnlyDictionary<string, DispatchCallSiteModel> dispatchCallSites,
        ITypeClassifier typeClassifier) =>
        new CalleeArgumentRootingClassifier(
            types,
            methods,
            dispatchCallSites,
            _runtimeSafepoints,
            typeClassifier,
            new MethodInitializationTriggerClassifier(types));
}
