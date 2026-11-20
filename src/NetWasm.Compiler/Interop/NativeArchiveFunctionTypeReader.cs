// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Interop;

// Reads a native archive (ar, GNU or BSD names) of Wasm object files or, from an LTO build, LLVM bitcode modules (see
// LlvmBitcodeFunctionTypeReader). A Wasm object's types, function imports and definitions, and its linking section's
// symbol table name the functions it defines: a function symbol that is defined and not local can be linked to from
// another object, so its type is the one a call must use. Other members (the archive's symbol and name tables) define
// nothing.
internal sealed class NativeArchiveFunctionTypeReader : INativeArchiveFunctionTypeReader
{
    private const int SymbolTable = 8;
    private const uint Undefined = 0x10;
    private const uint Local = 0x02;

    public ImmutableDictionary<string, NativeFunctionType> Read(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        byte[] bytes;
        try { bytes = File.ReadAllBytes(archivePath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Invalid(archivePath, "could not be read", exception);
        }
        if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual("!<arch>\n"u8))
            throw Invalid(archivePath, "is not an ar archive");
        var functions = ImmutableDictionary.CreateBuilder<string, NativeFunctionType>(StringComparer.Ordinal);
        var position = 8;
        while (position + 60 <= bytes.Length)
        {
            var header = bytes.AsSpan(position, 60);
            if (!header[58..60].SequenceEqual("`\n"u8)) throw Invalid(archivePath, "has a malformed member header");
            var name = Encoding.ASCII.GetString(header[..16]).TrimEnd();
            if (!long.TryParse(Encoding.ASCII.GetString(header[48..58]).Trim(), out var size) || size < 0 ||
                position + 60 + size > bytes.Length)
                throw Invalid(archivePath, "has a member larger than the archive");
            var start = position + 60;
            var length = (int)size;
            // A BSD name of #1/N is stored in the member's first N bytes.
            if (name.StartsWith("#1/", StringComparison.Ordinal) && int.TryParse(name[3..], out var nameLength) &&
                nameLength >= 0 && nameLength <= length)
            {
                start += nameLength;
                length -= nameLength;
            }
            if (length >= 8 && (bytes.AsSpan(start, 4).SequenceEqual("\0asm"u8) ||
                LlvmBitcodeFunctionTypeReader.IsBitcode(bytes.AsSpan(start, length))))
            {
                try
                {
                    if (bytes[start] == 0) ReadObject(bytes.AsSpan(start, length), functions);
                    else LlvmBitcodeFunctionTypeReader.Read(bytes.AsSpan(start, length), functions);
                }
                catch (Exception exception) when (exception is FormatException or IndexOutOfRangeException or
                    ArgumentOutOfRangeException or OverflowException)
                {
                    throw Invalid(archivePath, $"has a Wasm object ('{name}') that cannot be read", exception);
                }
            }
            position += 60 + (int)size + (int)(size & 1);
        }
        return functions.ToImmutable();
    }

    private static void ReadObject(ReadOnlySpan<byte> module, ImmutableDictionary<string, NativeFunctionType>.Builder functions)
    {
        var types = new List<NativeFunctionType>();
        var definedTypes = new List<int>();
        var importedFunctions = 0;
        var position = 8;
        while (position < module.Length)
        {
            var id = module[position++];
            var length = (int)ReadUnsigned(module, ref position);
            var section = module.Slice(position, length);
            position += length;
            var cursor = 0;
            switch (id)
            {
                case 1:
                    for (var count = ReadUnsigned(section, ref cursor); count > 0; count--)
                        types.Add(ReadFunctionType(section, ref cursor));
                    break;
                case 2:
                    for (var count = ReadUnsigned(section, ref cursor); count > 0; count--)
                        if (SkipImport(section, ref cursor)) importedFunctions++;
                    break;
                case 3:
                    for (var count = ReadUnsigned(section, ref cursor); count > 0; count--)
                        definedTypes.Add((int)ReadUnsigned(section, ref cursor));
                    break;
                case 0 when ReadName(section, ref cursor) == "linking":
                    ReadLinking(section[cursor..], types, definedTypes, importedFunctions, functions);
                    break;
            }
        }
    }

