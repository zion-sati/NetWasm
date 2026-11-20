// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Layout;

// The program assembly table as CoreLib's RuntimeTypeTable reads it: a count and presence flags, an entry per
// assembly index, an entry per resource, then the names and each resource's bytes at a multiple of eight.
public sealed class ProgramAssemblyTableEncoderTests
{
    [Fact]
    public void EveryNameAndResourceReadsBackAtItsOffset()
    {
        ProgramAssembly[] assemblies =
        [
            new("App, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                [new("App.greeting.txt", Encoding.UTF8.GetBytes("hello")), new("!Assets", new byte[] { 1, 2, 3 })]),
            new("Lib, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null", []),
            new(null, [new("Only.bin", new byte[] { 9 })]),
        ];

        var table = ProgramAssemblyTableEncoder.Encode(assemblies).ToArray();

        Assert.Equal(3u, U32(table, 0));
        Assert.Equal(3u, U32(table, 4));
        Assert.Equal(assemblies[0].DisplayName, Text(table, 8));
        Assert.Equal(assemblies[1].DisplayName, Text(table, 24));
        Assert.Equal(0u, U32(table, 40 + 4));
        Assert.Equal((0u, 2u), (U32(table, 8 + 8), U32(table, 8 + 12)));
        Assert.Equal((2u, 0u), (U32(table, 24 + 8), U32(table, 24 + 12)));
        Assert.Equal((2u, 1u), (U32(table, 40 + 8), U32(table, 40 + 12)));
        var resources = 8 + 3 * 16;
        string[] names = ["App.greeting.txt", "!Assets", "Only.bin"];
        byte[][] data = [Encoding.UTF8.GetBytes("hello"), [1, 2, 3], [9]];
        for (var index = 0; index < 3; index++)
        {
            var entry = resources + index * 16;
            Assert.Equal(names[index], Text(table, entry));
            var offset = (int)U32(table, entry + 8);
            Assert.Equal(0, offset % ProgramAssemblyTableEncoder.Alignment);
            Assert.Equal(data[index], table.AsSpan(offset, (int)U32(table, entry + 12)).ToArray());
        }
        Assert.Equal(0, table.Length % ProgramAssemblyTableEncoder.Alignment);
    }

    [Fact]
    public void NamesAloneSetOnlyTheNamesFlag()
    {
        var table = ProgramAssemblyTableEncoder.Encode([new("App", ImmutableArray<ManifestResourceData>.Empty)]).ToArray();

        Assert.Equal(1u, U32(table, 4));
        Assert.Equal("App", Text(table, 8));
    }

    private static uint U32(byte[] table, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(offset));

    private static string Text(byte[] table, int entry) =>
        Encoding.UTF8.GetString(table, (int)U32(table, entry), (int)U32(table, entry + 4));
}
