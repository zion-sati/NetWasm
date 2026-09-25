using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class StaticInitializerFunctionEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void BoundaryClaimsGuardThenInterceptsBeforeCallingAndPublishesSuccessOnlyAfterReturn(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var writers = new StaticInitializerRecordingWriters();
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = Assert.IsAssignableFrom<IStaticInitializerFunctionEmitter>(new StaticInitializerFunctionEmitter(layouts, imports,
            new AddressInstructionEmitter(layouts), new ExceptionPayloadBlockEmitter(layouts), writers));
        var initializer = new StaticInitializerFunction("owner", 101, 71, 240);
        var plan = new StaticInitializerFunctionPlan([initializer], 480, 102);

        emitter.Emit(initializer, plan);

        var code = writers.Instructions.ToInstructions();
        var calls = code.Where(i => i.Opcode == WasmOpcodes.Call).Select(i => i.Operand.UnsignedValue).ToArray();
        Assert.Equal(new uint[]
        {
            (uint)imports.Resolve(RuntimeImportSymbol.ExceptionFrameEnter), 71,
            (uint)imports.Resolve(RuntimeImportSymbol.ExceptionFrameLeave),
            (uint)imports.Resolve(RuntimeImportSymbol.ExceptionFrameLeave),
            (uint)imports.Resolve(RuntimeImportSymbol.HandleGet), 102,
        }, calls);
        var stores = Enumerable.Range(0, code.Length).Where(i => code[i].Opcode == WasmOpcodes.I32Store).ToArray();
        Assert.Equal(2, stores.Length);
        Assert.Equal(1, code[stores[0] - 1].Operand.SignedValue);
        Assert.Equal(2, code[stores[1] - 1].Operand.SignedValue);
        Assert.True(stores[0] < Array.FindIndex(code.ToArray(), i => i.Opcode == WasmOpcodes.TryTable));
        Assert.True(stores[1] > Array.FindIndex(code.ToArray(), i => i.Opcode == WasmOpcodes.Call && i.Operand.UnsignedValue == 71));
        Assert.DoesNotContain((uint)imports.Resolve(RuntimeImportSymbol.HandleNew), calls);
        Assert.DoesNotContain((uint)imports.Resolve(RuntimeImportSymbol.Allocate), calls);
        Assert.Equal(target == WasmTarget.Wasm64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant, code[0].Opcode);
        Assert.Equal(240L, target == WasmTarget.Wasm64 ? code[0].Operand.Signed64Value : code[0].Operand.SignedValue);
        Assert.Equal(WasmOpcodes.Return, code[8].Opcode);
    }
}

internal sealed class StaticInitializerRecordingWriters : IGeneratedFunctionWriterFactory
{
    public EmitterTestSupport.RecordingInstructionWriter Instructions { get; } = new();

    public GeneratedFunctionWriterLease Create()
    {
        var output = new GeneratedFunctionWriterFactory().Create();
        return new(output.Bytes, output.Snapshots, Instructions);
    }
}
