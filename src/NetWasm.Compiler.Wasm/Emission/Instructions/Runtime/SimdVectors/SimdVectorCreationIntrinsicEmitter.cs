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

// Create of each element, of one value in every lane, and CreateScalar.
internal sealed class SimdVectorCreationIntrinsicEmitter(
    ISimdVectorLaneGuard lanes) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (!lanes.TryResolve(request, code, out var resolved))
        {
            return;
        }
        var lane = resolved.Lane;
        switch (request.Intrinsic)
        {
            case RuntimeIntrinsic.SimdCreateElements:
                // Lanes fill in registers, so the stored vector forwards to its
                // next load whole.
                var parameters = request.Method.Signature.ParameterTypes;
                Get(code, request.Local(0, parameters[0]));
                Simd(code, Splat(lane));
                for (var index = 1; index < parameters.Length; index++)
                {
                    Get(code, request.Local(index, parameters[index]));
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.SimdPrefixed,
                        WasmInstructionOperand.PrefixedPair(ReplaceLane(lane), (uint)index)));
                }
                break;
            case RuntimeIntrinsic.SimdCreateBroadcast:
                Get(code, request.Local(0, request.Method.Signature.ParameterTypes[0]));
                Simd(code, Splat(lane));
                break;
            default:
                // CreateScalar. CreateScalarUnsafe leaves the other lanes undefined, so the scalar
                // fills them too; CreateScalar zeroes them.
                if (request.Method.Definition.Name == "CreateScalarUnsafe")
                {
                    Get(code, request.Local(0, request.Method.Signature.ParameterTypes[0]));
                    Simd(code, Splat(lane));
                    break;
                }
                VectorConstant(code, new byte[16]);
                Get(code, request.Local(0, request.Method.Signature.ParameterTypes[0]));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.SimdPrefixed,
                    WasmInstructionOperand.PrefixedPair(ReplaceLane(lane), 0)));
                break;
        }
        Set(code, request.Local(0, CliValueKind.V128));
    }
}
