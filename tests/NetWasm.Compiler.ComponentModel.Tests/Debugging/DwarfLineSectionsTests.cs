// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Buffers.Binary;
using System.Text;
using NetWasm.Compiler.ComponentModel.Debugging;

namespace NetWasm.Compiler.ComponentModel.Tests.Debugging;

public sealed class DwarfLineSectionsTests
{
    private static readonly WasmDebugLines Lines = new(
        ["Program.cs", "sub/Other.cs"],
        5000,
        [
            new("Program.Main", 10, 400,
            [
                new(20, 1, 8),
                new(20 + 200, 1, 3),
                new(300, 2, 300),
                new(301, 2, 0),
            ]),
            new("Program.Other", 400, 410, [new(400, 1, 1)]),
        ],
        [],
        []);

    [Fact]
    public void ModuleWithoutLinesHasNoDwarf() =>
        Assert.Empty(DwarfLineSections.Encode(new([], 10, [], [(2, "Program.Main")], []), "/src"));

    [Fact]
    public void LineTableReplaysToTheRowsWithOneSequencePerFunction()
    {
        var sections = DwarfLineSections.Encode(Lines, "/src/app");

        Assert.Equal([".debug_abbrev", ".debug_info", ".debug_line"], sections.Select(section => section.Name));
        var (files, rows) = ReplayLineTable(sections[2].Payload);
        Assert.Equal(["Program.cs", "sub/Other.cs"], files);
        Assert.Equal(
            [
                (20, 1, 8, false), (220, 1, 3, false), (300, 2, 300, false), (301, 2, 0, false), (400, 2, 0, true),
                (400, 1, 1, false), (410, 1, 1, true),
            ],
            rows);
    }

    [Theory]
    [InlineData("/src/app", "/src/app")]
    [InlineData(null, ".")]
    [InlineData("", ".")]
    public void CompileUnitNamesTheFirstDocumentAndTheSourceDirectoryAndASubprogramPerFunction(string? directory,
        string compilationDirectory)
    {
        var sections = DwarfLineSections.Encode(Lines, directory);
        var information = sections[1].Payload;

        Assert.Equal(information.Length - 4, BinaryPrimitives.ReadInt32LittleEndian(information));
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(information.AsSpan(4)));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(information.AsSpan(6)));
        Assert.Equal(4, information[10]);
        var at = 11;
        Assert.Equal(1, information[at++]);
        Assert.Equal("NetWasm", Text(information, ref at));
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(information.AsSpan(at)));
        at += 2;
        Assert.Equal("Program.cs", Text(information, ref at));
        Assert.Equal(compilationDirectory, Text(information, ref at));
        Assert.Equal([0, 0, 5000], Words(information, ref at, 3));
        foreach (var (name, low, length) in new[] { ("Program.Main", 10, 390), ("Program.Other", 400, 10) })
        {
            Assert.Equal(2, information[at++]);
            Assert.Equal(name, Text(information, ref at));
            Assert.Equal([low, length], Words(information, ref at, 2));
        }
        Assert.Equal(0, information[at++]);
        Assert.Equal(information.Length, at);
        Assert.Equal(
            [
                1, 0x11, 1, 0x25, 0x08, 0x13, 0x05, 0x03, 0x08, 0x1b, 0x08, 0x10, 0x17, 0x11, 0x01, 0x12, 0x06, 0, 0,
                2, 0x2e, 0, 0x03, 0x08, 0x11, 0x01, 0x12, 0x06, 0x3f, 0x19, 0, 0, 0,
            ],
            sections[0].Payload);
    }

    [Fact]
    public void RejectsMissingLines() =>
        Assert.Throws<ArgumentNullException>(() => DwarfLineSections.Encode(null!, null));

    // A DWARF 4 line program's file names and rows (address, file, line, end of sequence), for the opcodes the encoder
    // writes.
    private static (List<string> Files, List<(int, int, int, bool)> Rows) ReplayLineTable(byte[] table)
    {
        Assert.Equal(table.Length - 4, BinaryPrimitives.ReadInt32LittleEndian(table));
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(4)));
        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(6));
        var at = 10;
        var programStart = at + headerLength;
        Assert.Equal([1, 1, 1, 0xfb, 14, 13, 0, 1, 1, 1, 1, 0, 0, 0, 1, 0, 0, 1, 0], table.AsSpan(at, 19).ToArray());
        at += 19;
        var files = new List<string>();
        while (table[at] != 0)
        {
            files.Add(Text(table, ref at));
            Assert.Equal([0, 0, 0], table.AsSpan(at, 3).ToArray());
            at += 3;
        }
        Assert.Equal(programStart, at + 1);
        at = programStart;
        var rows = new List<(int, int, int, bool)>();
        var (address, file, line) = (0, 1, 1);
        while (at < table.Length)
        {
            switch (table[at++])
            {
                case 0:
                    var length = Unsigned(table, ref at);
                    var opcode = table[at++];
                    if (opcode == 2)
                    {
                        Assert.Equal(5, length);
                        address = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(at));
                        at += 4;
                    }
                    else
                    {
                        Assert.Equal((1, 1), (length, opcode));
                        rows.Add((address, file, line, true));
                        (address, file, line) = (0, 1, 1);
                    }
                    break;
                case 1:
                    rows.Add((address, file, line, false));
                    break;
                case 2:
                    address += Unsigned(table, ref at);
                    break;
                case 3:
                    line += Signed(table, ref at);
                    break;
                case 4:
                    file = Unsigned(table, ref at);
                    break;
                default:
                    Assert.Fail($"unexpected opcode at {at - 1}");
                    break;
            }
        }
        return (files, rows);
    }

    private static string Text(byte[] bytes, ref int at)
    {
        var end = Array.IndexOf(bytes, (byte)0, at);
        var text = Encoding.UTF8.GetString(bytes, at, end - at);
        at = end + 1;
        return text;
    }

    private static int[] Words(byte[] bytes, ref int at, int count)
    {
        var result = new int[count];
        for (var index = 0; index < count; index++, at += 4)
            result[index] = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
        return result;
    }

    private static int Unsigned(byte[] bytes, ref int at)
    {
        var (value, shift) = (0, 0);
        byte next;
        do
        {
            next = bytes[at++];
            value |= (next & 0x7f) << shift;
            shift += 7;
        }
        while ((next & 0x80) != 0);
        return value;
    }

    private static int Signed(byte[] bytes, ref int at)
    {
        var (value, shift) = (0, 0);
        byte next;
        do
        {
            next = bytes[at++];
            value |= (next & 0x7f) << shift;
            shift += 7;
        }
        while ((next & 0x80) != 0);
        return (next & 0x40) != 0 ? value | (-1 << shift) : value;
    }
}