    // The symbol table's defined, non-local functions, by name.
    private static void ReadLinking(ReadOnlySpan<byte> linking, List<NativeFunctionType> types, List<int> definedTypes,
        int importedFunctions, ImmutableDictionary<string, NativeFunctionType>.Builder functions)
    {
        var position = 0;
        ReadUnsigned(linking, ref position); // version
        while (position < linking.Length)
        {
            var kind = linking[position++];
            var length = (int)ReadUnsigned(linking, ref position);
            var subsection = linking.Slice(position, length);
            position += length;
            if (kind != SymbolTable) continue;
            var cursor = 0;
            for (var count = ReadUnsigned(subsection, ref cursor); count > 0; count--)
            {
                var symbol = subsection[cursor++];
                var flags = ReadUnsigned(subsection, ref cursor);
                var defined = (flags & Undefined) == 0;
                switch (symbol)
                {
                    case 0 or 2 or 4 or 5: // function, global, tag, table
                        var index = (int)ReadUnsigned(subsection, ref cursor);
                        string? name = null;
                        if (defined || (flags & 0x40) != 0) name = ReadName(subsection, ref cursor);
                        if (symbol == 0 && defined && (flags & Local) == 0 && name is not null &&
                            index >= importedFunctions && index - importedFunctions < definedTypes.Count)
                            functions[name] = types[definedTypes[index - importedFunctions]];
                        break;
                    case 1: // data
                        ReadName(subsection, ref cursor);
                        if (defined)
                        {
                            ReadUnsigned(subsection, ref cursor);
                            ReadUnsigned(subsection, ref cursor);
                            ReadUnsigned(subsection, ref cursor);
                        }
                        break;
                    case 3: // section
                        ReadUnsigned(subsection, ref cursor);
                        break;
                    default:
                        throw new FormatException("unknown symbol kind");
                }
            }
        }
    }

    private static NativeFunctionType ReadFunctionType(ReadOnlySpan<byte> section, ref int position)
    {
        if (section[position++] != 0x60) throw new FormatException("not a function type");
        var parameters = ImmutableArray.CreateBuilder<CliValueKind>();
        for (var count = ReadUnsigned(section, ref position); count > 0; count--)
            parameters.Add(ValueKind(section[position++]));
        var results = ReadUnsigned(section, ref position);
        var result = CliValueKind.Void;
        for (var index = 0UL; index < results; index++)
            result = ValueKind(section[position++]);
        return new(parameters.ToImmutable(), results > 1 ? CliValueKind.Unknown : result);
    }

    // Whether the import is a function's.
    private static bool SkipImport(ReadOnlySpan<byte> section, ref int position)
    {
        ReadName(section, ref position);
        ReadName(section, ref position);
        var kind = section[position++];
        switch (kind)
        {
            case 0:
                ReadUnsigned(section, ref position);
                return true;
            case 1:
                position++;
                SkipLimits(section, ref position);
                break;
            case 2:
                SkipLimits(section, ref position);
                break;
            case 3:
                position += 2;
                break;
            case 4:
                position++;
                ReadUnsigned(section, ref position);
                break;
            default:
                throw new FormatException("unknown import kind");
        }
        return false;
    }

    private static void SkipLimits(ReadOnlySpan<byte> section, ref int position)
    {
        var flags = section[position++];
        ReadUnsigned(section, ref position);
        if ((flags & 1) != 0) ReadUnsigned(section, ref position);
    }

    private static CliValueKind ValueKind(byte type) => type switch
    {
        0x7f => CliValueKind.I4,
        0x7e => CliValueKind.I8,
        0x7d => CliValueKind.F4,
        0x7c => CliValueKind.F8,
        _ => CliValueKind.Unknown,
    };

    private static string ReadName(ReadOnlySpan<byte> bytes, ref int position)
    {
        var length = (int)ReadUnsigned(bytes, ref position);
        var name = Encoding.UTF8.GetString(bytes.Slice(position, length));
        position += length;
        return name;
    }

    private static ulong ReadUnsigned(ReadOnlySpan<byte> bytes, ref int position)
    {
        ulong value = 0;
        for (var shift = 0; ; shift += 7)
        {
            if (shift > 63) throw new FormatException("LEB128 value too long");
            var current = bytes[position++];
            value |= (ulong)(current & 0x7f) << shift;
            if ((current & 0x80) == 0) return value;
        }
    }

    private static CompilerException Invalid(string path, string problem, Exception? inner = null) => new(
        new CompilerDiagnostic(DiagnosticCode.NativeInterop,
            $"The native archive '{Path.GetFileName(path)}' {problem}.", Path.GetFileName(path)), inner);
}
