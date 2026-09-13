using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ArrayCopyClearIntrinsicEmitterTests
{
    [Fact]
    public void CloneLoadsTheArrayCallsRuntimeAndPublishesTheResult()
    {
        var emitter = ThroughContract(new ArrayCloneIntrinsicEmitter(
            WasmRuntimeImports.CreateCatalog()));
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.ArrayClone,
            [CliValueKind.ManagedReference]);

        emitter.Emit(request, GetCodeWriter(request.Instruction));

        var code = GetCodeBytes(request.Instruction);
        Assert.Contains(WasmOpcodes.LocalGet, code);
        Assert.Contains(WasmOpcodes.Call, code);
        Assert.Contains(WasmOpcodes.LocalSet, code);
    }

    [Fact]
    public void CopyLoadsEveryArgumentCallsRuntimeAndPublishesTheResult()
    {
        var emitter = ThroughContract(new ArrayCopyIntrinsicEmitter(
            WasmRuntimeImports.CreateCatalog()));
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.ArrayCopy,
            [
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.I4,
            ]);

        emitter.Emit(request, GetCodeWriter(request.Instruction));

        var code = GetCodeBytes(request.Instruction);
        Assert.Equal(5, code.Count(value => value == WasmOpcodes.LocalGet));
        Assert.Contains(WasmOpcodes.Call, code);
        Assert.Contains(WasmOpcodes.LocalSet, code);
    }

    [Fact]
    public void ClearLoadsEveryArgumentAndCallsRuntime()
    {
        var emitter = ThroughContract(new ArrayClearIntrinsicEmitter(
            WasmRuntimeImports.CreateCatalog()));
        var request = CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.ArrayClear,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4]);

        emitter.Emit(request, GetCodeWriter(request.Instruction));

        var code = GetCodeBytes(request.Instruction);
        Assert.Equal(3, code.Count(value => value == WasmOpcodes.LocalGet));
        Assert.Contains(WasmOpcodes.Call, code);
        Assert.DoesNotContain(WasmOpcodes.LocalSet, code);
    }

    private static IRuntimeIntrinsicEmitter ThroughContract(
        IRuntimeIntrinsicEmitter emitter) => emitter;
}
