using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using static NetWasm.Compiler.Wasm.WasmSimdOpcodes;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// Vector128<float>'s views of System.Numerics' float vectors, upstream's
// [Intrinsic] AsVector128, AsVector128Unsafe, AsVector2, AsVector3, AsVector4,
// AsQuaternion and AsPlane. The float vectors are v128 values already, so a
// view of one is the same value, except that AsVector128 zeroes the lanes above
// a Vector2's two and a Vector3's three, which are undefined in the vector.
internal sealed class NumericsVectorBridgeIntrinsicEmitter : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var signature = request.Method.Signature;
        var vector = request.Local(0, CliValueKind.V128);
        if (request.Intrinsic == RuntimeIntrinsic.NumericsToVector128)
        {
            var source = signature.ParameterSignatureTypes[0];
            Require(source);
            if (request.Method.Definition.Name != "AsVector128")
            {
                return;
            }
            switch (source.FullName)
            {
                case "System.Numerics.Vector2":
                    Get(code, vector);
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(0)));
                    Simd(code, I64x2ReplaceLane, 1);
                    Set(code, vector);
                    return;
                case "System.Numerics.Vector3":
                    Get(code, vector);
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.F32Constant, WasmInstructionOperand.Float32(0)));
                    Simd(code, F32x4ReplaceLane, 3);
                    Set(code, vector);
                    return;
                default:
                    return;
            }
        }
        Require(signature.ReturnSignatureType);
    }

    // Every System.Numerics float vector moves as a v128 value.
    private static void Require(CliTypeIdentity vector)
    {
        if (vector.StackKind != CliValueKind.V128)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.CompilerInvariant,
                $"a numerics vector bridge's vector {vector.FullName} is not a v128 value"));
        }
    }

    private static void Simd(IWasmInstructionWriter code, uint opcode, uint lane) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.PrefixedPair(opcode, lane)));

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));

    private static void Set(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
}
