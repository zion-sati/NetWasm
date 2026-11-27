// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using NetWasm.Compiler.ComponentModel.Merging;

namespace NetWasm.Compiler.ComponentModel.Debugging;

// Finds the source lines of a linked Debug module. With managed stack traces on, the compiler emits
// `i32.const <method ID>; i32.const <symbol ID>; call <stack trace frame location>` before each CIL instruction; the
// symbol ID names a source line of the method, or is the method's own ID at a hidden sequence point. The callee is
// the function most markers call, so the scan needs no names. A row starts at each marker's call (a constant may emit
// no machine code of its own), and a line-0 row starts after each block end and each unconditional transfer, so code
// the compiler generates at a join (a rethrow, a spill) does not take the line of the statement before it.
// The first pass reads every body for its markers; only the bodies with source lines are read again for their joins.
// A document named by a URL (a library's Source Link, as CoreLib's frames are) is no file a debugger can open, and
// DWARF would read it as a path under the compilation directory: its code keeps its name but has no lines.
internal static class WasmDebugLineScanner
{
    private const byte Unreachable = 0x00;
    private const byte Else = 0x05;
    private const byte Throw = 0x08;
    private const byte Rethrow = 0x09;
    private const byte ThrowReference = 0x0a;
    private const byte End = 0x0b;
    private const byte Branch = 0x0c;
    private const byte BranchTable = 0x0e;
    private const byte Return = 0x0f;
    private const byte Call = 0x10;
    private const byte ReturnCall = 0x12;
    private const byte ReturnCallIndirect = 0x13;
    private const byte I32Constant = 0x41;
    private const string LocationSeparator = " in ";
    private const string LineSeparator = ":line ";

    private readonly record struct Marker(int Body, int Offset, int Method, int Symbol, uint Callee);

    // Each location symbol's document and line once read, by symbol ID, for the method it was read for (0: unread).
    private sealed record LocationTable(int[] Owners, string?[] Documents, int[] Lines, bool[] Remote);

