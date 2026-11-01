using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;
using static NetWasm.Compiler.Wasm.WasmSimdOpcodes;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// Vector128<float>'s views of System.Numerics' float vectors, upstream's
// [Intrinsic] AsVector128, AsVector128Unsafe, AsVector2, AsVector3, AsVector4,
// AsQuaternion and AsPlane. Vector2, Vector4, Quaternion and Plane are v128
// values already, so a view of one is the same value, except that AsVector128
// zeroes a Vector2's undefined upper lanes. A Vector3 is 12 bytes in memory:
// they load into the three low lanes, the fourth zero, and store back from them.
internal sealed class NumericsVectorBridgeIntrinsicEmitter(
    ITargetLayout layouts,
    IValueFrameAddressEmitter addresses) : IRuntimeIntrinsicEmitter
{
    private static readonly CliTypeIdentity Single = CliTypeIdentity.Primitive("f4", CliValueKind.F4);

    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var signature = request.Method.Signature;
        var vector = request.Local(0, CliValueKind.V128);
        if (request.Intrinsic == RuntimeIntrinsic.NumericsToVector128)
        {
            var source = signature.ParameterSignatureTypes[0];
            if (source.StackKind == CliValueKind.V128)
            {
                if (request.Method.Definition.Name == "AsVector128" && source.FullName == "System.Numerics.Vector2")
                {
                    Get(code, vector);
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(0)));
                    Simd(code, I64x2ReplaceLane, 1);
                    Set(code, vector);
                }
                return;
            }
            var address = request.Local(0, CliValueKind.ValueType);
            Get(code, address);
            ManagedMemoryEmitter.EmitVectorLoad(code, 0, sizeof(long));
            Get(code, address);
            ManagedMemoryEmitter.EmitLoadByType(code, layouts.Target, sizeof(long), Single, sizeof(float));
            Simd(code, F32x4ReplaceLane, 2);
            Set(code, vector);
            return;
        }

        if (signature.ReturnSignatureType.StackKind == CliValueKind.V128)
        {
            return;
        }
        var result = request.Local(0, CliValueKind.ValueType);
        addresses.Emit(
            code,
            request.Instruction.Context,
            request.Instruction.Context.ValueLayout.TemporaryOffsets[request.Instruction.Instruction.Offset]);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)result)));
        Get(code, vector);
        ManagedMemoryEmitter.EmitVectorStore(code, 0, sizeof(long));
        Get(code, result);
        Get(code, vector);
        Simd(code, F32x4ExtractLane, 2);
        ManagedMemoryEmitter.EmitStoreByType(code, layouts.Target, sizeof(long), Single, sizeof(float));
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
