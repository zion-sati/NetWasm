using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Delegates;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class DelegateInstructionEmitterTests
{
    [Fact]
    public void CombineAllocatesCompositeDelegateAndConsumesRightOperand()
    {
        var request = CreateRequest(CilOperation.DelegateCombine);

        CreateEmitter().Emit(request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
    }

    [Fact]
    public void RemoveCallsPlannedHelperAndConsumesRightOperand()
    {
        var request = CreateRequest(CilOperation.DelegateRemove);

        CreateEmitter().Emit(request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
    }

    [Fact]
    public void Memory64CombineUsesWideAddressInstructions()
    {
        var request = CreateRequest(CilOperation.DelegateCombine);

        CreateEmitter(WasmTargetLayout.Wasm64).Emit(request);

        var bytes = GetCodeBytes(request);
        Assert.Contains(WasmOpcodes.I64Constant, bytes);
        Assert.Contains(WasmOpcodes.I64EqualZero, bytes);
    }

    [Theory]
    [InlineData(CilOperation.DelegateEqual, false)]
    [InlineData(CilOperation.DelegateNotEqual, true)]
    public void EqualityProducesBooleanAndNegatesOnlyInequality(
        CilOperation operation,
        bool expectsNegation)
    {
        var request = CreateRequest(operation);

        CreateEmitter().Emit(request);

        Assert.Equal([CliValueKind.I4], request.Stack);
        Assert.Equal(
            expectsNegation,
            GetCodeBytes(request).Contains(WasmOpcodes.I32EqualZero));
    }

    private static DelegateInstructionEmitter CreateEmitter(
        WasmTargetLayout? target = null)
    {
        var layouts = new RecordingLayoutProvider(target);
        return new DelegateInstructionEmitter(layouts, layouts, layouts, WasmRuntimeImports.CreateCatalog(),
            new ImplicitExceptionEmitter(layouts, layouts, 7));
    }

    private static InstructionEmissionRequest CreateRequest(CilOperation operation) =>
        CreateInstructionRequest(
            operation,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference]);
}
