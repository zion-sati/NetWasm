using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;
using static NetWasm.Compiler.Wasm.WasmSimdOpcodes;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors;

internal sealed class SimdVectorLaneGuard(IImplicitExceptionEmitter exceptions) : ISimdVectorLaneGuard
{
    public bool TryResolve(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, out SimdVectorLanes lanes)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        var signature = request.Method.Signature;
        var vector = !request.Method.Definition.IsStatic ? request.Method.DeclaringType : request.Intrinsic switch
        {
            // A product may take its scalar first.
            RuntimeIntrinsic.SimdCreateBroadcast or RuntimeIntrinsic.SimdCreateElements or
                RuntimeIntrinsic.SimdCreateScalar or RuntimeIntrinsic.SimdLoad or
                RuntimeIntrinsic.SimdMultiply => signature.ReturnSignatureType,
            // Vector.ConditionalSelect(Vector<int>, Vector<float>, Vector<float>)
            // takes its condition with a different element type.
            RuntimeIntrinsic.SimdConditionalSelect => signature.ParameterSignatureTypes[1],
            _ => signature.ParameterSignatureTypes[0],
        };
        // The classifier takes only Vector128<T> and Vector<T> operands and
        // results, each with its one element type.
        var element = vector.TypeArguments[0];
        var result = default(SimdLane);
        if (!TryGetLane(element, request.Target, out var lane) ||
            request.Intrinsic is RuntimeIntrinsic.SimdReinterpret or RuntimeIntrinsic.SimdConvert or
                RuntimeIntrinsic.SimdWidenLower or RuntimeIntrinsic.SimdWidenUpper or RuntimeIntrinsic.SimdNarrow &&
            !TryGetLane(signature.ReturnSignatureType.TypeArguments[0], request.Target, out result))
        {
            exceptions.Emit(
                code,
                ManagedExceptionKind.NotSupported,
                request.Instruction.Context.ManagedThrowFunction);
            lanes = default;
            return false;
        }
        lanes = new(element, lane, result);
        return true;
    }

    // The element types upstream's vectors support.
    private static bool TryGetLane(CliTypeIdentity element, WasmTargetLayout target, out SimdLane lane)
    {
        SimdLaneShape native = target.UsesMemory64 ? SimdLaneShape.I64 : SimdLaneShape.I32;
        SimdLane? resolved = element.CanonicalName switch
        {
            "primitive:i1" => new(SimdLaneShape.I8, false),
            "primitive:u1" => new(SimdLaneShape.I8, true),
            "primitive:i2" => new(SimdLaneShape.I16, false),
            "primitive:u2" => new(SimdLaneShape.I16, true),
            "primitive:i4" => new(SimdLaneShape.I32, false),
            "primitive:u4" => new(SimdLaneShape.I32, true),
            "primitive:i8" => new(SimdLaneShape.I64, false),
            "primitive:u8" => new(SimdLaneShape.I64, true),
            "primitive:nativeint" => new(native, false),
            "primitive:nativeuint" => new(native, true),
            "primitive:f4" => new(SimdLaneShape.F32, false),
            "primitive:f8" => new(SimdLaneShape.F64, false),
            _ => null,
        };
        lane = resolved.GetValueOrDefault();
        return resolved.HasValue;
    }
}
