using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;
using static NetWasm.Compiler.Wasm.WasmSimdOpcodes;
using static NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors.SimdByteShuffles;
using static NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors.SimdLaneOpcodes;
using static NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors.SimdVectorCode;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors;

// GetElement and WithElement.
internal sealed class SimdVectorElementIntrinsicEmitter(
    ISimdVectorLaneGuard lanes,
    IImplicitExceptionEmitter exceptions) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (!lanes.TryResolve(request, code, out var resolved))
        {
            return;
        }
        var lane = resolved.Lane;
        if (request.Intrinsic == RuntimeIntrinsic.SimdGetElement)
        {
            EmitGetElement(request, code, resolved.Element, lane);
            return;
        }
        EmitWithElement(request, code, lane);
    }

    // As upstream: an index outside the lanes throws; the element is read from
    // the vector's storage.
    private void EmitGetElement(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity element,
        SimdLane lane)
    {
        var size = LaneSize(lane);
        var index = request.Local(1, CliValueKind.I4);
        ThrowOutsideLanes(request, code, index, lane);

        var receiver = request.Instruction.Stack[request.ArgumentBase];
        if (receiver == CliValueKind.V128 && Lanes(lane) <= 4)
        {
            // Each lane's extract_lane, kept where the index selects it. Upstream's
            // Vector2, Vector3, Vector4 and matrix code passes a constant index, so
            // the optimizer folds the selects and keeps that lane's extract alone.
            var result = request.Local(0, request.Method.Signature.ReturnType);
            Get(code, request.Local(0, CliValueKind.V128));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.SimdPrefixed,
                WasmInstructionOperand.PrefixedPair(ExtractLane(lane), 0)));
            Set(code, result);
            for (var other = 1; other < Lanes(lane); other++)
            {
                Get(code, request.Local(0, CliValueKind.V128));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.SimdPrefixed,
                    WasmInstructionOperand.PrefixedPair(ExtractLane(lane), (uint)other)));
                Get(code, result);
                Get(code, index);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(other)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Select));
                Set(code, result);
            }
            return;
        }
        if (receiver == CliValueKind.V128)
        {
            // The element's bytes move to lane 0: each byte lane i reads byte
            // index * size + i % size.
            Get(code, request.Local(0, CliValueKind.V128));
            Get(code, index);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(size)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Multiply));
            Simd(code, I8x16Splat);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.SimdPrefixed,
                WasmInstructionOperand.FromBytes([(byte)V128Constant, .. Enumerable.Range(0, 16).Select(i => (byte)(i % size))])));
            Simd(code, I8x16Add);
            Simd(code, I8x16Swizzle);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.SimdPrefixed,
                WasmInstructionOperand.PrefixedPair(ExtractLane(lane), 0)));
            Set(code, request.Local(0, request.Method.Signature.ReturnType));
            return;
        }

        var wide = request.Target.UsesMemory64;
        Get(code, request.Local(0, receiver));
        Get(code, index);
        if (wide)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        code.Write(wide
            ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(size))
            : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(size)));
        code.Write(WasmInstruction.NoOperand(wide ? WasmOpcodes.I64Multiply : WasmOpcodes.I32Multiply));
        code.Write(WasmInstruction.NoOperand(wide ? WasmOpcodes.I64Add : WasmOpcodes.I32Add));
        ManagedMemoryEmitter.EmitLoadByType(code, request.Target, 0, element, size);
        Set(code, request.Local(0, request.Method.Signature.ReturnType));
    }

    // As upstream: an index outside the lanes throws. The value replaces the lane
    // the index selects through a mask of that lane, so the index need not be a
    // constant.
    private void EmitWithElement(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        SimdLane lane)
    {
        var index = request.Local(1, CliValueKind.I4);
        ThrowOutsideLanes(request, code, index, lane);
        Get(code, request.Local(2, request.Method.Signature.ParameterTypes[2]));
        Simd(code, Splat(lane));
        Load(request, code, 0);
        var bits = new SimdLane(lane.Bits, Unsigned: false);
        Get(code, index);
        if (bits.Shape == SimdLaneShape.I64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        Simd(code, Splat(bits));
        var size = LaneSize(lane);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.FromBytes([(byte)V128Constant, .. Enumerable.Range(0, 16).Select(i => i % size == 0 ? (byte)(i / size) : (byte)0)])));
        Simd(code, bits.Shape switch
        {
            SimdLaneShape.I8 => I8x16Equal,
            SimdLaneShape.I16 => I16x8Equal,
            SimdLaneShape.I32 => I32x4Equal,
            _ => I64x2Equal,
        });
        Simd(code, V128BitSelect);
        Set(code, request.Local(0, CliValueKind.V128));
    }

    private void ThrowOutsideLanes(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int index,
        SimdLane lane)
    {
        Get(code, index);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(16 / LaneSize(lane))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(
            code,
            ManagedExceptionKind.ArgumentOutOfRange,
            request.Instruction.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
