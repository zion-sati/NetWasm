using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;
using static NetWasm.Compiler.Wasm.WasmSimdOpcodes;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors;

// The byte tables of the shuffles the lowerings use.
internal static class SimdByteShuffles
{
    internal static readonly byte[] EvenBytes =
        [0, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30];

    internal static readonly byte[] I64SignBits =
        [0, 0, 0, 0, 0, 0, 0, 0x80, 0, 0, 0, 0, 0, 0, 0, 0x80];

    // The low half of each 32-bit and 64-bit lane of two vectors, which
    // truncates them as Narrow does.
    internal static readonly byte[] EvenHalfwords =
        [0, 1, 4, 5, 8, 9, 12, 13, 16, 17, 20, 21, 24, 25, 28, 29];

    internal static readonly byte[] EvenWords =
        [0, 1, 2, 3, 8, 9, 10, 11, 16, 17, 18, 19, 24, 25, 26, 27];

    // The low two lanes of each of two vectors, and the upper two lanes of one.
    internal static readonly byte[] LowWords =
        [0, 1, 2, 3, 4, 5, 6, 7, 16, 17, 18, 19, 20, 21, 22, 23];

    internal static readonly byte[] UpperWords =
        [8, 9, 10, 11, 12, 13, 14, 15, 8, 9, 10, 11, 12, 13, 14, 15];

    // Value in every lane of the given byte size, little-endian.
    internal static byte[] Repeat(int size, long value) =>
        [.. Enumerable.Range(0, 16).Select(index => (byte)(value >> (index % size * 8)))];

    // Upstream builds these from Vector64 halves: ZipLower interleaves the lower
    // halves of its operands, UnzipEven takes the even lanes of the first and then
    // of the second, ConcatUpperLower joins the first's upper half to the second's
    // lower half. Lanes 0 to count - 1 are the first operand's, count onwards the
    // second's.
    internal static byte[] PermutedBytes(string name, int size)
    {
        var count = 16 / size;
        var source = PermutationSources[name];
        return [.. Enumerable.Range(0, count).SelectMany(lane =>
            Enumerable.Range(0, size).Select(index => (byte)(source(lane, count, count / 2) * size + index)))];
    }

    // The source lane of each result lane, given the lane count and its half;
    // the classifier admits only these names.
    internal static readonly FrozenDictionary<string, Func<int, int, int, int>> PermutationSources =
        new Dictionary<string, Func<int, int, int, int>>
        {
            ["ZipLower"] = (lane, count, _) => lane / 2 + lane % 2 * count,
            ["ZipUpper"] = (lane, count, half) => half + lane / 2 + lane % 2 * count,
            ["UnzipEven"] = (lane, _, _) => 2 * lane,
            ["UnzipOdd"] = (lane, _, _) => 2 * lane + 1,
            ["ConcatLowerLower"] = (lane, count, half) => lane < half ? lane : count + lane - half,
            ["ConcatLowerUpper"] = (lane, count, half) => lane < half ? lane : count + lane,
            ["ConcatUpperLower"] = (lane, count, half) => lane < half ? half + lane : count + lane - half,
            ["ConcatUpperUpper"] = (lane, count, half) => lane < half ? half + lane : count + lane,
        }.ToFrozenDictionary(StringComparer.Ordinal);
}
