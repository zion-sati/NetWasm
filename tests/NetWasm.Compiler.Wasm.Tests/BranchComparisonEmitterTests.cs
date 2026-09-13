using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class BranchComparisonEmitterTests
{
    private static readonly CilOperation[] ScalarOperations =
    [
        CilOperation.BranchIfEqual,
        CilOperation.BranchIfNotEqual,
        CilOperation.BranchIfGreaterThanSigned,
        CilOperation.BranchIfGreaterThanUnsigned,
        CilOperation.BranchIfGreaterThanOrEqualSigned,
        CilOperation.BranchIfGreaterThanOrEqualUnsigned,
        CilOperation.BranchIfLessThanSigned,
        CilOperation.BranchIfLessThanUnsigned,
        CilOperation.BranchIfLessThanOrEqualSigned,
        CilOperation.BranchIfLessThanOrEqualUnsigned,
    ];

    private static readonly CilOperation[] ReferenceOperations =
    [
        CilOperation.BranchIfEqual,
        CilOperation.BranchIfNotEqual,
        CilOperation.BranchIfGreaterThanUnsigned,
        CilOperation.BranchIfGreaterThanOrEqualUnsigned,
        CilOperation.BranchIfLessThanUnsigned,
        CilOperation.BranchIfLessThanOrEqualUnsigned,
    ];

    [Theory]
    [InlineData(CilOperation.BranchIfEqual)]
    [InlineData(CilOperation.BranchIfGreaterThanSigned)]
    [InlineData(CilOperation.BranchIfLessThanOrEqualUnsigned)]
    public void EmitsScalarComparisons(CilOperation operation)
    {
        var outputBuffer = new WasmBinaryBuffer();
        var output = new WasmBinaryWriter(outputBuffer);
        var code = new WasmInstructionWriter(output);

        IBranchComparisonEmitter emitter =
            new[] { new BranchComparisonEmitter(new RecordingLayoutProvider(), new StackTypeCompatibilityValidator()) }
                .Cast<IBranchComparisonEmitter>()
                .Single();
        emitter.Compare(
            code,
            operation,
            CliValueKind.I4,
            CliValueKind.I4);

        Assert.NotEmpty(new WasmBinarySnapshotReader(outputBuffer).Read());
    }

    [Fact]
    public void RejectsMixedManagedOperands()
    {
        IBranchComparisonEmitter emitter =
            new[] { new BranchComparisonEmitter(new RecordingLayoutProvider(), new StackTypeCompatibilityValidator()) }
                .Cast<IBranchComparisonEmitter>()
                .Single();

        var exception = Assert.Throws<InvalidOperationException>(() => emitter.Compare(
            new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
            CilOperation.BranchIfEqual,
            CliValueKind.ManagedReference,
            CliValueKind.ManagedAddress));

        Assert.Contains("cannot mix", exception.Message);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.NativeInt)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I8)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.F4)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.F8)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.NativeInt)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I8)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.F4)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.F8)]
    public void EmitsEveryScalarComparisonForEachTarget(
        WasmTarget target,
        CliValueKind valueKind)
    {
        foreach (var operation in ScalarOperations)
        {
            Assert.NotEmpty(Emit(target, operation, valueKind, valueKind));
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.ManagedReference)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.ManagedAddress)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.ManagedReference)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.ManagedAddress)]
    public void EmitsEverySupportedManagedComparisonForEachTarget(
        WasmTarget target,
        CliValueKind valueKind)
    {
        foreach (var operation in ReferenceOperations)
        {
            Assert.NotEmpty(Emit(target, operation, valueKind, valueKind));
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ComparesEveryManagedAddressNativeIntegerOrderForEachTarget(WasmTarget target)
    {
        foreach (var operation in ReferenceOperations)
        {
            foreach (var (left, right) in new[]
                     {
                         (CliValueKind.ManagedAddress, CliValueKind.NativeInt),
                         (CliValueKind.NativeInt, CliValueKind.ManagedAddress),
                     })
            {
                Assert.NotEmpty(Emit(target, operation, left, right));
            }
        }
    }

    [Fact]
    public void RejectsIncompatibleScalarOperands()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Emit(
            WasmTarget.Wasm32,
            CilOperation.BranchIfEqual,
            CliValueKind.I4,
            CliValueKind.I8));

        Assert.Contains("incompatible scalar operands", exception.Message);
    }

    [Theory]
    [InlineData(CliValueKind.I4, typeof(ArgumentOutOfRangeException))]
    [InlineData(CliValueKind.I8, typeof(InvalidOperationException))]
    [InlineData(CliValueKind.F4, typeof(InvalidOperationException))]
    [InlineData(CliValueKind.F8, typeof(InvalidOperationException))]
    [InlineData(CliValueKind.ManagedReference, typeof(ArgumentOutOfRangeException))]
    public void RejectsOperationsOutsideTheComparisonContract(
        CliValueKind valueKind,
        Type exceptionType)
    {
        var exception = Record.Exception(() => Emit(
            WasmTarget.Wasm32,
            CilOperation.BranchIfTrue,
            valueKind,
            valueKind));

        Assert.IsType(exceptionType, exception);
    }

    private static byte[] Emit(
        WasmTarget target,
        CilOperation operation,
        CliValueKind leftType,
        CliValueKind rightType)
    {
        var buffer = new WasmBinaryBuffer();
        IBranchComparisonEmitter emitter = new[]
        {
            new BranchComparisonEmitter(
                new RecordingLayoutProvider(WasmTargetLayout.For(target)),
                new StackTypeCompatibilityValidator()),
        }.Cast<IBranchComparisonEmitter>().Single();

        emitter.Compare(
            new WasmInstructionWriter(new WasmBinaryWriter(buffer)),
            operation,
            leftType,
            rightType);

        return new WasmBinarySnapshotReader(buffer).Read();
    }
}
