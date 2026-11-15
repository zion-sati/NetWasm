// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeInlineIntentWriter : IRuntimeInlineIntentWriter
{
    private const string InlineSectionName = "binaryen.inline";
    private static readonly byte[] Header = "\0asm\x01\0\0\0"u8.ToArray();
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public byte[] Write(
        ReadOnlyMemory<byte> module,
        ImmutableArray<RuntimeFunctionInlineIntent> intents)
    {
        if (intents.IsDefault)
            throw Invalid();
        try
        {
            var source = module.Span;
            var reader = new BinaryReader(source);
            if (!reader.Bytes(Header.Length).SequenceEqual(Header))
                throw Invalid();
            var sections = new List<Section>();
            Section? exports = null;
            Section? inline = null;
            while (reader.Remaining != 0)
            {
                var start = reader.Offset;
                var id = reader.Byte();
                var payloadLength = checked((int)reader.Unsigned());
                var payloadStart = reader.Offset;
                var payload = reader.Bytes(payloadLength);
                var section = new Section(id, start, payloadStart, reader.Offset);
                sections.Add(section);
                if (id == 7)
                {
                    if (exports is not null)
                        throw Invalid();
                    exports = section;
                }
                if (id != 0)
                    continue;
                var custom = new BinaryReader(payload);
                if (custom.Name() != InlineSectionName)
                    continue;
                if (inline is not null)
                    throw Invalid();
                inline = section;
            }

            var requested = ValidateIntents(intents);
            if (requested.Count == 0)
                return module.ToArray();
            if (exports is null)
                throw Invalid();
            var functions = ReadFunctionExports(source[exports.Value.PayloadStart..exports.Value.End]);
            var entries = inline is null
                ? []
                : ReadAnnotations(source[inline.Value.PayloadStart..inline.Value.End]);
            var byFunction = entries.ToDictionary(entry => entry.FunctionIndex);
            foreach (var intent in requested)
            {
                if (!functions.TryGetValue(intent.Export, out var functionIndex))
                    throw Invalid();
                if (!byFunction.TryGetValue(functionIndex, out var entry))
                {
                    entry = new(functionIndex, []);
                    entries.Add(entry);
                    byFunction.Add(functionIndex, entry);
                }
                var functionHints = entry.Annotations.Where(IsFunctionHint).ToArray();
                var hasNever = functionHints.Any(annotation => annotation.Hint[0] == 0);
                if (intent.Preference == RuntimeFunctionInlinePreference.Aggressive)
                {
                    if (hasNever)
                    {
                        entry.Annotations.RemoveAll(annotation =>
                            IsFunctionHint(annotation) && annotation.Hint[0] == 127);
                        continue;
                    }
                    if (functionHints.Length != 0)
                        continue;
                }
                else
                {
                    entry.Annotations.RemoveAll(IsFunctionHint);
                }
                entry.Annotations.Add(new(0, [Hint(intent.Preference)]));
            }

            var replacement = WriteInlineSection(entries);
            using var output = new MemoryStream(source.Length + replacement.Length);
            output.Write(Header);
            foreach (var section in sections)
            {
                if (inline is not null && section.Start == inline.Value.Start)
                    output.Write(replacement);
                else
                    output.Write(source[section.Start..section.End]);
            }
            if (inline is null)
                output.Write(replacement);
            return output.ToArray();
        }
        catch (Exception exception) when (
            exception is not InvalidOperationException ||
            exception.Message != "The runtime inline-intent metadata is invalid.")
        {
            throw Invalid(exception);
        }
    }

    private static List<RuntimeFunctionInlineIntent> ValidateIntents(
        ImmutableArray<RuntimeFunctionInlineIntent> intents)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<RuntimeFunctionInlineIntent>(intents.Length);
        foreach (var intent in intents)
        {
            if (intent is null || string.IsNullOrWhiteSpace(intent.Export) ||
                !Enum.IsDefined(intent.Preference) || !seen.Add(intent.Export))
                throw Invalid();
            result.Add(intent);
        }
        return result;
    }

    private static Dictionary<string, uint> ReadFunctionExports(ReadOnlySpan<byte> payload)
    {
        var reader = new BinaryReader(payload);
        var count = reader.Unsigned();
        var functions = new Dictionary<string, uint>(StringComparer.Ordinal);
        for (var index = 0u; index < count; index++)
        {
            var name = reader.Name();
            var kind = reader.Byte();
            var itemIndex = reader.Unsigned();
            if (kind == 0 && !functions.TryAdd(name, itemIndex))
                throw Invalid();
        }
        reader.RequireEnd();
        return functions;
    }

    private static List<AnnotationEntry> ReadAnnotations(ReadOnlySpan<byte> payload)
    {
        var reader = new BinaryReader(payload);
        if (reader.Name() != InlineSectionName)
            throw Invalid();
        var count = reader.Unsigned();
        var entries = new List<AnnotationEntry>(checked((int)count));
        var functions = new HashSet<uint>();
        for (var index = 0u; index < count; index++)
        {
            var function = reader.Unsigned();
            if (!functions.Add(function))
                throw Invalid();
            var annotationCount = reader.Unsigned();
            var annotations = new List<InlineAnnotation>(checked((int)annotationCount));
            for (var annotation = 0u; annotation < annotationCount; annotation++)
            {
                var instructionOffset = reader.Unsigned();
                var hint = reader.Bytes(checked((int)reader.Unsigned())).ToArray();
                if (hint.Length == 0)
                    throw Invalid();
                annotations.Add(new(instructionOffset, hint));
            }
            entries.Add(new(function, annotations));
        }
        reader.RequireEnd();
        return entries;
    }

    private static byte[] WriteInlineSection(List<AnnotationEntry> entries)
    {
        using var payload = new MemoryStream();
        WriteName(payload, InlineSectionName);
        WriteUnsigned(payload, checked((uint)entries.Count));
        foreach (var entry in entries)
        {
            WriteUnsigned(payload, entry.FunctionIndex);
            WriteUnsigned(payload, checked((uint)entry.Annotations.Count));
            foreach (var annotation in entry.Annotations)
            {
                WriteUnsigned(payload, annotation.InstructionOffset);
                WriteUnsigned(payload, checked((uint)annotation.Hint.Length));
                payload.Write(annotation.Hint);
            }
        }
        using var section = new MemoryStream();
        section.WriteByte(0);
        WriteUnsigned(section, checked((uint)payload.Length));
        payload.Position = 0;
        payload.CopyTo(section);
        return section.ToArray();
    }

    private static byte Hint(RuntimeFunctionInlinePreference preference) => preference switch
    {
        RuntimeFunctionInlinePreference.Never => 0,
        RuntimeFunctionInlinePreference.Aggressive => 127,
        _ => throw Invalid(),
    };

    private static bool IsFunctionHint(InlineAnnotation annotation) =>
        annotation.InstructionOffset == 0 && annotation.Hint is [0 or 127];

    private static void WriteName(Stream output, string value)
    {
        var bytes = Utf8.GetBytes(value);
        WriteUnsigned(output, checked((uint)bytes.Length));
        output.Write(bytes);
    }

    private static void WriteUnsigned(Stream output, uint value)
    {
        while (true)
        {
            var item = (byte)(value & 0x7f);
            value >>= 7;
            output.WriteByte((byte)(item | (value == 0 ? 0 : 0x80)));
            if (value == 0)
                return;
        }
    }

    private static InvalidOperationException Invalid(Exception? cause = null) =>
        new("The runtime inline-intent metadata is invalid.", cause);

    private readonly record struct Section(byte Id, int Start, int PayloadStart, int End);
    private sealed record AnnotationEntry(uint FunctionIndex, List<InlineAnnotation> Annotations);
    private sealed record InlineAnnotation(uint InstructionOffset, byte[] Hint);

    private ref struct BinaryReader
    {
        private readonly int _length;
        private ReadOnlySpan<byte> _remaining;

        public BinaryReader(ReadOnlySpan<byte> source)
        {
            _length = source.Length;
            _remaining = source;
        }

        public readonly int Offset => _length - _remaining.Length;
        public readonly int Remaining => _remaining.Length;

        public byte Byte()
        {
            if (_remaining.IsEmpty)
                throw Invalid();
            var value = _remaining[0];
            _remaining = _remaining[1..];
            return value;
        }

        public ReadOnlySpan<byte> Bytes(int length)
        {
            if (length < 0 || length > _remaining.Length)
                throw Invalid();
            var value = _remaining[..length];
            _remaining = _remaining[length..];
            return value;
        }

        public string Name() => Utf8.GetString(Bytes(checked((int)Unsigned())));

        public uint Unsigned()
        {
            uint value = 0;
            for (var shift = 0; shift < 35; shift += 7)
            {
                var item = Byte();
                var payload = item & 0x7f;
                if (shift == 28 && payload > 0x0f)
                    throw Invalid();
                value |= (uint)payload << shift;
                if ((item & 0x80) == 0)
                    return value;
            }
            throw Invalid();
        }

        public readonly void RequireEnd()
        {
            if (!_remaining.IsEmpty)
                throw Invalid();
        }
    }
}
