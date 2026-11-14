using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors;

// A vector's lane: its shape and, for integer lanes, whether it is unsigned.
internal enum SimdLaneShape { I8, I16, I32, I64, F32, F64 }

internal readonly record struct SimdLane(SimdLaneShape Shape, bool Unsigned)
{
    public bool IsFloating => Shape is SimdLaneShape.F32 or SimdLaneShape.F64;

    // The integer lanes of the same width, for bitwise views of floating lanes.
    public SimdLaneShape Bits => Shape switch
    {
        SimdLaneShape.F32 => SimdLaneShape.I32,
        SimdLaneShape.F64 => SimdLaneShape.I64,
        _ => Shape,
    };
}

internal enum SimdComparison { Equal, LessThan, GreaterThan, LessOrEqual, GreaterOrEqual }

// The lanes an intrinsic works on: its vector's element type and lane, and a conversion's result lane.
internal readonly record struct SimdVectorLanes(CliTypeIdentity Element, SimdLane Lane, SimdLane Result);
