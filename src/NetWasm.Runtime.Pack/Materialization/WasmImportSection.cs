// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;

namespace NetWasm.Runtime.Pack.Materialization;

// Reading a Wasm module's import section: its imports' module and field names and kinds, for a linked module or a
// relocatable object (whose undefined functions are imports).
internal static class WasmImportSection
{
    public const int HeaderLength = 8;
    private const byte ImportSectionId = 2;
    private static readonly byte[] Header = "\0asm\x01\0\0\0"u8.ToArray();
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static bool HasHeader(ReadOnlySpan<byte> module) =>
        module.Length >= HeaderLength && module[..HeaderLength].SequenceEqual(Header);

    // Each function import's module and name, in order.
    public static List<(string Module, string Name)> ReadFunctionImports(ReadOnlySpan<byte> module) =>
        ReadImports(module, FunctionImportKind);

    // Each tag import's module and name, in order.
    public static List<(string Module, string Name)> ReadTagImports(ReadOnlySpan<byte> module) =>
        ReadImports(module, TagImportKind);

    private const byte FunctionImportKind = 0;
    private const byte TagImportKind = 4;

    private static List<(string Module, string Name)> ReadImports(ReadOnlySpan<byte> module, byte wanted)
    {
        try
        {
            if (!HasHeader(module))
                throw Invalid();
            var imports = new List<(string Module, string Name)>();
            var offset = HeaderLength;
            while (offset < module.Length)
            {
                var id = module[offset++];
                var length = checked((int)ReadUnsigned(module, ref offset));
                var end = checked(offset + length);
                if (end > module.Length)
                    throw Invalid();
                if (id == ImportSectionId)
                {
                    var section = module[offset..end];
                    var position = 0;
                    var count = ReadUnsigned(section, ref position);
                    for (var index = 0u; index < count; index++)
                    {
                        var moduleName = ReadName(section, ref position);
                        var name = ReadName(section, ref position);
                        var kind = section[position];
                        position = SkipDescription(section, position + 1, kind);
                        if (kind == wanted)
                            imports.Add((moduleName, name));
                    }
                    if (position != section.Length)
                        throw Invalid();
                }
                offset = end;
            }
            return imports;
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IndexOutOfRangeException or
            DecoderFallbackException or OverflowException)
        {
            throw Invalid(exception);
        }
    }

    // The end of an import's description: a function's type, a table's element type and limits, a memory's limits, a
    // global's type and mutability, or a tag's attribute and type.
    public static int SkipDescription(ReadOnlySpan<byte> section, int offset, byte kind)
    {
        switch (kind)
        {
            case 0:
                ReadUnsigned(section, ref offset);
                return offset;
            case 1:
                offset = SkipReferenceType(section, offset);
                return SkipLimits(section, offset);
            case 2:
                return SkipLimits(section, offset);
            case 3:
                offset = SkipValueType(section, offset);
                return offset + 1;
            case 4:
                offset++;
                ReadUnsigned(section, ref offset);
                return offset;
            default:
                throw Invalid();
        }
    }

    private static int SkipReferenceType(ReadOnlySpan<byte> section, int offset)
    {
        var type = section[offset++];
        // (ref null? heap type) carries a heap type after its prefix.
        if (type is 0x63 or 0x64)
            ReadSigned(section, ref offset);
        return offset;
    }

    private static int SkipValueType(ReadOnlySpan<byte> section, int offset) =>
        section[offset] is 0x63 or 0x64 ? SkipReferenceType(section, offset) : offset + 1;

    // Flags, minimum, an optional maximum, and with custom page sizes a page-size exponent.
    private static int SkipLimits(ReadOnlySpan<byte> section, int offset)
    {
        var flags = section[offset++];
        ReadUnsigned64(section, ref offset);
        if ((flags & 0x01) != 0)
            ReadUnsigned64(section, ref offset);
        if ((flags & 0x08) != 0)
            ReadUnsigned(section, ref offset);
        return offset;
    }

    public static string ReadName(ReadOnlySpan<byte> source, ref int offset)
    {
        var length = checked((int)ReadUnsigned(source, ref offset));
        var name = Utf8.GetString(source.Slice(offset, length));
        offset += length;
        return name;
    }

    public static uint ReadUnsigned(ReadOnlySpan<byte> source, ref int offset) =>
        checked((uint)ReadUnsigned64(source, ref offset));

    public static ulong ReadUnsigned64(ReadOnlySpan<byte> source, ref int offset)
    {
        ulong result = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            var value = source[offset++];
            result |= (ulong)(value & 0x7f) << shift;
            if ((value & 0x80) == 0)
                return result;
        }
        throw Invalid();
    }

    private static void ReadSigned(ReadOnlySpan<byte> source, ref int offset)
    {
        for (var shift = 0; shift < 64; shift += 7)
        {
            if ((source[offset++] & 0x80) == 0)
                return;
        }
        throw Invalid();
    }

    public static InvalidOperationException Invalid(Exception? cause = null) =>
        new("The linked runtime module's import section is invalid.", cause);
}
