using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public interface ICalleeArgumentRootingClassifierFactory
{
    ICalleeArgumentRootingClassifier Create(
        ITypeRepository types,
        IMethodRepository methods,
        IReadOnlyDictionary<string, DispatchCallSiteModel> dispatchCallSites,
        ITypeClassifier typeClassifier);
}
