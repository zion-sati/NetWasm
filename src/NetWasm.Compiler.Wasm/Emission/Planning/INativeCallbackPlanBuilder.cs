using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface INativeCallbackPlanBuilder
{
    NativeCallbackPlan Build(
        IReadOnlyDictionary<string, MethodInstanceModel> callbacks,
        IReadOnlySet<string> addressedCallbacks,
        int firstGetterIndex);
}
