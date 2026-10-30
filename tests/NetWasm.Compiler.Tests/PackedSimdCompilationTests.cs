using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// PackedSimd was the PlatformNotSupported class. Each of upstream's overloads
// is one Wasm SIMD instruction, and lowers to it, on both targets.
public sealed partial class PackedSimdCompilationTests
{
    // One call of every overload, generated from upstream's PackedSimd.cs by
    // eng/packedsimd/generate_packedsimd_calls.py.
    private const string EveryOverload = """
        using System.Runtime.Intrinsics;
        using System.Runtime.Intrinsics.Wasm;

        public static unsafe class EntryPoint
        {
            private static int s_count;

            private static void Sink<T>(T value) => s_count++;

            public static int Run(int input)
            {
                var buffer = stackalloc byte[64];
                Sink(PackedSimd.Splat((sbyte)1));
                Sink(PackedSimd.Splat((byte)1));
                Sink(PackedSimd.Splat((short)1));
                Sink(PackedSimd.Splat((ushort)1));
                Sink(PackedSimd.Splat((int)1));
                Sink(PackedSimd.Splat((uint)1));
                Sink(PackedSimd.Splat((long)1));
                Sink(PackedSimd.Splat((ulong)1));
                Sink(PackedSimd.Splat((float)1));
                Sink(PackedSimd.Splat((double)1));
                Sink(PackedSimd.Splat((nint)1));
                Sink(PackedSimd.Splat((nuint)1));
                Sink(PackedSimd.ExtractScalar(Vector128.Create((sbyte)1), (byte)0));
                Sink(PackedSimd.ExtractScalar(Vector128.Create((byte)1), (byte)0));
                Sink(PackedSimd.ExtractScalar(Vector128.Create((short)1), (byte)0));
                Sink(PackedSimd.ExtractScalar(Vector128.Create((ushort)1), (byte)0));
                Sink(PackedSimd.ExtractScalar(Vector128.Create((int)1), (byte)0));
                Sink(PackedSimd.ExtractScalar(Vector128.Create((uint)1), (byte)0));
                Sink(PackedSimd.ExtractScalar(Vector128.Create((long)1), (byte)0));
                Sink(PackedSimd.ExtractScalar(Vector128.Create((ulong)1), (byte)0));
                Sink(PackedSimd.ExtractScalar(Vector128.Create((float)1), (byte)0));
                Sink(PackedSimd.ExtractScalar(Vector128.Create((double)1), (byte)0));
                Sink(PackedSimd.ExtractScalar(Vector128.Create((nint)1), (byte)0));
                Sink(PackedSimd.ExtractScalar(Vector128.Create((nuint)1), (byte)0));
                Sink(PackedSimd.ReplaceScalar(Vector128.Create((sbyte)1), (byte)0, (int)1));
                Sink(PackedSimd.ReplaceScalar(Vector128.Create((byte)1), (byte)0, (uint)1));
                Sink(PackedSimd.ReplaceScalar(Vector128.Create((short)1), (byte)0, (int)1));
                Sink(PackedSimd.ReplaceScalar(Vector128.Create((ushort)1), (byte)0, (uint)1));
                Sink(PackedSimd.ReplaceScalar(Vector128.Create((int)1), (byte)0, (int)1));
                Sink(PackedSimd.ReplaceScalar(Vector128.Create((uint)1), (byte)0, (uint)1));
                Sink(PackedSimd.ReplaceScalar(Vector128.Create((long)1), (byte)0, (long)1));
                Sink(PackedSimd.ReplaceScalar(Vector128.Create((ulong)1), (byte)0, (ulong)1));
                Sink(PackedSimd.ReplaceScalar(Vector128.Create((float)1), (byte)0, (float)1));
                Sink(PackedSimd.ReplaceScalar(Vector128.Create((double)1), (byte)0, (double)1));
                Sink(PackedSimd.ReplaceScalar(Vector128.Create((nint)1), (byte)0, (nint)1));
                Sink(PackedSimd.ReplaceScalar(Vector128.Create((nuint)1), (byte)0, (nuint)1));
                Sink(PackedSimd.Swizzle(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.Swizzle(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.Add(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.Add(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.Add(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.Add(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.Add(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.Add(Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.Add(Vector128.Create((long)1), Vector128.Create((long)1)));
                Sink(PackedSimd.Add(Vector128.Create((ulong)1), Vector128.Create((ulong)1)));
                Sink(PackedSimd.Add(Vector128.Create((nint)1), Vector128.Create((nint)1)));
                Sink(PackedSimd.Add(Vector128.Create((nuint)1), Vector128.Create((nuint)1)));
                Sink(PackedSimd.Subtract(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.Subtract(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.Subtract(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.Subtract(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.Subtract(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.Subtract(Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.Subtract(Vector128.Create((long)1), Vector128.Create((long)1)));
                Sink(PackedSimd.Subtract(Vector128.Create((ulong)1), Vector128.Create((ulong)1)));
                Sink(PackedSimd.Subtract(Vector128.Create((nint)1), Vector128.Create((nint)1)));
                Sink(PackedSimd.Subtract(Vector128.Create((nuint)1), Vector128.Create((nuint)1)));
                Sink(PackedSimd.Multiply(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.Multiply(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.Multiply(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.Multiply(Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.Multiply(Vector128.Create((long)1), Vector128.Create((long)1)));
                Sink(PackedSimd.Multiply(Vector128.Create((ulong)1), Vector128.Create((ulong)1)));
                Sink(PackedSimd.Multiply(Vector128.Create((nint)1), Vector128.Create((nint)1)));
                Sink(PackedSimd.Multiply(Vector128.Create((nuint)1), Vector128.Create((nuint)1)));
                Sink(PackedSimd.Dot(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.Negate(Vector128.Create((sbyte)1)));
                Sink(PackedSimd.Negate(Vector128.Create((byte)1)));
                Sink(PackedSimd.Negate(Vector128.Create((short)1)));
                Sink(PackedSimd.Negate(Vector128.Create((ushort)1)));
                Sink(PackedSimd.Negate(Vector128.Create((int)1)));
                Sink(PackedSimd.Negate(Vector128.Create((uint)1)));
                Sink(PackedSimd.Negate(Vector128.Create((long)1)));
                Sink(PackedSimd.Negate(Vector128.Create((ulong)1)));
                Sink(PackedSimd.Negate(Vector128.Create((nint)1)));
                Sink(PackedSimd.Negate(Vector128.Create((nuint)1)));
                Sink(PackedSimd.MultiplyWideningLower(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.MultiplyWideningLower(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.MultiplyWideningLower(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.MultiplyWideningLower(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.MultiplyWideningLower(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.MultiplyWideningLower(Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.MultiplyWideningUpper(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.MultiplyWideningUpper(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.MultiplyWideningUpper(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.MultiplyWideningUpper(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.MultiplyWideningUpper(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.MultiplyWideningUpper(Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.AddPairwiseWidening(Vector128.Create((sbyte)1)));
                Sink(PackedSimd.AddPairwiseWidening(Vector128.Create((byte)1)));
                Sink(PackedSimd.AddPairwiseWidening(Vector128.Create((short)1)));
                Sink(PackedSimd.AddPairwiseWidening(Vector128.Create((ushort)1)));
                Sink(PackedSimd.AddSaturate(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.AddSaturate(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.AddSaturate(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.AddSaturate(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.SubtractSaturate(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.SubtractSaturate(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.SubtractSaturate(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.SubtractSaturate(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.MultiplyRoundedSaturateQ15(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.Min(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.Min(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.Min(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.Min(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.Min(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.Min(Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.Max(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.Max(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.Max(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.Max(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.Max(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.Max(Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.AverageRounded(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.AverageRounded(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.Abs(Vector128.Create((sbyte)1)));
                Sink(PackedSimd.Abs(Vector128.Create((short)1)));
                Sink(PackedSimd.Abs(Vector128.Create((int)1)));
                Sink(PackedSimd.Abs(Vector128.Create((long)1)));
                Sink(PackedSimd.Abs(Vector128.Create((nint)1)));
                Sink(PackedSimd.ShiftLeft(Vector128.Create((sbyte)1), (int)1));
                Sink(PackedSimd.ShiftLeft(Vector128.Create((byte)1), (int)1));
                Sink(PackedSimd.ShiftLeft(Vector128.Create((short)1), (int)1));
                Sink(PackedSimd.ShiftLeft(Vector128.Create((ushort)1), (int)1));
                Sink(PackedSimd.ShiftLeft(Vector128.Create((int)1), (int)1));
                Sink(PackedSimd.ShiftLeft(Vector128.Create((uint)1), (int)1));
                Sink(PackedSimd.ShiftLeft(Vector128.Create((long)1), (int)1));
                Sink(PackedSimd.ShiftLeft(Vector128.Create((ulong)1), (int)1));
                Sink(PackedSimd.ShiftLeft(Vector128.Create((nint)1), (int)1));
                Sink(PackedSimd.ShiftLeft(Vector128.Create((nuint)1), (int)1));
                Sink(PackedSimd.ShiftRightArithmetic(Vector128.Create((sbyte)1), (int)1));
                Sink(PackedSimd.ShiftRightArithmetic(Vector128.Create((byte)1), (int)1));
                Sink(PackedSimd.ShiftRightArithmetic(Vector128.Create((short)1), (int)1));
                Sink(PackedSimd.ShiftRightArithmetic(Vector128.Create((ushort)1), (int)1));
                Sink(PackedSimd.ShiftRightArithmetic(Vector128.Create((int)1), (int)1));
                Sink(PackedSimd.ShiftRightArithmetic(Vector128.Create((uint)1), (int)1));
                Sink(PackedSimd.ShiftRightArithmetic(Vector128.Create((long)1), (int)1));
                Sink(PackedSimd.ShiftRightArithmetic(Vector128.Create((ulong)1), (int)1));
                Sink(PackedSimd.ShiftRightArithmetic(Vector128.Create((nint)1), (int)1));
                Sink(PackedSimd.ShiftRightArithmetic(Vector128.Create((nuint)1), (int)1));
                Sink(PackedSimd.ShiftRightLogical(Vector128.Create((sbyte)1), (int)1));
                Sink(PackedSimd.ShiftRightLogical(Vector128.Create((byte)1), (int)1));
                Sink(PackedSimd.ShiftRightLogical(Vector128.Create((short)1), (int)1));
                Sink(PackedSimd.ShiftRightLogical(Vector128.Create((ushort)1), (int)1));
                Sink(PackedSimd.ShiftRightLogical(Vector128.Create((int)1), (int)1));
                Sink(PackedSimd.ShiftRightLogical(Vector128.Create((uint)1), (int)1));
                Sink(PackedSimd.ShiftRightLogical(Vector128.Create((long)1), (int)1));
                Sink(PackedSimd.ShiftRightLogical(Vector128.Create((ulong)1), (int)1));
                Sink(PackedSimd.ShiftRightLogical(Vector128.Create((nint)1), (int)1));
                Sink(PackedSimd.ShiftRightLogical(Vector128.Create((nuint)1), (int)1));
                Sink(PackedSimd.And(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.And(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.And(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.And(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.And(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.And(Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.And(Vector128.Create((long)1), Vector128.Create((long)1)));
                Sink(PackedSimd.And(Vector128.Create((ulong)1), Vector128.Create((ulong)1)));
                Sink(PackedSimd.And(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.And(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.And(Vector128.Create((nint)1), Vector128.Create((nint)1)));
                Sink(PackedSimd.And(Vector128.Create((nuint)1), Vector128.Create((nuint)1)));
                Sink(PackedSimd.Or(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.Or(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.Or(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.Or(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.Or(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.Or(Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.Or(Vector128.Create((long)1), Vector128.Create((long)1)));
                Sink(PackedSimd.Or(Vector128.Create((ulong)1), Vector128.Create((ulong)1)));
                Sink(PackedSimd.Or(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.Or(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.Or(Vector128.Create((nint)1), Vector128.Create((nint)1)));
                Sink(PackedSimd.Or(Vector128.Create((nuint)1), Vector128.Create((nuint)1)));
                Sink(PackedSimd.Xor(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.Xor(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.Xor(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.Xor(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.Xor(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.Xor(Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.Xor(Vector128.Create((long)1), Vector128.Create((long)1)));
                Sink(PackedSimd.Xor(Vector128.Create((ulong)1), Vector128.Create((ulong)1)));
                Sink(PackedSimd.Xor(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.Xor(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.Xor(Vector128.Create((nint)1), Vector128.Create((nint)1)));
                Sink(PackedSimd.Xor(Vector128.Create((nuint)1), Vector128.Create((nuint)1)));
                Sink(PackedSimd.Not(Vector128.Create((sbyte)1)));
                Sink(PackedSimd.Not(Vector128.Create((byte)1)));
                Sink(PackedSimd.Not(Vector128.Create((short)1)));
                Sink(PackedSimd.Not(Vector128.Create((ushort)1)));
                Sink(PackedSimd.Not(Vector128.Create((int)1)));
                Sink(PackedSimd.Not(Vector128.Create((uint)1)));
                Sink(PackedSimd.Not(Vector128.Create((long)1)));
                Sink(PackedSimd.Not(Vector128.Create((ulong)1)));
                Sink(PackedSimd.Not(Vector128.Create((float)1)));
                Sink(PackedSimd.Not(Vector128.Create((double)1)));
                Sink(PackedSimd.Not(Vector128.Create((nint)1)));
                Sink(PackedSimd.Not(Vector128.Create((nuint)1)));
                Sink(PackedSimd.AndNot(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.AndNot(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.AndNot(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.AndNot(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.AndNot(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.AndNot(Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.AndNot(Vector128.Create((long)1), Vector128.Create((long)1)));
                Sink(PackedSimd.AndNot(Vector128.Create((ulong)1), Vector128.Create((ulong)1)));
                Sink(PackedSimd.AndNot(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.AndNot(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.AndNot(Vector128.Create((nint)1), Vector128.Create((nint)1)));
                Sink(PackedSimd.AndNot(Vector128.Create((nuint)1), Vector128.Create((nuint)1)));
                Sink(PackedSimd.BitwiseSelect(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.BitwiseSelect(Vector128.Create((byte)1), Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.BitwiseSelect(Vector128.Create((short)1), Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.BitwiseSelect(Vector128.Create((ushort)1), Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.BitwiseSelect(Vector128.Create((int)1), Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.BitwiseSelect(Vector128.Create((uint)1), Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.BitwiseSelect(Vector128.Create((long)1), Vector128.Create((long)1), Vector128.Create((long)1)));
                Sink(PackedSimd.BitwiseSelect(Vector128.Create((ulong)1), Vector128.Create((ulong)1), Vector128.Create((ulong)1)));
                Sink(PackedSimd.BitwiseSelect(Vector128.Create((float)1), Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.BitwiseSelect(Vector128.Create((double)1), Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.BitwiseSelect(Vector128.Create((nint)1), Vector128.Create((nint)1), Vector128.Create((nint)1)));
                Sink(PackedSimd.BitwiseSelect(Vector128.Create((nuint)1), Vector128.Create((nuint)1), Vector128.Create((nuint)1)));
                Sink(PackedSimd.PopCount(Vector128.Create((byte)1)));
                Sink(PackedSimd.AnyTrue(Vector128.Create((sbyte)1)));
                Sink(PackedSimd.AnyTrue(Vector128.Create((byte)1)));
                Sink(PackedSimd.AnyTrue(Vector128.Create((short)1)));
                Sink(PackedSimd.AnyTrue(Vector128.Create((ushort)1)));
                Sink(PackedSimd.AnyTrue(Vector128.Create((int)1)));
                Sink(PackedSimd.AnyTrue(Vector128.Create((uint)1)));
                Sink(PackedSimd.AnyTrue(Vector128.Create((long)1)));
                Sink(PackedSimd.AnyTrue(Vector128.Create((ulong)1)));
                Sink(PackedSimd.AnyTrue(Vector128.Create((float)1)));
                Sink(PackedSimd.AnyTrue(Vector128.Create((double)1)));
                Sink(PackedSimd.AnyTrue(Vector128.Create((nint)1)));
                Sink(PackedSimd.AnyTrue(Vector128.Create((nuint)1)));
                Sink(PackedSimd.AllTrue(Vector128.Create((sbyte)1)));
                Sink(PackedSimd.AllTrue(Vector128.Create((byte)1)));
                Sink(PackedSimd.AllTrue(Vector128.Create((short)1)));
                Sink(PackedSimd.AllTrue(Vector128.Create((ushort)1)));
                Sink(PackedSimd.AllTrue(Vector128.Create((int)1)));
                Sink(PackedSimd.AllTrue(Vector128.Create((uint)1)));
                Sink(PackedSimd.AllTrue(Vector128.Create((long)1)));
                Sink(PackedSimd.AllTrue(Vector128.Create((ulong)1)));
                Sink(PackedSimd.AllTrue(Vector128.Create((nint)1)));
                Sink(PackedSimd.AllTrue(Vector128.Create((nuint)1)));
                Sink(PackedSimd.Bitmask(Vector128.Create((sbyte)1)));
                Sink(PackedSimd.Bitmask(Vector128.Create((byte)1)));
                Sink(PackedSimd.Bitmask(Vector128.Create((short)1)));
                Sink(PackedSimd.Bitmask(Vector128.Create((ushort)1)));
                Sink(PackedSimd.Bitmask(Vector128.Create((int)1)));
                Sink(PackedSimd.Bitmask(Vector128.Create((uint)1)));
                Sink(PackedSimd.Bitmask(Vector128.Create((long)1)));
                Sink(PackedSimd.Bitmask(Vector128.Create((ulong)1)));
                Sink(PackedSimd.Bitmask(Vector128.Create((nint)1)));
                Sink(PackedSimd.Bitmask(Vector128.Create((nuint)1)));
                Sink(PackedSimd.CompareEqual(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.CompareEqual(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.CompareEqual(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.CompareEqual(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.CompareEqual(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.CompareEqual(Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.CompareEqual(Vector128.Create((long)1), Vector128.Create((long)1)));
                Sink(PackedSimd.CompareEqual(Vector128.Create((ulong)1), Vector128.Create((ulong)1)));
                Sink(PackedSimd.CompareEqual(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.CompareEqual(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.CompareEqual(Vector128.Create((nint)1), Vector128.Create((nint)1)));
                Sink(PackedSimd.CompareEqual(Vector128.Create((nuint)1), Vector128.Create((nuint)1)));
                Sink(PackedSimd.CompareNotEqual(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.CompareNotEqual(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.CompareNotEqual(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.CompareNotEqual(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.CompareNotEqual(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.CompareNotEqual(Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.CompareNotEqual(Vector128.Create((long)1), Vector128.Create((long)1)));
                Sink(PackedSimd.CompareNotEqual(Vector128.Create((ulong)1), Vector128.Create((ulong)1)));
                Sink(PackedSimd.CompareNotEqual(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.CompareNotEqual(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.CompareNotEqual(Vector128.Create((nint)1), Vector128.Create((nint)1)));
                Sink(PackedSimd.CompareNotEqual(Vector128.Create((nuint)1), Vector128.Create((nuint)1)));
                Sink(PackedSimd.CompareLessThan(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.CompareLessThan(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.CompareLessThan(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.CompareLessThan(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.CompareLessThan(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.CompareLessThan(Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.CompareLessThan(Vector128.Create((long)1), Vector128.Create((long)1)));
                Sink(PackedSimd.CompareLessThan(Vector128.Create((ulong)1), Vector128.Create((ulong)1)));
                Sink(PackedSimd.CompareLessThan(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.CompareLessThan(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.CompareLessThan(Vector128.Create((nint)1), Vector128.Create((nint)1)));
                Sink(PackedSimd.CompareLessThan(Vector128.Create((nuint)1), Vector128.Create((nuint)1)));
                Sink(PackedSimd.CompareLessThanOrEqual(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.CompareLessThanOrEqual(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.CompareLessThanOrEqual(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.CompareLessThanOrEqual(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.CompareLessThanOrEqual(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.CompareLessThanOrEqual(Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.CompareLessThanOrEqual(Vector128.Create((long)1), Vector128.Create((long)1)));
                Sink(PackedSimd.CompareLessThanOrEqual(Vector128.Create((ulong)1), Vector128.Create((ulong)1)));
                Sink(PackedSimd.CompareLessThanOrEqual(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.CompareLessThanOrEqual(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.CompareLessThanOrEqual(Vector128.Create((nint)1), Vector128.Create((nint)1)));
                Sink(PackedSimd.CompareLessThanOrEqual(Vector128.Create((nuint)1), Vector128.Create((nuint)1)));
                Sink(PackedSimd.CompareGreaterThan(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.CompareGreaterThan(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.CompareGreaterThan(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.CompareGreaterThan(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.CompareGreaterThan(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.CompareGreaterThan(Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.CompareGreaterThan(Vector128.Create((long)1), Vector128.Create((long)1)));
                Sink(PackedSimd.CompareGreaterThan(Vector128.Create((ulong)1), Vector128.Create((ulong)1)));
                Sink(PackedSimd.CompareGreaterThan(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.CompareGreaterThan(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.CompareGreaterThan(Vector128.Create((nint)1), Vector128.Create((nint)1)));
                Sink(PackedSimd.CompareGreaterThan(Vector128.Create((nuint)1), Vector128.Create((nuint)1)));
                Sink(PackedSimd.CompareGreaterThanOrEqual(Vector128.Create((sbyte)1), Vector128.Create((sbyte)1)));
                Sink(PackedSimd.CompareGreaterThanOrEqual(Vector128.Create((byte)1), Vector128.Create((byte)1)));
                Sink(PackedSimd.CompareGreaterThanOrEqual(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.CompareGreaterThanOrEqual(Vector128.Create((ushort)1), Vector128.Create((ushort)1)));
                Sink(PackedSimd.CompareGreaterThanOrEqual(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.CompareGreaterThanOrEqual(Vector128.Create((uint)1), Vector128.Create((uint)1)));
                Sink(PackedSimd.CompareGreaterThanOrEqual(Vector128.Create((long)1), Vector128.Create((long)1)));
                Sink(PackedSimd.CompareGreaterThanOrEqual(Vector128.Create((ulong)1), Vector128.Create((ulong)1)));
                Sink(PackedSimd.CompareGreaterThanOrEqual(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.CompareGreaterThanOrEqual(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.CompareGreaterThanOrEqual(Vector128.Create((nint)1), Vector128.Create((nint)1)));
                Sink(PackedSimd.CompareGreaterThanOrEqual(Vector128.Create((nuint)1), Vector128.Create((nuint)1)));
                Sink(PackedSimd.LoadVector128((sbyte*)buffer));
                Sink(PackedSimd.LoadVector128((byte*)buffer));
                Sink(PackedSimd.LoadVector128((short*)buffer));
                Sink(PackedSimd.LoadVector128((ushort*)buffer));
                Sink(PackedSimd.LoadVector128((int*)buffer));
                Sink(PackedSimd.LoadVector128((uint*)buffer));
                Sink(PackedSimd.LoadVector128((long*)buffer));
                Sink(PackedSimd.LoadVector128((ulong*)buffer));
                Sink(PackedSimd.LoadVector128((float*)buffer));
                Sink(PackedSimd.LoadVector128((double*)buffer));
                Sink(PackedSimd.LoadVector128((nint*)buffer));
                Sink(PackedSimd.LoadVector128((nuint*)buffer));
                Sink(PackedSimd.LoadScalarVector128((int*)buffer));
                Sink(PackedSimd.LoadScalarVector128((uint*)buffer));
                Sink(PackedSimd.LoadScalarVector128((long*)buffer));
                Sink(PackedSimd.LoadScalarVector128((ulong*)buffer));
                Sink(PackedSimd.LoadScalarVector128((float*)buffer));
                Sink(PackedSimd.LoadScalarVector128((double*)buffer));
                Sink(PackedSimd.LoadScalarVector128((nint*)buffer));
                Sink(PackedSimd.LoadScalarVector128((nuint*)buffer));
                Sink(PackedSimd.LoadScalarAndSplatVector128((sbyte*)buffer));
                Sink(PackedSimd.LoadScalarAndSplatVector128((byte*)buffer));
                Sink(PackedSimd.LoadScalarAndSplatVector128((short*)buffer));
                Sink(PackedSimd.LoadScalarAndSplatVector128((ushort*)buffer));
                Sink(PackedSimd.LoadScalarAndSplatVector128((int*)buffer));
                Sink(PackedSimd.LoadScalarAndSplatVector128((uint*)buffer));
                Sink(PackedSimd.LoadScalarAndSplatVector128((long*)buffer));
                Sink(PackedSimd.LoadScalarAndSplatVector128((ulong*)buffer));
                Sink(PackedSimd.LoadScalarAndSplatVector128((float*)buffer));
                Sink(PackedSimd.LoadScalarAndSplatVector128((double*)buffer));
                Sink(PackedSimd.LoadScalarAndSplatVector128((nint*)buffer));
                Sink(PackedSimd.LoadScalarAndSplatVector128((nuint*)buffer));
                Sink(PackedSimd.LoadScalarAndInsert((sbyte*)buffer, Vector128.Create((sbyte)1), (byte)0));
                Sink(PackedSimd.LoadScalarAndInsert((byte*)buffer, Vector128.Create((byte)1), (byte)0));
                Sink(PackedSimd.LoadScalarAndInsert((short*)buffer, Vector128.Create((short)1), (byte)0));
                Sink(PackedSimd.LoadScalarAndInsert((ushort*)buffer, Vector128.Create((ushort)1), (byte)0));
                Sink(PackedSimd.LoadScalarAndInsert((int*)buffer, Vector128.Create((int)1), (byte)0));
                Sink(PackedSimd.LoadScalarAndInsert((uint*)buffer, Vector128.Create((uint)1), (byte)0));
                Sink(PackedSimd.LoadScalarAndInsert((long*)buffer, Vector128.Create((long)1), (byte)0));
                Sink(PackedSimd.LoadScalarAndInsert((ulong*)buffer, Vector128.Create((ulong)1), (byte)0));
                Sink(PackedSimd.LoadScalarAndInsert((float*)buffer, Vector128.Create((float)1), (byte)0));
                Sink(PackedSimd.LoadScalarAndInsert((double*)buffer, Vector128.Create((double)1), (byte)0));
                Sink(PackedSimd.LoadScalarAndInsert((nint*)buffer, Vector128.Create((nint)1), (byte)0));
                Sink(PackedSimd.LoadScalarAndInsert((nuint*)buffer, Vector128.Create((nuint)1), (byte)0));
                PackedSimd.Store((sbyte*)buffer, Vector128.Create((sbyte)1));
                PackedSimd.Store((byte*)buffer, Vector128.Create((byte)1));
                PackedSimd.Store((short*)buffer, Vector128.Create((short)1));
                PackedSimd.Store((ushort*)buffer, Vector128.Create((ushort)1));
                PackedSimd.Store((int*)buffer, Vector128.Create((int)1));
                PackedSimd.Store((uint*)buffer, Vector128.Create((uint)1));
                PackedSimd.Store((long*)buffer, Vector128.Create((long)1));
                PackedSimd.Store((ulong*)buffer, Vector128.Create((ulong)1));
                PackedSimd.Store((float*)buffer, Vector128.Create((float)1));
                PackedSimd.Store((double*)buffer, Vector128.Create((double)1));
                PackedSimd.Store((nint*)buffer, Vector128.Create((nint)1));
                PackedSimd.Store((nuint*)buffer, Vector128.Create((nuint)1));
                PackedSimd.StoreSelectedScalar((sbyte*)buffer, Vector128.Create((sbyte)1), (byte)0);
                PackedSimd.StoreSelectedScalar((byte*)buffer, Vector128.Create((byte)1), (byte)0);
                PackedSimd.StoreSelectedScalar((short*)buffer, Vector128.Create((short)1), (byte)0);
                PackedSimd.StoreSelectedScalar((ushort*)buffer, Vector128.Create((ushort)1), (byte)0);
                PackedSimd.StoreSelectedScalar((int*)buffer, Vector128.Create((int)1), (byte)0);
                PackedSimd.StoreSelectedScalar((uint*)buffer, Vector128.Create((uint)1), (byte)0);
                PackedSimd.StoreSelectedScalar((long*)buffer, Vector128.Create((long)1), (byte)0);
                PackedSimd.StoreSelectedScalar((ulong*)buffer, Vector128.Create((ulong)1), (byte)0);
                PackedSimd.StoreSelectedScalar((float*)buffer, Vector128.Create((float)1), (byte)0);
                PackedSimd.StoreSelectedScalar((double*)buffer, Vector128.Create((double)1), (byte)0);
                PackedSimd.StoreSelectedScalar((nint*)buffer, Vector128.Create((nint)1), (byte)0);
                PackedSimd.StoreSelectedScalar((nuint*)buffer, Vector128.Create((nuint)1), (byte)0);
                Sink(PackedSimd.LoadWideningVector128((sbyte*)buffer));
                Sink(PackedSimd.LoadWideningVector128((byte*)buffer));
                Sink(PackedSimd.LoadWideningVector128((short*)buffer));
                Sink(PackedSimd.LoadWideningVector128((ushort*)buffer));
                Sink(PackedSimd.LoadWideningVector128((int*)buffer));
                Sink(PackedSimd.LoadWideningVector128((uint*)buffer));
                Sink(PackedSimd.Negate(Vector128.Create((float)1)));
                Sink(PackedSimd.Negate(Vector128.Create((double)1)));
                Sink(PackedSimd.Abs(Vector128.Create((float)1)));
                Sink(PackedSimd.Abs(Vector128.Create((double)1)));
                Sink(PackedSimd.Min(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.Min(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.Max(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.Max(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.PseudoMin(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.PseudoMin(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.PseudoMax(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.PseudoMax(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.Add(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.Add(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.Subtract(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.Subtract(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.Divide(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.Divide(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.Multiply(Vector128.Create((float)1), Vector128.Create((float)1)));
                Sink(PackedSimd.Multiply(Vector128.Create((double)1), Vector128.Create((double)1)));
                Sink(PackedSimd.Sqrt(Vector128.Create((float)1)));
                Sink(PackedSimd.Sqrt(Vector128.Create((double)1)));
                Sink(PackedSimd.Ceiling(Vector128.Create((float)1)));
                Sink(PackedSimd.Ceiling(Vector128.Create((double)1)));
                Sink(PackedSimd.Floor(Vector128.Create((float)1)));
                Sink(PackedSimd.Floor(Vector128.Create((double)1)));
                Sink(PackedSimd.Truncate(Vector128.Create((float)1)));
                Sink(PackedSimd.Truncate(Vector128.Create((double)1)));
                Sink(PackedSimd.RoundToNearest(Vector128.Create((float)1)));
                Sink(PackedSimd.RoundToNearest(Vector128.Create((double)1)));
                Sink(PackedSimd.ConvertToSingle(Vector128.Create((int)1)));
                Sink(PackedSimd.ConvertToSingle(Vector128.Create((uint)1)));
                Sink(PackedSimd.ConvertToSingle(Vector128.Create((double)1)));
                Sink(PackedSimd.ConvertToDoubleLower(Vector128.Create((int)1)));
                Sink(PackedSimd.ConvertToDoubleLower(Vector128.Create((uint)1)));
                Sink(PackedSimd.ConvertToDoubleLower(Vector128.Create((float)1)));
                Sink(PackedSimd.ConvertToInt32Saturate(Vector128.Create((float)1)));
                Sink(PackedSimd.ConvertToUInt32Saturate(Vector128.Create((float)1)));
                Sink(PackedSimd.ConvertToInt32Saturate(Vector128.Create((double)1)));
                Sink(PackedSimd.ConvertToUInt32Saturate(Vector128.Create((double)1)));
                Sink(PackedSimd.ConvertNarrowingSaturateSigned(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.ConvertNarrowingSaturateSigned(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.ConvertNarrowingSaturateUnsigned(Vector128.Create((short)1), Vector128.Create((short)1)));
                Sink(PackedSimd.ConvertNarrowingSaturateUnsigned(Vector128.Create((int)1), Vector128.Create((int)1)));
                Sink(PackedSimd.SignExtendWideningLower(Vector128.Create((sbyte)1)));
                Sink(PackedSimd.SignExtendWideningLower(Vector128.Create((byte)1)));
                Sink(PackedSimd.SignExtendWideningLower(Vector128.Create((short)1)));
                Sink(PackedSimd.SignExtendWideningLower(Vector128.Create((ushort)1)));
                Sink(PackedSimd.SignExtendWideningLower(Vector128.Create((int)1)));
                Sink(PackedSimd.SignExtendWideningLower(Vector128.Create((uint)1)));
                Sink(PackedSimd.SignExtendWideningUpper(Vector128.Create((sbyte)1)));
                Sink(PackedSimd.SignExtendWideningUpper(Vector128.Create((byte)1)));
                Sink(PackedSimd.SignExtendWideningUpper(Vector128.Create((short)1)));
                Sink(PackedSimd.SignExtendWideningUpper(Vector128.Create((ushort)1)));
                Sink(PackedSimd.SignExtendWideningUpper(Vector128.Create((int)1)));
                Sink(PackedSimd.SignExtendWideningUpper(Vector128.Create((uint)1)));
                Sink(PackedSimd.ZeroExtendWideningLower(Vector128.Create((sbyte)1)));
                Sink(PackedSimd.ZeroExtendWideningLower(Vector128.Create((byte)1)));
                Sink(PackedSimd.ZeroExtendWideningLower(Vector128.Create((short)1)));
                Sink(PackedSimd.ZeroExtendWideningLower(Vector128.Create((ushort)1)));
                Sink(PackedSimd.ZeroExtendWideningLower(Vector128.Create((int)1)));
                Sink(PackedSimd.ZeroExtendWideningLower(Vector128.Create((uint)1)));
                Sink(PackedSimd.ZeroExtendWideningUpper(Vector128.Create((sbyte)1)));
                Sink(PackedSimd.ZeroExtendWideningUpper(Vector128.Create((byte)1)));
                Sink(PackedSimd.ZeroExtendWideningUpper(Vector128.Create((short)1)));
                Sink(PackedSimd.ZeroExtendWideningUpper(Vector128.Create((ushort)1)));
                Sink(PackedSimd.ZeroExtendWideningUpper(Vector128.Create((int)1)));
                Sink(PackedSimd.ZeroExtendWideningUpper(Vector128.Create((uint)1)));
                return s_count;
            }
        }
        """;

