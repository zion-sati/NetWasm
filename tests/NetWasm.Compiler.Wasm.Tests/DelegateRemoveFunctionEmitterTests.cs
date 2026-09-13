using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class DelegateRemoveFunctionEmitterTests
{
    [Fact]
    public void EmitsLastSubsequenceRemovalWithManagedAllocation()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new DelegateRemoveFunctionEmitter(layouts, layouts, layouts, WasmRuntimeImports.CreateCatalog(),
            new ImplicitExceptionEmitter(layouts, layouts, 7),
            CreateReferenceComparisons(layouts),
            CreateDelegateLeafEquality(layouts),
            CreateAddressInstructions(layouts),
            new GeneratedFunctionWriterFactory());

        var bytes = ((IDelegateRemoveFunctionEmitter)emitter).Emit(
            CreateDelegateHelperTarget());

        Assert.NotEmpty(bytes);
        Assert.Contains(WasmOpcodes.Loop, bytes);
        Assert.Contains(WasmOpcodes.Call, bytes);
    }
}
