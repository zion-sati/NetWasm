using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ArrayLengthAdapterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.NativeInt)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I4)]
    public void AdaptChecksSignedI32StorageAndRetainsItsLocal(
        WasmTarget target,
        CliValueKind lengthType)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        IImplicitExceptionEmitter exceptions = new ImplicitExceptionEmitter(
            layouts,
            layouts,
            7);
        var adapter = CreateAdapter(layouts, exceptions);
        var request = CreateInstructionRequest(
            CilOperation.NewArray,
            [lengthType]);

        var local = adapter.Adapt(request, GetCodeWriter(request), 0);
        var expected = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            request.Context.StackLocals,
            0,
            lengthType,
            layouts.Target);
        var code = GetCodeBytes(request);

        Assert.Equal(expected, local);
        Assert.Contains(WasmOpcodes.I32LessThanSigned, code);
        Assert.DoesNotContain(WasmOpcodes.I32WrapI64, code);
        Assert.Contains(WasmOpcodes.Throw, code);
    }

    [Fact]
    public void AdaptRangeChecksAndNarrowsWasm64NativeLength()
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        IImplicitExceptionEmitter exceptions = new ImplicitExceptionEmitter(
            layouts,
            layouts,
            7);
        var adapter = CreateAdapter(layouts, exceptions);
        var request = CreateInstructionRequest(
            CilOperation.NewArray,
            [CliValueKind.NativeInt]);

        var local = adapter.Adapt(request, GetCodeWriter(request), 0);
        var code = GetCodeBytes(request);

        Assert.Equal(request.Context.NumericTemporaryI4, local);
        Assert.Contains(WasmOpcodes.I64GreaterThanUnsigned, code);
        Assert.Contains(WasmOpcodes.I32WrapI64, code);
        Assert.Contains(WasmOpcodes.Throw, code);
    }

    [Fact]
    public void AdaptRejectsATypeOutsideItsValidatedContract()
    {
        var layouts = new RecordingLayoutProvider();
        IImplicitExceptionEmitter exceptions = new ImplicitExceptionEmitter(
            layouts,
            layouts,
            7);
        var adapter = CreateAdapter(layouts, exceptions);
        var request = CreateInstructionRequest(
            CilOperation.NewArray,
            [CliValueKind.I8]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            adapter.Adapt(request, GetCodeWriter(request), 0));

        Assert.Equal("array length must be int32 or native integer", exception.Message);
    }

    private static IArrayLengthAdapter CreateAdapter(
        ITargetLayout layouts,
        IImplicitExceptionEmitter exceptions) => new[]
        {
            new ArrayLengthAdapter(layouts, exceptions),
        }.Cast<IArrayLengthAdapter>().Single();
}