    public static WasmDebugLines Scan(byte[] module, string?[] symbols)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(symbols);
        var code = WasmCustomSections.Code(module);
        var parsed = WasmMergeModule.Parse(new WasmMergeInput("linked module", module));
        var bodies = parsed.Bodies;
        var imported = (uint)parsed.ImportsOfKind[WasmMergeModule.FunctionKind].Count;
        var locations = new LocationTable(new int[symbols.Length], new string?[symbols.Length], new int[symbols.Length],
            new bool[symbols.Length]);
        var markers = Markers(module, bodies, symbols, locations);
        var votes = new Dictionary<uint, int>();
        foreach (var marker in markers) CollectionsMarshal.GetValueRefOrAddDefault(votes, marker.Callee, out _)++;
        var files = new List<string>();
        var fileIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var functions = ImmutableArray.CreateBuilder<WasmDebugFunction>();
        var names = ImmutableArray.CreateBuilder<(uint, string)>();
        var callee = Winner(votes);
        var called = new List<Marker>();
        for (var at = 0; at < markers.Count;)
        {
            var body = markers[at].Body;
            called.Clear();
            for (; at < markers.Count && markers[at].Body == body; at++)
            {
                if (markers[at].Callee == callee) called.Add(markers[at]);
            }
            if (called.Count == 0) continue;
            var name = ManagedName(symbols[called[0].Method]!);
            names.Add((imported + (uint)body, name));
            if (called.Exists(marker => marker.Symbol != marker.Method && !locations.Remote[marker.Symbol]))
            {
                functions.Add(Function(name, module, bodies[body], called, code.Start, symbols, locations, files,
                    fileIndices));
            }
        }
        ImmutableArray<(uint, string)> existing = parsed.NameSection is { } section
            ? [.. WasmFunctionNames.Read(module, section)]
            : [];
        return new([.. files], code.Length, functions.ToImmutable(), names.ToImmutable(), existing);
    }

    // The callee most markers call (the first one met on a tie, in module order); none without markers.
    private static uint? Winner(Dictionary<uint, int> votes)
    {
        uint? winner = null;
        var most = 0;
        foreach (var (callee, count) in votes)
        {
            if (count > most) (winner, most) = (callee, count);
        }
        return winner;
    }

    // Every body's markers whose IDs name a method and one of its lines (or the method itself), in body and offset order.
    private static List<Marker> Markers(byte[] module, List<WasmMergeRange> bodies, string?[] symbols,
        LocationTable locations)
    {
        var markers = new List<Marker>();
        var body = 0;
        var (first, second) = ((Offset: -1, Opcode: -1), (Offset: -1, Opcode: -1));
        Action<int, byte> visit = (offset, opcode) =>
        {
            if (opcode == Call && first.Opcode == I32Constant && second.Opcode == I32Constant)
            {
                var at = first.Offset + 1;
                var method = Signed(module, ref at);
                at = second.Offset + 1;
                var symbol = Signed(module, ref at);
                at = offset + 1;
                var callee = WasmMergeRead.Unsigned(module, ref at);
                if ((uint)method < (uint)symbols.Length && symbols[method] is not null &&
                    (symbol == method || Location(method, symbol, symbols, locations) is not null))
                {
                    markers.Add(new(body, offset, method, symbol, callee));
                }
            }
            (first, second) = (second, (offset, opcode));
        };
        for (; body < bodies.Count; body++)
        {
            (first, second) = ((-1, -1), (-1, -1));
            WasmExpressionRemapper.Walk(module, bodies[body].Start, bodies[body].Length, visit);
        }
        return markers;
    }

    // The offsets that follow a block end or an unconditional transfer, in order.
    private static List<int> Joins(byte[] module, WasmMergeRange body)
    {
        var joins = new List<int>();
        var transferred = false;
        WasmExpressionRemapper.Walk(module, body.Start, body.Length, (offset, opcode) =>
        {
            if (transferred) joins.Add(offset);
            transferred = opcode is Unreachable or Else or Throw or Rethrow or ThrowReference or End or Branch or
                BranchTable or Return or ReturnCall or ReturnCallIndirect;
        });
        return joins;
    }

    // A managed method's name as a debugger and a backtrace show it: "Program.Main" for "Program::Main".
    private static string ManagedName(string symbol) => symbol.Replace("::", ".", StringComparison.Ordinal);

    // The rows of a body with source lines: its markers and its joins, which both come in offset order, merged.
    private static WasmDebugFunction Function(string name, byte[] module, WasmMergeRange body, List<Marker> markers,
        int code, string?[] symbols, LocationTable locations, List<string> files, Dictionary<string, int> fileIndices)
    {
        var joins = Joins(module, body);
        var rows = ImmutableArray.CreateBuilder<WasmDebugRow>();
        var (file, line) = (0, -1);
        var (nextMarker, nextJoin) = (0, 0);
        while (nextMarker < markers.Count || nextJoin < joins.Count)
        {
            var takeMarker = nextJoin == joins.Count ||
                (nextMarker < markers.Count && markers[nextMarker].Offset <= joins[nextJoin]);
            var offset = takeMarker ? markers[nextMarker].Offset : joins[nextJoin];
            var next = (File: file, Line: 0);
            if (takeMarker && markers[nextMarker] is var found && found.Symbol != found.Method)
            {
                var (document, number) = Location(found.Method, found.Symbol, symbols, locations)!.Value;
                // Code with no file a debugger can open stays on line 0, as at a join.
                if (!locations.Remote[found.Symbol])
                {
                    ref var index = ref CollectionsMarshal.GetValueRefOrAddDefault(fileIndices, document, out var known);
                    if (!known)
                    {
                        files.Add(document);
                        index = files.Count;
                    }
                    next = (index, number);
                }
            }
            if (takeMarker) nextMarker++;
            else nextJoin++;
            // Before its first source line a function has no file for a line-0 row to name.
            if (next.File == 0 || next == (file, line)) continue;
            (file, line) = next;
            rows.Add(new(offset - code, file, line));
        }
        return new(name, body.Start - code, body.Start + body.Length - code, rows.ToImmutable());
    }

    // The document and line a location symbol names: the method's name, " in ", the document, ":line " and the line.
    private static (string Document, int Line)? Location(int method, int symbol, string?[] symbols, LocationTable table)
    {
        if ((uint)symbol >= (uint)symbols.Length) return null;
        if (table.Owners[symbol] == method)
            return table.Documents[symbol] is { } read ? (read, table.Lines[symbol]) : null;
        table.Owners[symbol] = method;
        table.Documents[symbol] = null;
        var name = symbols[method]!;
        if (symbols[symbol] is { } text &&
            text.Length > name.Length + LocationSeparator.Length &&
            text.StartsWith(name, StringComparison.Ordinal) &&
            string.CompareOrdinal(text, name.Length, LocationSeparator, 0, LocationSeparator.Length) == 0)
        {
            var rest = text.AsSpan(name.Length + LocationSeparator.Length);
            var separator = rest.LastIndexOf(LineSeparator, StringComparison.Ordinal);
            if (separator > 0 &&
                int.TryParse(rest[(separator + LineSeparator.Length)..], NumberStyles.None, CultureInfo.InvariantCulture,
                    out var number) &&
                number > 0)
            {
                table.Documents[symbol] = rest[..separator].ToString();
                table.Lines[symbol] = number;
                table.Remote[symbol] = IsUrl(table.Documents[symbol]!);
                return (table.Documents[symbol]!, number);
            }
        }
        return null;
    }

    private static bool IsUrl(string document) =>
        document.Contains("://", StringComparison.Ordinal) &&
        Uri.TryCreate(document, UriKind.Absolute, out var uri) && !uri.IsFile;

    private static int Signed(byte[] module, ref int at)
    {
        var value = 0;
        var shift = 0;
        byte next;
        do
        {
            next = WasmMergeRead.Byte(module, ref at);
            value |= (next & 0x7f) << shift;
            shift += 7;
        }
        while ((next & 0x80) != 0 && shift < 35);
        return shift < 32 && (next & 0x40) != 0 ? value | (-1 << shift) : value;
    }
}
