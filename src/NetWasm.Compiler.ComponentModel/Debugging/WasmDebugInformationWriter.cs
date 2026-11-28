// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Merging;

namespace NetWasm.Compiler.ComponentModel.Debugging;

/// <summary>
/// Gives a linked Debug module what a debugger reads (LLDB under Wasmtime, Chrome's DWARF extension) and what names
/// Wasmtime's trap backtraces: a name section, and DWARF with a compile unit, a subprogram per managed method with
/// source lines and a line table. Both come from the markers the compiler emits before each CIL instruction with
/// managed stack traces on, which survive merging and an O0 optimizer, and from the method symbols, which hold the
/// managed names a stack-trace build keeps out of the module. Names the module already has are kept, and earlier
/// DWARF is replaced.
/// </summary>
public sealed class WasmDebugInformationWriter(IByteFileReader reader, IByteFileWriter writer) : IWasmDebugInformationWriter
{
    private const string NameSection = "name";
    private const string DwarfSectionPrefix = ".debug_";
    private const byte FunctionNamesSubsection = 1;

    private readonly IByteFileReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    private readonly IByteFileWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    public void Write(string modulePath, WasmDebugInformation debugInformation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        ArgumentNullException.ThrowIfNull(debugInformation);
        if (string.IsNullOrEmpty(debugInformation.MethodSymbolsPath)) return;
        var module = _reader.Read(modulePath);
        var symbols = MethodSymbolsFormat.Parse(_reader.Read(debugInformation.MethodSymbolsPath));
        var lines = WasmDebugLineScanner.Scan(module, symbols);
        var names = new SortedDictionary<uint, string>(
            lines.ExistingNames.GroupBy(name => name.Function).ToDictionary(group => group.Key, group => group.First().Name));
        foreach (var (function, name) in lines.Names) names.TryAdd(function, name);
        List<WasmCustomSection> sections = [];
        if (names.Count != 0) sections.Add(new(NameSection, FunctionNames(names)));
        sections.AddRange(DwarfLineSections.Encode(lines, debugInformation.SourceDirectory));
        _writer.Write(
            modulePath,
            WasmCustomSections.Replace(
                module,
                name => name == NameSection || name.StartsWith(DwarfSectionPrefix, StringComparison.Ordinal),
                sections));
    }

    // A name section of function names alone, as the merger writes one.
    private static byte[] FunctionNames(SortedDictionary<uint, string> names)
    {
        var entries = new WasmMergeSink();
        entries.Unsigned((uint)names.Count);
        foreach (var (function, name) in names)
        {
            entries.Unsigned(function);
            entries.Name(name);
        }
        var payload = new WasmMergeSink();
        payload.Byte(FunctionNamesSubsection);
        payload.Unsigned((uint)entries.Length);
        payload.Write(entries.Written);
        return payload.ToArray();
    }
}
