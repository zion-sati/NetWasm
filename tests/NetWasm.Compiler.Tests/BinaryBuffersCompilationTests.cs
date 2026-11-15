// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class BinaryBuffersCompilationTests
{
    [Fact]
    public void CompilerExecutesScalarBinaryBufferContracts()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "BinaryBuffersFixture",
            """
            using System;
            using System.Buffers;
            using System.Buffers.Binary;

            namespace BinaryBuffersFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var bytes = new byte[8];
                    BinaryPrimitives.WriteInt32BigEndian(bytes, input);
                    if (BinaryPrimitives.ReadInt32BigEndian(bytes) != input) return 1;
                    if (BinaryPrimitives.ReadInt32LittleEndian(bytes) !=
                        BinaryPrimitives.ReverseEndianness(input)) return 2;
                    if (!BinaryPrimitives.TryReadInt32BigEndian(bytes, out var roundTrip) ||
                        roundTrip != input) return 3;
                    if (!BinaryPrimitives.TryWriteUInt16LittleEndian(bytes, 0xabcd) ||
                        bytes[0] != 0xcd || bytes[1] != 0xab) return 4;
                    if (BitConverter.ToInt32(BitConverter.GetBytes(input), 0) != input) return 5;
                    if (BitConverter.ToString(new byte[] { 0x7f, 0x2c, 0x00 }) != "7F-2C-00") return 6;
                    var format = StandardFormat.Parse("X2");
                    if (format.Symbol != 'X' || format.Precision != 2 ||
                        !format.HasPrecision || format.ToString() != "X2") return 7;
                    if (StandardFormat.Parse("G99").ToString() != "G99") return 13;
                    if (StandardFormat.Parse((string?)null).ToString() != string.Empty) return 15;
                    if (StandardFormat.TryParse("G100", out _)) return 16;
                    try
                    {
                        StandardFormat.Parse("Gx");
                        return 14;
                    }
                    catch (FormatException)
                    {
                    }
                    if (OperationStatus.Done != (OperationStatus)0 ||
                        OperationStatus.InvalidData != (OperationStatus)3) return 8;
                    var overlap = new int[] { 0x01020304, 0x11121314, 0x21222324, 0x31323334 };
                    BinaryPrimitives.ReverseEndianness(overlap.AsSpan(0, 3), overlap.AsSpan(1, 3));
                    if (overlap[0] != 0x01020304 ||
                        overlap[1] != 0x04030201 ||
                        overlap[2] != 0x14131211 ||
                        overlap[3] != 0x24232221) return 9;
                    BinaryPrimitives.ReverseEndianness(overlap.AsSpan(1, 3), overlap.AsSpan(0, 3));
                    if (overlap[0] != 0x01020304 ||
                        overlap[1] != 0x11121314 ||
                        overlap[2] != 0x21222324 ||
                        overlap[3] != 0x24232221) return 10;
                    BinaryPrimitives.ReverseEndianness(overlap, overlap);
                    if (overlap[0] != 0x04030201 ||
                        overlap[1] != 0x14131211 ||
                        overlap[2] != 0x24232221 ||
                        overlap[3] != 0x21222324) return 11;
                    try
                    {
                        BinaryPrimitives.ReverseEndianness(overlap, overlap.AsSpan(0, 3));
                        return 12;
                    }
                    catch (ArgumentException)
                    {
                    }
                    return input - input;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "BinaryBuffersFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(0, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }
}
