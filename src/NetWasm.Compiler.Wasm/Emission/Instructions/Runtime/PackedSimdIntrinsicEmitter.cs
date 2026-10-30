using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;
using static NetWasm.Compiler.Wasm.WasmSimdOpcodes;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// Lowers System.Runtime.Intrinsics.Wasm.PackedSimd, whose every overload is one
// Wasm SIMD instruction. Vector operands and results are v128 values, as for the
// portable vectors. A lane index may be a variable, so lane reads move the lane's
// bytes to lane 0 with a swizzle and lane writes select it with a mask.
internal sealed class PackedSimdIntrinsicEmitter(
    IImplicitExceptionEmitter exceptions) : IRuntimeIntrinsicEmitter
{
    private static readonly byte[] I64SignBits =
        [0, 0, 0, 0, 0, 0, 0, 0x80, 0, 0, 0, 0, 0, 0, 0, 0x80];

    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (request.Intrinsic == RuntimeIntrinsic.PackedSimdIsSupported)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(1)));
            Set(code, request.Local(0, CliValueKind.I4));
            return;
        }

        if (!PackedSimdInstructions.TryGet(request.Method.Definition, out var instruction))
        {
            throw new InvalidOperationException(
                $"{request.Method.Definition.Name} has no PackedSimd lowering.");
        }

        var wide = request.Target.UsesMemory64 &&
            PackedSimdInstructions.IsNativeWidth(request.Method.Signature);
        var opcode = wide ? instruction.Wide.Opcode : instruction.Opcode;
        var laneBytes = wide ? instruction.Wide.LaneBytes : instruction.LaneBytes;
        var flipsSign = wide && instruction.Wide.FlipsSign;
        var floating = instruction.Name.StartsWith("f32x4", StringComparison.Ordinal) ||
            instruction.Name.StartsWith("f64x2", StringComparison.Ordinal);
        var signature = request.Method.Signature;

        switch (instruction.Shape)
        {
            case PackedSimdShape.Simple:
            case PackedSimdShape.Memory:
                for (var argument = 0; argument < signature.ParameterTypes.Length; argument++)
                {
                    Get(code, Argument(request, argument));
                    if (flipsSign && IsVector(signature.ParameterSignatureTypes[argument]))
                    {
                        code.Write(WasmInstruction.WithOperand(
                            WasmOpcodes.SimdPrefixed,
                            WasmInstructionOperand.FromBytes([(byte)V128Constant, .. I64SignBits])));
                        Simd(code, V128Xor);
                    }
                }
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.SimdPrefixed,
                    instruction.Shape == PackedSimdShape.Memory
                        ? WasmInstructionOperand.PrefixedTriple(opcode, 0, 0)
                        : WasmInstructionOperand.Prefixed(opcode)));
                break;
            case PackedSimdShape.ExtractLane:
                CheckLane(request, code, 1, laneBytes);
                SelectLane(request, code, 0, 1, laneBytes);
                ExtractFirst(code, laneBytes, floating, instruction.SignedLane);
                break;
            case PackedSimdShape.ReplaceLane:
                CheckLane(request, code, 1, laneBytes);
                Get(code, Argument(request, 2));
                Simd(code, Splat(laneBytes, floating));
                Get(code, Argument(request, 0));
                LaneMask(request, code, 1, laneBytes);
                Simd(code, V128BitSelect);
                break;
            case PackedSimdShape.LoadLane:
                CheckLane(request, code, 2, laneBytes);
                Get(code, Argument(request, 0));
                Load(code, laneBytes, floating: false, signed: false);
                Simd(code, Splat(laneBytes, floating: false));
                Get(code, Argument(request, 1));
                LaneMask(request, code, 2, laneBytes);
                Simd(code, V128BitSelect);
                break;
            case PackedSimdShape.StoreLane:
                CheckLane(request, code, 2, laneBytes);
                Get(code, Argument(request, 0));
                SelectLane(request, code, 1, 2, laneBytes);
                ExtractFirst(code, laneBytes, floating: false, signed: false);
                Store(code, laneBytes, floating: false);
                return;
        }

        if (signature.ReturnType != CliValueKind.Void)
        {
            Set(code, request.Local(0, signature.ReturnType));
        }
    }

    private static int Argument(RuntimeIntrinsicEmissionRequest request, int argument) =>
        request.Local(argument, request.Instruction.Stack[request.ArgumentBase + argument]);

    // As upstream for a lane index outside the vector.
    private void CheckLane(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, int argument, int laneBytes)
    {
        Get(code, Argument(request, argument));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(16 / laneBytes)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.ArgumentOutOfRange, request.Instruction.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    // Pushes the vector with the indexed lane's bytes moved to lane 0: byte lane
    // i reads byte index * laneBytes + i % laneBytes.
    private static void SelectLane(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int vectorArgument,
        int indexArgument,
        int laneBytes)
    {
        Get(code, Argument(request, vectorArgument));
        Get(code, Argument(request, indexArgument));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(laneBytes)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Multiply));
        Simd(code, I8x16Splat);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.FromBytes([(byte)V128Constant, .. Enumerable.Range(0, 16).Select(i => (byte)(i % laneBytes))])));
        Simd(code, I8x16Add);
        Simd(code, I8x16Swizzle);
    }

    // Pushes a mask with every bit of the indexed lane set and the rest clear.
    private static void LaneMask(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int indexArgument,
        int laneBytes)
    {
        var lanes = Enumerable.Range(0, 16)
            .Select(i => i % laneBytes == 0 ? (byte)(i / laneBytes) : (byte)0);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.FromBytes([(byte)V128Constant, .. lanes])));
        Get(code, Argument(request, indexArgument));
        if (laneBytes == 8)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        Simd(code, Splat(laneBytes, floating: false));
        Simd(code, laneBytes switch
        {
            1 => I8x16Equal,
            2 => I16x8Equal,
            4 => I32x4Equal,
            _ => I64x2Equal,
        });
    }

    private static uint Splat(int laneBytes, bool floating) => (laneBytes, floating) switch
    {
        (1, _) => I8x16Splat,
        (2, _) => I16x8Splat,
        (4, true) => F32x4Splat,
        (4, false) => I32x4Splat,
        (_, true) => F64x2Splat,
        _ => I64x2Splat,
    };

    private static void ExtractFirst(IWasmInstructionWriter code, int laneBytes, bool floating, bool signed) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.PrefixedPair((laneBytes, floating, signed) switch
            {
                (1, _, true) => I8x16ExtractLaneSigned,
                (1, _, false) => I8x16ExtractLaneUnsigned,
                (2, _, true) => I16x8ExtractLaneSigned,
                (2, _, false) => I16x8ExtractLaneUnsigned,
                (4, true, _) => F32x4ExtractLane,
                (4, false, _) => I32x4ExtractLane,
                (_, true, _) => F64x2ExtractLane,
                _ => I64x2ExtractLane,
            }, 0)));

    private static void Load(IWasmInstructionWriter code, int laneBytes, bool floating, bool signed)
    {
        var (opcode, alignment) = (laneBytes, floating, signed) switch
        {
            (1, _, true) => (WasmOpcodes.I32Load8Signed, 0u),
            (1, _, false) => (WasmOpcodes.I32Load8Unsigned, 0u),
            (2, _, true) => (WasmOpcodes.I32Load16Signed, 1u),
            (2, _, false) => (WasmOpcodes.I32Load16Unsigned, 1u),
            (4, true, _) => (WasmOpcodes.F32Load, 2u),
            (4, false, _) => (WasmOpcodes.I32Load, 2u),
            (_, true, _) => (WasmOpcodes.F64Load, 3u),
            _ => (WasmOpcodes.I64Load, 3u),
        };
        code.Write(WasmInstruction.WithOperand(opcode, WasmInstructionOperand.Memory(alignment, 0)));
    }

    private static void Store(IWasmInstructionWriter code, int laneBytes, bool floating)
    {
        var (opcode, alignment) = (laneBytes, floating) switch
        {
            (1, _) => (WasmOpcodes.I32Store8, 0u),
            (2, _) => (WasmOpcodes.I32Store16, 1u),
            (4, true) => (WasmOpcodes.F32Store, 2u),
            (4, false) => (WasmOpcodes.I32Store, 2u),
            (_, true) => (WasmOpcodes.F64Store, 3u),
            _ => (WasmOpcodes.I64Store, 3u),
        };
        code.Write(WasmInstruction.WithOperand(opcode, WasmInstructionOperand.Memory(alignment, 0)));
    }

    private static bool IsVector(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.GenericInstantiation &&
        type.ElementType?.FullName == "System.Runtime.Intrinsics.Vector128`1";

    private static void Simd(IWasmInstructionWriter code, uint opcode) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.Prefixed(opcode)));

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void Set(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));
}
