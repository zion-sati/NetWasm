using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

// The classifier gives the emitter only the pairs and lane widths upstream's
// vectors declare; anything else is a compiler fault, which stops emission
// rather than emitting the wrong instruction.
public sealed class SimdVectorIntrinsicEmitterTests
{
    public static TheoryData<RuntimeIntrinsic, string, string> Unsupported() => new()
    {
        { RuntimeIntrinsic.SimdConvert, "i1", "i1" },
        { RuntimeIntrinsic.SimdWidenLower, "i8", "i8" },
        { RuntimeIntrinsic.SimdNarrow, "i1", "i1" },
        { RuntimeIntrinsic.ArrayLength, "i4", "i4" },
    };

    [Theory]
    [MemberData(nameof(Unsupported))]
    public void PairsAndWidthsWithoutALoweringStopEmission(RuntimeIntrinsic intrinsic, string from, string to)
    {
        var source = Vector(from);
        var result = Vector(to);
        var binary = intrinsic is RuntimeIntrinsic.SimdNarrow or RuntimeIntrinsic.ArrayLength;
        CliTypeIdentity[] parameters = binary ? [source, source] : [source];
        var signature = MethodSignatureModel.Create(result, parameters);
        var instruction = CreateInstructionRequest(CilOperation.Call,
            [.. parameters.Select(static _ => CliValueKind.V128)], maxStack: parameters.Length);
        var definition = new MethodDefinitionModel(Key(0x06000200), TypeKey, "Lowered", true, signature, 1);
        var method = new MethodInstanceModel(definition,
            CliTypeIdentity.Named(Assembly, "System.Runtime.Intrinsics", "Vector128", false), [], signature);
        var request = new RuntimeIntrinsicEmissionRequest(
            new CallEmissionRequest(instruction, method, 0, parameters.Length),
            intrinsic, null, WasmTargetLayout.Wasm32, CreateFunctionIndexResolver());
        // Binary's other operations reach the arithmetic family's last case.
        IRuntimeIntrinsicEmitter emitter = intrinsic == RuntimeIntrinsic.ArrayLength
            ? new SimdVectorArithmeticIntrinsicEmitter(new SimdVectorLaneGuard(new NoExceptions()), new NoExceptions())
            : new SimdVectorConversionIntrinsicEmitter(new SimdVectorLaneGuard(new NoExceptions()));

        var exception = Assert.Throws<InvalidOperationException>(() => emitter.Emit(request, GetCodeWriter(instruction)));
        Assert.Contains("has no lowering", exception.Message, StringComparison.Ordinal);
    }

    // Each family resolves its lanes first: an element type the vectors do not
    // support throws NotSupportedException, as upstream's software implementation
    // does, and the family emits nothing else.
    [Theory]
    [InlineData(RuntimeIntrinsic.SimdAdd)]
    [InlineData(RuntimeIntrinsic.SimdBitwiseAnd)]
    [InlineData(RuntimeIntrinsic.SimdEquals)]
    [InlineData(RuntimeIntrinsic.SimdSum)]
    [InlineData(RuntimeIntrinsic.SimdGetElement)]
    [InlineData(RuntimeIntrinsic.SimdCreateBroadcast)]
    [InlineData(RuntimeIntrinsic.SimdLoad)]
    [InlineData(RuntimeIntrinsic.SimdShuffle)]
    [InlineData(RuntimeIntrinsic.SimdConvert)]
    public void AnUnsupportedElementTypeThrowsNotSupportedInEveryFamily(RuntimeIntrinsic intrinsic)
    {
        var vector = Vector("bool");
        var signature = MethodSignatureModel.Create(vector, vector, vector, vector);
        var instruction = CreateInstructionRequest(CilOperation.Call,
            [CliValueKind.V128, CliValueKind.V128, CliValueKind.V128], maxStack: 3);
        var definition = new MethodDefinitionModel(Key(0x06000202), TypeKey, "Lowered", true, signature, 1);
        var method = new MethodInstanceModel(definition,
            CliTypeIdentity.Named(Assembly, "System.Runtime.Intrinsics", "Vector128", false), [], signature);
        var request = new RuntimeIntrinsicEmissionRequest(
            new CallEmissionRequest(instruction, method, 0, 3),
            intrinsic, null, WasmTargetLayout.Wasm32, CreateFunctionIndexResolver());
        var exceptions = new RecordingExceptions();
        var lanes = new SimdVectorLaneGuard(exceptions);
        IRuntimeIntrinsicEmitter emitter = intrinsic switch
        {
            RuntimeIntrinsic.SimdAdd => new SimdVectorArithmeticIntrinsicEmitter(lanes, exceptions),
            RuntimeIntrinsic.SimdBitwiseAnd => new SimdVectorBitwiseIntrinsicEmitter(lanes),
            RuntimeIntrinsic.SimdEquals => new SimdVectorComparisonIntrinsicEmitter(lanes),
            RuntimeIntrinsic.SimdSum => new SimdVectorReductionIntrinsicEmitter(lanes),
            RuntimeIntrinsic.SimdGetElement => new SimdVectorElementIntrinsicEmitter(lanes, exceptions),
            RuntimeIntrinsic.SimdCreateBroadcast => new SimdVectorCreationIntrinsicEmitter(lanes),
            RuntimeIntrinsic.SimdLoad => new SimdVectorMemoryIntrinsicEmitter(lanes),
            RuntimeIntrinsic.SimdShuffle => new SimdVectorPermutationIntrinsicEmitter(lanes),
            _ => new SimdVectorConversionIntrinsicEmitter(lanes),
        };

        emitter.Emit(request, GetCodeWriter(instruction));

        Assert.Equal([ManagedExceptionKind.NotSupported], exceptions.Kinds);
    }

    // PackedSimd lowers only the overloads in its table.
    [Fact]
    public void APackedSimdMethodWithoutAnInstructionStopsEmission()
    {
        var vector = Vector("i4");
        var signature = MethodSignatureModel.Create(vector, vector);
        var instruction = CreateInstructionRequest(CilOperation.Call, [CliValueKind.V128], maxStack: 1);
        var definition = new MethodDefinitionModel(Key(0x06000201), TypeKey, "Unlisted", true, signature, 1);
        var method = new MethodInstanceModel(definition,
            CliTypeIdentity.Named(Assembly, "System.Runtime.Intrinsics.Wasm", "PackedSimd", false), [], signature);
        var request = new RuntimeIntrinsicEmissionRequest(
            new CallEmissionRequest(instruction, method, 0, 1),
            RuntimeIntrinsic.PackedSimdInstruction, null, WasmTargetLayout.Wasm32, CreateFunctionIndexResolver());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new PackedSimdIntrinsicEmitter(new NoExceptions()).Emit(request, GetCodeWriter(instruction)));
        Assert.Contains("Unlisted has no PackedSimd lowering", exception.Message, StringComparison.Ordinal);
    }

    private static CliTypeIdentity Vector(string element) => CliTypeIdentity.GenericInstantiation(
        CliTypeIdentity.Named(Assembly, "System.Runtime.Intrinsics", "Vector128`1", true),
        [CliTypeIdentity.Primitive(element, element == "i8" ? CliValueKind.I8 : CliValueKind.I4)]);

    private sealed class RecordingExceptions : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind, OptionalFunctionIndex throwFunctionIndex) =>
            Kinds.Add(kind);
    }

    private sealed class NoExceptions : IImplicitExceptionEmitter
    {
        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind, OptionalFunctionIndex throwFunctionIndex) =>
            throw new InvalidOperationException("The emitter raised a managed exception.");
    }
}
