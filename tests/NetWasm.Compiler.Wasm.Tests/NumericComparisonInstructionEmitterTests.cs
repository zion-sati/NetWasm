using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class NumericComparisonInstructionEmitterTests
{
    [Theory]
    [InlineData(CilOperation.CompareEqual, WasmOpcodes.I32Equal)]
    [InlineData(CilOperation.CompareGreaterThanSigned, WasmOpcodes.I32GreaterThanSigned)]
    [InlineData(CilOperation.CompareLessThanSigned, WasmOpcodes.I32LessThanSigned)]
    public void IntegerComparisonProducesInt32(
        CilOperation operation,
        byte expectedOpcode)
    {
        var request = CreateRequest(operation, CliValueKind.I4);

        new NumericComparisonInstructionEmitter(
            new RecordingLayoutProvider(), new StackTypeCompatibilityValidator()).Emit(request);

        Assert.Equal([CliValueKind.I4], request.Stack);
        Assert.Contains(expectedOpcode, GetCodeBytes(request));
    }

    [Fact]
    public void Memory64ReferenceEqualityUsesI64()
    {
        var request = CreateRequest(
            CilOperation.CompareEqual,
            CliValueKind.ManagedReference);

        new NumericComparisonInstructionEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.Wasm64), new StackTypeCompatibilityValidator()).Emit(request);

        Assert.Contains(WasmOpcodes.I64Equal, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EmitsEveryNumericComparisonForEveryScalarKind(WasmTarget target)
    {
        var operations = new[]
        {
            CilOperation.CompareEqual,
            CilOperation.CompareGreaterThanSigned,
            CilOperation.CompareGreaterThanUnsigned,
            CilOperation.CompareLessThanSigned,
            CilOperation.CompareLessThanUnsigned,
        };
        var types = new[]
        {
            CliValueKind.I4,
            CliValueKind.I8,
            CliValueKind.NativeInt,
            CliValueKind.F4,
            CliValueKind.F8,
        };
        var emitter = new NumericComparisonInstructionEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)),
            new StackTypeCompatibilityValidator());

        foreach (var operation in operations)
            foreach (var type in types)
            {
                var request = CreateRequest(operation, type);

                emitter.Emit(request);

                Assert.Equal([CliValueKind.I4], request.Stack);
                Assert.NotEmpty(GetCodeBytes(request));
            }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.ManagedReference)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.ManagedAddress)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.ManagedReference)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.ManagedAddress)]
    public void EmitsEveryManagedReferenceComparison(
        WasmTarget target,
        CliValueKind type)
    {
        var emitter = new NumericComparisonInstructionEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)),
            new StackTypeCompatibilityValidator());

        foreach (var operation in new[]
                 {
                     CilOperation.CompareEqual,
                     CilOperation.CompareGreaterThanUnsigned,
                     CilOperation.CompareLessThanUnsigned,
                 })
        {
            var request = CreateRequest(operation, type);

            emitter.Emit(request);

            Assert.Equal([CliValueKind.I4], request.Stack);
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ComparesEveryManagedAddressNativeIntegerOrderForEachTarget(WasmTarget target)
    {
        var emitter = new NumericComparisonInstructionEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)),
            new StackTypeCompatibilityValidator());

        foreach (var operation in new[]
                 {
                     CilOperation.CompareEqual,
                     CilOperation.CompareGreaterThanUnsigned,
                     CilOperation.CompareLessThanUnsigned,
                 })
        {
            foreach (var (left, right) in new[]
                     {
                         (CliValueKind.ManagedAddress, CliValueKind.NativeInt),
                         (CliValueKind.NativeInt, CliValueKind.ManagedAddress),
                     })
            {
                var request = CreateInstructionRequest(operation, [left, right]);

                emitter.Emit(request);

                Assert.Equal([CliValueKind.I4], request.Stack);
            }
        }
    }

    [Fact]
    public void RejectsMixedManagedOperands()
    {
        var request = CreateInstructionRequest(
            CilOperation.CompareEqual,
            [CliValueKind.ManagedReference, CliValueKind.ManagedAddress]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new NumericComparisonInstructionEmitter(
                new RecordingLayoutProvider(), new StackTypeCompatibilityValidator()).Emit(request));

        Assert.Contains("cannot mix", exception.Message);
    }

    [Theory]
    [InlineData(CilOperation.CompareEqual)]
    [InlineData(CilOperation.CompareGreaterThanUnsigned)]
    [InlineData(CilOperation.CompareLessThanUnsigned)]
    public void RejectsUnsupportedComparisonStackKind(CilOperation operation)
    {
        var request = CreateRequest(operation, CliValueKind.ValueType);

        Assert.Throws<InvalidOperationException>(() =>
            new NumericComparisonInstructionEmitter(
                new RecordingLayoutProvider(), new StackTypeCompatibilityValidator()).Emit(request));
    }

    private static InstructionEmissionRequest CreateRequest(
        CilOperation operation,
        CliValueKind type)
    {
        return CreateInstructionRequest(operation, [type, type]);
    }
}
