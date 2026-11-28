// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using NetWasm.Compiler.ComponentModel.Merging;

namespace NetWasm.Compiler.ComponentModel.Debugging;

// A custom section to add to a module.
internal sealed record WasmCustomSection(string Name, byte[] Payload);

internal static class WasmCustomSections
{
    private const byte CustomSectionId = 0;
    private const byte CodeSectionId = 10;

    // The module without the custom sections whose names the predicate removes, then the given sections. Removing
    // and appending custom sections moves no code: DWARF addresses count from the start of the code section.
    public static byte[] Replace(byte[] module, Func<string, bool> removed, IReadOnlyList<WasmCustomSection> sections)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(sections);
        var output = new WasmMergeSink(module.Length + 1024);
        output.Write(Header(module));
        var at = WasmBinaryFormat.Header.Length;
        while (at < module.Length)
        {
            var start = at;
            var (id, payload, end) = Section(module, ref at);
            if (id == CustomSectionId && removed(WasmMergeRead.Name(module, ref payload))) continue;
            output.Write(module.AsSpan(start, end - start));
        }
        var section = new WasmMergeSink();
        foreach (var custom in sections)
        {
            section.Clear();
            section.Name(custom.Name);
            section.Write(custom.Payload);
            output.Section(CustomSectionId, section);
        }
        return output.ToArray();
    }

    // Where the code section's contents start and how long they are; a module without code has none.
    public static (int Start, int Length) Code(byte[] module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var at = Header(module).Length;
        while (at < module.Length)
        {
            var (id, payload, end) = Section(module, ref at);
            if (id == CodeSectionId) return (payload, end - payload);
        }
        return (at, 0);
    }

    private static ReadOnlySpan<byte> Header(byte[] module) =>
        module.AsSpan().StartsWith(WasmBinaryFormat.Header)
            ? WasmBinaryFormat.Header
            : throw WasmMergeRead.Invalid("debug information needs a WebAssembly core module");

    private static (byte Id, int Payload, int End) Section(byte[] module, ref int at)
    {
        var id = WasmMergeRead.Byte(module, ref at);
        var size = WasmMergeRead.Unsigned(module, ref at);
        var payload = at;
        WasmMergeRead.Bytes(module, ref at, size);
        return (id, payload, at);
    }
}
