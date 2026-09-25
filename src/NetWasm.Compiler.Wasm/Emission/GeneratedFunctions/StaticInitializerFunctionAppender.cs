using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class StaticInitializerFunctionAppender(
    IStaticInitializerFunctionEmitter initializers,
    IStaticInitializationFailureEmitter failures) : IStaticInitializerFunctionAppender
{
    public void Append(IList<WasmFunctionDefinition> functions, StaticInitializerFunctionPlan? plan)
    {
        if (plan is null)
            return;
        foreach (var initializer in plan.Initializers)
            functions.Add(new($"static.initialize<{initializer.Key}>",
                WasmFunctionType.Create(CliValueKind.Void), initializers.Emit(initializer, plan)));
        functions.Add(new("static.initialization.failure",
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.ManagedAddress,
                CliValueKind.ManagedReference), failures.Emit(plan)));
    }
}
