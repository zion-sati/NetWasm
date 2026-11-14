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

// The lane-wise comparisons, and the All and Any comparisons of every lane.
internal sealed class SimdVectorComparisonIntrinsicEmitter(
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
            case RuntimeIntrinsic.SimdEqualsAll:
            case RuntimeIntrinsic.SimdNotEqualsAll:
            case RuntimeIntrinsic.SimdEqualsAny:
                Compare(request, code, lane, SimdComparison.Equal);
                if (request.Intrinsic == RuntimeIntrinsic.SimdEqualsAny)
                {
                    Simd(code, V128AnyTrue);
                }
                else
                {
                    Simd(code, AllTrue(lane.Bits));
                    if (request.Intrinsic == RuntimeIntrinsic.SimdNotEqualsAll)
                    {
                        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
                    }
                }
                Set(code, request.Local(0, CliValueKind.I4));
                return;
            case RuntimeIntrinsic.SimdCompareAll:
            case RuntimeIntrinsic.SimdCompareAny:
                // LessThanAll, GreaterThanOrEqualAny and the rest: every lane, or
                // any lane, of the comparison.
                var name = request.Method.Definition.Name;
                Compare(request, code, lane, name.StartsWith("LessThanOrEqual", StringComparison.Ordinal) ? SimdComparison.LessOrEqual
                    : name.StartsWith("LessThan", StringComparison.Ordinal) ? SimdComparison.LessThan
                    : name.StartsWith("GreaterThanOrEqual", StringComparison.Ordinal) ? SimdComparison.GreaterOrEqual
                    : SimdComparison.GreaterThan);
                Simd(code, request.Intrinsic == RuntimeIntrinsic.SimdCompareAll ? AllTrue(lane.Bits) : V128AnyTrue);
                Set(code, request.Local(0, CliValueKind.I4));
                return;
            case RuntimeIntrinsic.SimdEquals:
                Compare(request, code, lane, SimdComparison.Equal);
                break;
            case RuntimeIntrinsic.SimdLessThan:
                Compare(request, code, lane, SimdComparison.LessThan);
                break;
            case RuntimeIntrinsic.SimdGreaterThan:
                Compare(request, code, lane, SimdComparison.GreaterThan);
                break;
            case RuntimeIntrinsic.SimdLessThanOrEqual:
                Compare(request, code, lane, SimdComparison.LessOrEqual);
                break;
            default:
                // GreaterThanOrEqual.
                Compare(request, code, lane, SimdComparison.GreaterOrEqual);
                break;
        }
        Set(code, request.Local(0, CliValueKind.V128));
    }
}
