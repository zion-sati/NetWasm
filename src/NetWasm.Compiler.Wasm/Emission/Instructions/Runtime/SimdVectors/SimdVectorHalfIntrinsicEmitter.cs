// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;
using static NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors.SimdByteShuffles;
using static NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors.SimdVectorCode;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors;

// A Vector128's Vector64 halves, as .NET's JIT expands GetLower, GetUpper, WithLower, WithUpper, Create from two
// halves and Vector64's ToVector128. A Vector64 is a v128 whose low 8 bytes hold it, as RyuJIT keeps a TYP_SIMD8 in a
// vector register, so each is at most one shuffle: GetLower and ToVector128Unsafe are the value itself, and
// ToVector128 shuffles in zeros above it.
internal sealed class SimdVectorHalfIntrinsicEmitter(ISimdVectorLaneGuard lanes) : IRuntimeIntrinsicEmitter
{
    // The first operand's low half, then the second's upper half.
    private static readonly byte[] LowThenUpper = [0, 1, 2, 3, 4, 5, 6, 7, 24, 25, 26, 27, 28, 29, 30, 31];

    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (!lanes.TryResolve(request, code, out _))
        {
            return;
        }
        var upper = request.Method.Definition.Name is "GetUpper" or "WithUpper";
        var first = request.Local(0, CliValueKind.V128);
        switch (request.Intrinsic)
        {
            case RuntimeIntrinsic.SimdGetHalf:
                if (!upper)
                {
                    // The lower half is the vector's low 8 bytes, where a Vector64 lives.
                    return;
                }
                Get(code, first);
                Get(code, first);
                Shuffle(code, UpperWords);
                break;
            case RuntimeIntrinsic.SimdWithHalf:
                var half = request.Local(1, CliValueKind.V128);
                if (upper)
                {
                    Get(code, first);
                    Get(code, half);
                    Shuffle(code, LowWords);
                }
                else
                {
                    Get(code, half);
                    Get(code, first);
                    Shuffle(code, LowThenUpper);
                }
                break;
            case RuntimeIntrinsic.SimdCreateFromHalves:
                Get(code, first);
                Get(code, request.Local(1, CliValueKind.V128));
                Shuffle(code, LowWords);
                break;
            default:
                // ToVector128 zeroes the upper half; ToVector128Unsafe leaves it undefined, so the value serves.
                if (request.Method.Definition.Name == "ToVector128Unsafe")
                {
                    return;
                }
                Get(code, first);
                VectorConstant(code, new byte[16]);
                Shuffle(code, LowWords);
                break;
        }
        Set(code, first);
    }
}
