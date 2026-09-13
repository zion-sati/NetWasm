using System;
using System.Collections.Generic;
using NetWasm.Compiler;

namespace NetWasm.Compiler.Validation;

internal sealed class WasmEmissionInvariantValidator(
    ICompilerInvariantExceptionFactory exceptions) : IWasmEmissionInvariantValidator
{
    private static ReadOnlySpan<byte> Header => [0, 0x61, 0x73, 0x6d, 1, 0, 0, 0];

    public void Validate(ReadOnlySpan<byte> bytes, Core.WasmTarget target)
    {
        _ = target;
        if (bytes.Length < Header.Length || !bytes[..Header.Length].SequenceEqual(Header))
        {
            throw exceptions.Create(
                "emitted module has an invalid WebAssembly header");
        }
        var offset = Header.Length;
        var previousSectionOrder = 0;
        var seen = new HashSet<int>();
        while (offset < bytes.Length)
        {
            var section = bytes[offset++];
            var size = ReadUnsigned(bytes, ref offset);
            if (size > int.MaxValue || offset > bytes.Length - (int)size)
            {
                throw exceptions.Create(
                    "emitted module section exceeds the serialized module");
            }
            if (section != 0)
            {
                var sectionOrder = GetSectionOrder(section);
                if (sectionOrder < previousSectionOrder || !seen.Add(section))
                {
                    throw exceptions.Create(
                        "emitted module sections are duplicated or out of order");
                }
                previousSectionOrder = sectionOrder;
            }
            offset += (int)size;
        }
    }

    private int GetSectionOrder(byte section) => section switch
    {
        1 => 1,
        2 => 2,
        3 => 3,
        4 => 4,
        5 => 5,
        13 => 6,
        6 => 7,
        7 => 8,
        8 => 9,
        9 => 10,
        12 => 11,
        10 => 12,
        11 => 13,
        _ => throw exceptions.Create(
            $"emitted module contains unknown section id {section}"),
    };

    private uint ReadUnsigned(ReadOnlySpan<byte> bytes, ref int offset)
    {
        uint value = 0;
        var shift = 0;
        for (var count = 0; count < 5; count++)
        {
            if (offset >= bytes.Length)
            {
                throw exceptions.Create(
                    "emitted module contains a truncated unsigned LEB128 value");
            }
            var current = bytes[offset++];
            value |= (uint)(current & 0x7f) << shift;
            if ((current & 0x80) == 0) return value;
            shift += 7;
        }
        throw exceptions.Create(
            "emitted module contains an oversized unsigned LEB128 value");
    }
}
