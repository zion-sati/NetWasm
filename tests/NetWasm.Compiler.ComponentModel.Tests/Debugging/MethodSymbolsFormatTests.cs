// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Debugging;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Debugging;

public sealed class MethodSymbolsFormatTests
{
    [Fact]
    public void ReadsEachEntryWithTextAndSkipsTheOthers()
    {
        var symbols = MethodSymbolsFormat.Parse(MethodSymbolsBuilder.Build(
            [(1, "Program::Main"), (2, null), (3, "Program::Main in Program.cs:line 8"), (4, "")]));

        Assert.Equal(new string?[] { null, "Program::Main", null, "Program::Main in Program.cs:line 8", "" }, (IEnumerable<string?>)symbols);
    }

    public static TheoryData<byte[]> NotMethodSymbols => new()
    {
        new byte[79],
        Corrupt(MethodSymbolsBuilder.Build([(1, "a")]), 0),
        MethodSymbolsBuilder.Build([(1, "a")], version: 2),
        MethodSymbolsBuilder.Build([(1, "a")], kind: 1),
        MethodSymbolsBuilder.Build([(1, "a")], stringsPerEntry: 4),
        MethodSymbolsBuilder.Build([(1, "a")], count: -1),
        MethodSymbolsBuilder.Build([(1, "a")], count: 2),
        WithTrailingByte(MethodSymbolsBuilder.Build([(1, "a")])),
        WithOffset(MethodSymbolsBuilder.Build([(1, "ab")]), 1),
        WithOffset(MethodSymbolsBuilder.Build([(1, "ab")]), -1),
        MethodSymbolsBuilder.Build([(1, "a"), (1, "b")]),
        MethodSymbolsBuilder.Build([(0, "a")]),
        MethodSymbolsBuilder.Build([(-3, "a")]),
        MethodSymbolsBuilder.Build([((1 << 26) + 1, "a")]),
    };

    [Theory]
    [MemberData(nameof(NotMethodSymbols))]
    public void RejectsWhatIsNotAWholeMethodSymbolsSidecar(byte[] bytes)
    {
        var exception = Assert.Throws<CompilerException>(() => MethodSymbolsFormat.Parse(bytes));
        Assert.Contains("method symbols", exception.Message, StringComparison.Ordinal);
    }

    private static byte[] WithTrailingByte(byte[] bytes) => [.. bytes, 0];

    private static byte[] Corrupt(byte[] bytes, int at)
    {
        bytes[at] ^= 0xff;
        return bytes;
    }

    // The first entry's character offset.
    private static byte[] WithOffset(byte[] bytes, int offset)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(84), offset);
        return bytes;
    }
}