    private const string Semantics = """
        using System;
        using System.Runtime.Intrinsics;
        using System.Runtime.Intrinsics.Wasm;

        public static unsafe class EntryPoint
        {
            private static int s_failures;
            private static int s_check;

            private static void Check(bool passed)
            {
                if (!passed) s_failures |= 1 << s_check;
                s_check++;
            }

            public static int Run(int input)
            {
                Check(PackedSimd.IsSupported);
                Check(PackedSimd.Add(Vector128.Create(1, -2, 3, int.MaxValue), Vector128.Create(5, 6, -7, 1)) ==
                    Vector128.Create(6, 4, -4, int.MinValue));
                // Lanes out of range select zero.
                Check(PackedSimd.Swizzle(
                        Vector128.Create((byte)10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25),
                        Vector128.Create((byte)15, 0, 1, 200, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 16)) ==
                    Vector128.Create((byte)25, 10, 11, 0, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 0));
                Check(PackedSimd.ConvertNarrowingSaturateUnsigned(
                        Vector128.Create((short)-5, 300, 255, 0, 1, 2, 3, 4),
                        Vector128.Create((short)7, 8, 9, 10, 11, 12, 13, 32767)) ==
                    Vector128.Create((byte)0, 255, 255, 0, 1, 2, 3, 4, 7, 8, 9, 10, 11, 12, 13, 255));
                Check(PackedSimd.AddPairwiseWidening(Vector128.Create((byte)255)) == Vector128.Create((ushort)510));
                Check(PackedSimd.Dot(Vector128.Create((short)1, 2, 3, 4, 5, 6, 7, 8), Vector128.Create((short)2)) ==
                    Vector128.Create(6, 14, 22, 30));
                Check(PackedSimd.Bitmask(Vector128.Create(-1, 0, -1, 1)) == 0b0101);
                Check(PackedSimd.AllTrue(Vector128.Create(1, 2, 3, 4)) &&
                    !PackedSimd.AllTrue(Vector128.Create(1, 0, 3, 4)) &&
                    PackedSimd.AnyTrue(Vector128.Create(0, 0, 0, 4)));
                Check(PackedSimd.ShiftRightArithmetic(Vector128.Create(-16), 2 + input) == Vector128.Create(-4 >> input));
                // Native-width lanes follow the target, with unsigned comparisons
                // on 64-bit lanes.
                Check(PackedSimd.CompareLessThan(Vector128.Create((nuint)1), Vector128.Create(nuint.MaxValue)) ==
                    Vector128<nuint>.AllBitsSet);
                Check(PackedSimd.Multiply(PackedSimd.Splat((nint)(-3)), Vector128.Create((nint)5)) ==
                    Vector128.Create((nint)(-15)));

                var data = stackalloc int[8] { 1, 2, 3, 4, 5, 6, 7, 8 };
                Check(PackedSimd.LoadVector128(data + 1) == Vector128.Create(2, 3, 4, 5));
                Check(PackedSimd.LoadScalarAndSplatVector128(data + 2) == Vector128.Create(3));
                Check(PackedSimd.LoadScalarVector128(data + 3) == Vector128.Create(4, 0, 0, 0));
                var shorts = stackalloc short[4] { -1, 2, -3, 4 };
                Check(PackedSimd.LoadWideningVector128(shorts) == Vector128.Create(-1, 2, -3, 4));
                PackedSimd.Store(data, Vector128.Create(9, 10, 11, 12));
                Check(data[0] == 9 && data[3] == 12 && data[4] == 5);

                // Constant and variable lane indices.
                var lane = input + 2;
                Check(PackedSimd.ExtractScalar(Vector128.Create((sbyte)-1), 3) == -1 &&
                    PackedSimd.ExtractScalar(Vector128.Create((byte)255), (byte)lane) == 255u);
                Check(PackedSimd.ExtractScalar(Vector128.Create(1.5, 2.5), 1) == 2.5);
                Check(PackedSimd.ReplaceScalar(Vector128.Create(1, 2, 3, 4), (byte)lane, 9) ==
                    Vector128.Create(1, 2, 3, 4).WithElement(lane, 9));
                Check(PackedSimd.LoadScalarAndInsert(data + 1, Vector128.Create(0), 3) == Vector128.Create(0, 0, 0, 10));
                PackedSimd.StoreSelectedScalar(data + 7, Vector128.Create(5, 6, 7, 8), (byte)lane);
                Check(data[7] == 5 + lane);
                try
                {
                    _ = PackedSimd.ExtractScalar(Vector128.Create(1), (byte)(lane + 2));
                    Check(false);
                }
                catch (ArgumentOutOfRangeException)
                {
                    Check(true);
                }
                return s_failures;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void EveryOverloadLowersToItsInstruction(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource("PackedSimdOverloads", EveryOverload);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var call = PackedSimdCall().Match(printed.StandardOutput);
        Assert.False(call.Success, call.Value);
        Assert.Equal(EveryOverload.Split("Sink(").Length - 1,
            ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void InstructionsComputeWhatUpstreamDocuments(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource("PackedSimdSemantics", Semantics);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        foreach (var input in new[] { 0, 1 })
        {
            Assert.Equal(0,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    [GeneratedRegex(@"call \$[^\s]*PackedSimd::\w+")]
    private static partial Regex PackedSimdCall();
}
