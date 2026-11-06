using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

// System.Numerics' float vectors are v128 values, so Vector128's views of them
// are the value itself, except that AsVector128 zeroes the lanes above a
// Vector2's two and a Vector3's three. A bridge whose vector is not a v128
// value is a compiler fault and stops emission before writing anything.
public sealed class NumericsVectorBridgeIntrinsicEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AsVector128ZeroesVector3sFourthLane(WasmTarget target)
    {
        var (request, code) = Request(target, RuntimeIntrinsic.NumericsToVector128, "AsVector128", Numerics("Vector3"), Float128());
        var vector = request.Local(0, CliValueKind.V128);

        ThroughContract(new NumericsVectorBridgeIntrinsicEmitter()).Emit(request, code);

        Assert.Equal(
            [
                WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)vector)),
                WasmInstruction.WithOperand(WasmOpcodes.F32Constant, WasmInstructionOperand.Float32(0)),
                WasmInstruction.WithOperand(WasmOpcodes.SimdPrefixed, WasmInstructionOperand.PrefixedPair(WasmSimdOpcodes.F32x4ReplaceLane, 3)),
                WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)vector)),
            ],
            code.ToInstructions());
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AsVector128ZeroesVector2sUpperHalf(WasmTarget target)
    {
        var (request, code) = Request(target, RuntimeIntrinsic.NumericsToVector128, "AsVector128", Numerics("Vector2"), Float128());
        var vector = request.Local(0, CliValueKind.V128);

        ThroughContract(new NumericsVectorBridgeIntrinsicEmitter()).Emit(request, code);

        Assert.Equal(
            [
                WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)vector)),
                WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(0)),
                WasmInstruction.WithOperand(WasmOpcodes.SimdPrefixed, WasmInstructionOperand.PrefixedPair(WasmSimdOpcodes.I64x2ReplaceLane, 1)),
                WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)vector)),
            ],
            code.ToInstructions());
    }

    // The value is already the view: AsVector128Unsafe leaves the undefined
    // lanes, a 16-byte vector has none, and the views back take the value.
    [Theory]
    [InlineData(RuntimeIntrinsic.NumericsToVector128, "AsVector128Unsafe", "Vector3", true)]
    [InlineData(RuntimeIntrinsic.NumericsToVector128, "AsVector128Unsafe", "Vector2", true)]
    [InlineData(RuntimeIntrinsic.NumericsToVector128, "AsVector128", "Vector4", true)]
    [InlineData(RuntimeIntrinsic.NumericsToVector128, "AsVector128", "Plane", true)]
    [InlineData(RuntimeIntrinsic.NumericsFromVector128, "AsVector3", "Vector3", false)]
    [InlineData(RuntimeIntrinsic.NumericsFromVector128, "AsQuaternion", "Quaternion", false)]
    public void OtherViewsAreTheValueItself(RuntimeIntrinsic intrinsic, string name, string numerics, bool toVector128)
    {
        var (request, code) = toVector128
            ? Request(WasmTarget.Wasm32, intrinsic, name, Numerics(numerics), Float128())
            : Request(WasmTarget.Wasm32, intrinsic, name, Float128(), Numerics(numerics));

        ThroughContract(new NumericsVectorBridgeIntrinsicEmitter()).Emit(request, code);

        Assert.Empty(code.ToInstructions());
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.NumericsToVector128, true)]
    [InlineData(RuntimeIntrinsic.NumericsFromVector128, false)]
    public void ABridgeWhoseVectorIsNotAV128ValueStopsEmission(RuntimeIntrinsic intrinsic, bool toVector128)
    {
        var aggregate = CliTypeIdentity.Named(Assembly, "Test", "Triple", isValueType: true);
        var (request, code) = toVector128
            ? Request(WasmTarget.Wasm32, intrinsic, "AsVector128", aggregate, Float128())
            : Request(WasmTarget.Wasm32, intrinsic, "AsVector3", Float128(), aggregate);

        var error = Assert.Throws<CompilerException>(() =>
            ThroughContract(new NumericsVectorBridgeIntrinsicEmitter()).Emit(request, code));

        Assert.Equal(DiagnosticCode.CompilerInvariant, error.Diagnostic.Code);
        Assert.Contains("Test.Triple", error.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Empty(code.ToInstructions());
    }

    private static (RuntimeIntrinsicEmissionRequest Request, RecordingInstructionWriter Code) Request(
        WasmTarget target,
        RuntimeIntrinsic intrinsic,
        string name,
        CliTypeIdentity parameter,
        CliTypeIdentity result)
    {
        var signature = MethodSignatureModel.Create(result, parameter);
        var instruction = CreateInstructionRequest(CilOperation.Call, [CliValueKind.V128], maxStack: 1);
        var definition = new MethodDefinitionModel(Key(0x06000210), TypeKey, name, true, signature, 1);
        var method = new MethodInstanceModel(definition,
            CliTypeIdentity.Named(Assembly, "System.Runtime.Intrinsics", "Vector128", false), [], signature);
        var request = new RuntimeIntrinsicEmissionRequest(
            new CallEmissionRequest(instruction, method, 0, 1),
            intrinsic, null, WasmTargetLayout.For(target), CreateFunctionIndexResolver());
        return (request, new RecordingInstructionWriter());
    }

    private static CliTypeIdentity Numerics(string name) =>
        CliTypeIdentity.Named(Assembly, "System.Numerics", name, isValueType: true);

    private static CliTypeIdentity Float128() => CliTypeIdentity.GenericInstantiation(
        CliTypeIdentity.Named(Assembly, "System.Runtime.Intrinsics", "Vector128`1", true),
        [CliTypeIdentity.Primitive("f4", CliValueKind.F4)]);

    private static IRuntimeIntrinsicEmitter ThroughContract(IRuntimeIntrinsicEmitter emitter) => emitter;
}
