using System;
using System.Collections.Generic;
using System.IO;
using NetWasm.Compiler.ComponentModel.Binary;

namespace NetWasm.Compiler.ComponentModel.Inlining;

public sealed class WasmInlineAnnotationFilter : IWasmInlineAnnotationFilter
{
    public byte[] Filter(byte[] source, WasmInlineAnnotationSelection selection)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!Enum.IsDefined(selection))
            throw new ArgumentOutOfRangeException(nameof(selection));
        var reader = new WasmBinaryReader(source);
        reader.RequireHeader();
        // A module without hints, as a link below O1 writes, is returned as it is.
        if (!HasAnnotations(source))
            return source;
        using var output = new MemoryStream(source.Length);
        output.Write(WasmBinaryFormat.Header);
        var sawAnnotations = false;
        while (!reader.IsAtEnd)
        {
            var start = reader.Offset;
            var sectionId = reader.ReadByte();
            var section = reader.ReadBytes(reader.ReadUnsigned());
            if (sectionId == 0)
            {
                var custom = new WasmBinaryReader(section);
                if (custom.ReadString() == "binaryen.inline")
                {
                    if (sawAnnotations)
                        throw ComponentException.Invalid("core module contains duplicate inline annotation sections");
                    sawAnnotations = true;
                    var filtered = FilterAnnotations(section, ref custom, selection);
                    if (filtered is not null)
                    {
                        if (filtered.AsSpan().SequenceEqual(section))
                            output.Write(source.AsSpan(start, reader.Offset - start));
                        else
                        {
                            output.WriteByte(0);
                            WasmUnsignedLeb128.Write(output, (uint)filtered.Length);
                            output.Write(filtered);
                        }
                    }
                    continue;
                }
            }
            output.Write(source.AsSpan(start, reader.Offset - start));
        }
        return output.ToArray();
    }

    private static bool HasAnnotations(byte[] source)
    {
        var reader = new WasmBinaryReader(source);
        reader.RequireHeader();
        while (!reader.IsAtEnd)
        {
            var sectionId = reader.ReadByte();
            var section = reader.ReadBytes(reader.ReadUnsigned());
            if (sectionId == 0 && new WasmBinaryReader(section).ReadString() == "binaryen.inline")
                return true;
        }
        return false;
    }

    private static byte[]? FilterAnnotations(
        ReadOnlySpan<byte> section, ref WasmBinaryReader reader, WasmInlineAnnotationSelection selection)
    {
        var nameEnd = reader.Offset;
        var functions = reader.ReadUnsigned();
        var seen = new HashSet<uint>();
        var retainedFunctions = new List<byte[]>();
        var changed = selection == WasmInlineAnnotationSelection.Remove;
        for (var index = 0u; index < functions; index++)
        {
            var functionStart = reader.Offset;
            var functionIndex = reader.ReadUnsigned();
            if (!seen.Add(functionIndex))
                throw ComponentException.Invalid("core module repeats an inline annotation function");
            var functionIndexEnd = reader.Offset;
            var annotations = reader.ReadUnsigned();
            var retainedAnnotations = new List<byte[]>();
            for (var annotation = 0u; annotation < annotations; annotation++)
            {
                var annotationStart = reader.Offset;
                var offset = reader.ReadUnsigned();
                var hint = reader.ReadBytes(reader.ReadUnsigned());
                var discard = selection == WasmInlineAnnotationSelection.Remove ||
                    (selection == WasmInlineAnnotationSelection.SizeAware && offset == 0 &&
                        hint.SequenceEqual([(byte)127]));
                changed |= discard;
                if (!discard)
                    retainedAnnotations.Add(section[annotationStart..reader.Offset].ToArray());
            }
            if (retainedAnnotations.Count == 0)
                continue;
            using var entry = new MemoryStream();
            entry.Write(section[functionStart..functionIndexEnd]);
            WasmUnsignedLeb128.Write(entry, (uint)retainedAnnotations.Count);
            foreach (var annotation in retainedAnnotations)
                entry.Write(annotation);
            retainedFunctions.Add(entry.ToArray());
        }
        reader.RequireEnd("inline annotation section");
        if (!changed)
            return section.ToArray();
        if (retainedFunctions.Count == 0)
            return null;
        using var output = new MemoryStream(section.Length);
        output.Write(section[..nameEnd]);
        WasmUnsignedLeb128.Write(output, (uint)retainedFunctions.Count);
        foreach (var function in retainedFunctions)
            output.Write(function);
        return output.ToArray();
    }
}
