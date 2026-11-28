// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Text;

namespace NetWasm.Compiler.ComponentModel.Debugging;

// Encodes a module's source lines as DWARF 4 with 32-bit addresses: .debug_abbrev, .debug_info with one compile unit
// and a subprogram per function, and .debug_line with one sequence per function. The compile unit says C++, which
// LLDB knows; it has no C# support, and C++ is the closest language whose frames and lines it shows unchanged.
internal static class DwarfLineSections
{
    private const ushort DwarfVersion = 4;
    private const byte AddressSize = 4;
    private const string Producer = "NetWasm";
    private const ushort LanguageCPlusPlus = 0x0004;
    private const byte CompileUnitAbbreviation = 1;
    private const byte SubprogramAbbreviation = 2;
    private const sbyte LineBase = -5;
    private const byte LineRange = 14;
    private const byte OpcodeBase = 13;

    // The operand counts of the standard opcodes 1 to 12.
    private static ReadOnlySpan<byte> StandardOpcodeLengths => [0, 1, 1, 1, 1, 0, 0, 0, 1, 0, 0, 1];

    public static IReadOnlyList<WasmCustomSection> Encode(WasmDebugLines lines, string? sourceDirectory)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Functions.IsEmpty) return [];
        return
        [
            new(".debug_abbrev", Abbreviations()),
            new(".debug_info", Information(lines, sourceDirectory)),
            new(".debug_line", LineTable(lines)),
        ];
    }

    private static byte[] Abbreviations()
    {
        var output = new List<byte>();
        Unsigned(output, CompileUnitAbbreviation);
        Unsigned(output, 0x11); // DW_TAG_compile_unit
        output.Add(1); // DW_CHILDREN_yes
        Attributes(output,
            (0x25, 0x08), // DW_AT_producer, DW_FORM_string
            (0x13, 0x05), // DW_AT_language, DW_FORM_data2
            (0x03, 0x08), // DW_AT_name, DW_FORM_string
            (0x1b, 0x08), // DW_AT_comp_dir, DW_FORM_string
            (0x10, 0x17), // DW_AT_stmt_list, DW_FORM_sec_offset
            (0x11, 0x01), // DW_AT_low_pc, DW_FORM_addr
            (0x12, 0x06)); // DW_AT_high_pc, DW_FORM_data4 (a length)
        Unsigned(output, SubprogramAbbreviation);
        Unsigned(output, 0x2e); // DW_TAG_subprogram
        output.Add(0); // DW_CHILDREN_no
        Attributes(output,
            (0x03, 0x08), // DW_AT_name, DW_FORM_string
            (0x11, 0x01), // DW_AT_low_pc, DW_FORM_addr
            (0x12, 0x06), // DW_AT_high_pc, DW_FORM_data4
            (0x3f, 0x19)); // DW_AT_external, DW_FORM_flag_present
        output.Add(0);
        return [.. output];
    }

    private static void Attributes(List<byte> output, params ReadOnlySpan<(int Attribute, int Form)> attributes)
    {
        foreach (var (attribute, form) in attributes)
        {
            Unsigned(output, (uint)attribute);
            Unsigned(output, (uint)form);
        }
        output.Add(0);
        output.Add(0);
    }

    private static byte[] Information(WasmDebugLines lines, string? sourceDirectory)
    {
        var entries = new List<byte>();
        Unsigned(entries, CompileUnitAbbreviation);
        Text(entries, Producer);
        UInt16(entries, LanguageCPlusPlus);
        Text(entries, lines.Files[0]);
        Text(entries, string.IsNullOrEmpty(sourceDirectory) ? "." : sourceDirectory);
        UInt32(entries, 0); // the line table's offset in .debug_line
        UInt32(entries, 0);
        UInt32(entries, (uint)lines.CodeSize);
        foreach (var function in lines.Functions)
        {
            Unsigned(entries, SubprogramAbbreviation);
            Text(entries, function.Name);
            UInt32(entries, (uint)function.Low);
            UInt32(entries, (uint)(function.High - function.Low));
        }
        entries.Add(0); // the compile unit's children end
        var unit = new List<byte>();
        UInt16(unit, DwarfVersion);
        UInt32(unit, 0); // the abbreviations' offset in .debug_abbrev
        unit.Add(AddressSize);
        unit.AddRange(entries);
        return WithLength(unit);
    }

    private static byte[] LineTable(WasmDebugLines lines)
    {
        var header = new List<byte> { 1, 1, 1, unchecked((byte)LineBase), LineRange, OpcodeBase };
        header.AddRange(StandardOpcodeLengths);
        header.Add(0); // no include directories: a document is absolute or relative to the compilation directory
        foreach (var file in lines.Files)
        {
            Text(header, file);
            header.AddRange([0, 0, 0]); // the compilation directory, no modification time, no length
        }
        header.Add(0);
        var program = new List<byte>();
        foreach (var function in lines.Functions)
        {
            // One DW_LNE_set_address per sequence, then DW_LNS_advance_pc: gimli, and so Wasmtime, starts a sequence at
            // every set_address.
            var address = function.Rows[0].Address;
            program.AddRange([0, 5, 2]);
            UInt32(program, (uint)address);
            var (file, line) = (1, 1);
            foreach (var row in function.Rows)
            {
                if (row.Address != address)
                {
                    program.Add(2); // DW_LNS_advance_pc
                    Unsigned(program, (uint)(row.Address - address));
                    address = row.Address;
                }
                if (row.File != file)
                {
                    program.Add(4); // DW_LNS_set_file
                    Unsigned(program, (uint)row.File);
                    file = row.File;
                }
                if (row.Line != line)
                {
                    program.Add(3); // DW_LNS_advance_line
                    Signed(program, row.Line - line);
                    line = row.Line;
                }
                program.Add(1); // DW_LNS_copy
            }
            program.Add(2);
            Unsigned(program, (uint)(function.High - address));
            program.AddRange([0, 1, 1]); // DW_LNE_end_sequence
        }
        var unit = new List<byte>();
        UInt16(unit, DwarfVersion);
        UInt32(unit, (uint)header.Count);
        unit.AddRange(header);
        unit.AddRange(program);
        return WithLength(unit);
    }

    private static byte[] WithLength(List<byte> unit)
    {
        var output = new List<byte>(unit.Count + 4);
        UInt32(output, (uint)unit.Count);
        output.AddRange(unit);
        return [.. output];
    }

    private static void Text(List<byte> output, string value)
    {
        output.AddRange(Encoding.UTF8.GetBytes(value));
        output.Add(0);
    }

    private static void UInt16(List<byte> output, ushort value)
    {
        output.Add((byte)value);
        output.Add((byte)(value >> 8));
    }

    private static void UInt32(List<byte> output, uint value)
    {
        for (var shift = 0; shift < 32; shift += 8) output.Add((byte)(value >> shift));
    }

    private static void Unsigned(List<byte> output, uint value)
    {
        do
        {
            var next = (byte)(value & 0x7f);
            value >>= 7;
            output.Add(value == 0 ? next : (byte)(next | 0x80));
        }
        while (value != 0);
    }

    private static void Signed(List<byte> output, int value)
    {
        while (true)
        {
            var next = (byte)(value & 0x7f);
            value >>= 7;
            if ((value == 0 && (next & 0x40) == 0) || (value == -1 && (next & 0x40) != 0))
            {
                output.Add(next);
                return;
            }
            output.Add((byte)(next | 0x80));
        }
    }
}
