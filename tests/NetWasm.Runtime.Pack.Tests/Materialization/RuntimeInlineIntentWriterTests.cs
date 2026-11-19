// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Text;
using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeInlineIntentWriterTests
{
    [Fact]
    public void WritesDeclaredFunctionIntentsAndPreservesUnrelatedSections()
    {
        var source = Module(
            CustomSection("producer", [1, 2, 3]),
            ExportSection(("root_frame_enter", 0, 3), ("value_frame_enter", 0, 7)));
        var writer = Assert.IsAssignableFrom<IRuntimeInlineIntentWriter>(
            new RuntimeInlineIntentWriter());

        var result = writer.Write(source,
        [
            new("root_frame_enter", RuntimeFunctionInlinePreference.Aggressive),
            new("value_frame_enter", RuntimeFunctionInlinePreference.Never),
        ]);

        Assert.Equal(["producer", "binaryen.inline"], CustomSectionNames(result));
        var annotations = ReadAnnotations(result);
        Assert.Equal([127], Assert.Single(annotations[3]).Hint);
        Assert.Equal([0], Assert.Single(annotations[7]).Hint);
    }

    // The link leaves out the optional runtime functions a program does not use, such as the write barrier
    // outside NetWasmGen: their intents have nothing to annotate, and the others are written as ever.
    [Fact]
    public void AnIntentForAnExportTheLinkLeftOutIsSkipped()
    {
        var source = Module(ExportSection(("root_frame_enter", 0, 3)));
        var writer = new RuntimeInlineIntentWriter();

        var result = writer.Write(source,
        [
            new("record_reference_write", RuntimeFunctionInlinePreference.Aggressive),
            new("root_frame_enter", RuntimeFunctionInlinePreference.Aggressive),
        ]);
        var untouched = writer.Write(source, [new("record_reference_write", RuntimeFunctionInlinePreference.Aggressive)]);

        Assert.Equal([127], Assert.Single(Assert.Single(ReadAnnotations(result)).Value).Hint);
        Assert.Equal(3u, Assert.Single(ReadAnnotations(result)).Key);
        Assert.Equal(source, untouched);
        Assert.NotSame(source, untouched);
    }

    [Fact]
    public void MergesExistingAnnotationsAndGivesNeverPrecedence()
    {
        var source = Module(
            ExportSection(("never", 0, 0), ("replace", 0, 1), ("append", 0, 2)),
            InlineSection(
                (0, [(0u, new byte[] { 127 }), (0u, new byte[] { 0 })]),
                (1, [(0u, new byte[] { 127 })]),
                (2, [(4u, new byte[] { 55 })])));

        var result = new RuntimeInlineIntentWriter().Write(source,
        [
            new("never", RuntimeFunctionInlinePreference.Aggressive),
            new("replace", RuntimeFunctionInlinePreference.Never),
            new("append", RuntimeFunctionInlinePreference.Aggressive),
        ]);

        var annotations = ReadAnnotations(result);
        AssertAnnotation(Assert.Single(annotations[0]), 0, [0]);
        AssertAnnotation(Assert.Single(annotations[1]), 0, [0]);
        Assert.Collection(
            annotations[2],
            annotation => AssertAnnotation(annotation, 4, [55]),
            annotation => AssertAnnotation(annotation, 0, [127]));
    }

    [Fact]
    public void EmptyIntentSetReturnsEquivalentOwnedBytes()
    {
        var source = Module(ExportSection(("function", 0, 0)));

        var result = new RuntimeInlineIntentWriter().Write(source, []);

        Assert.Equal(source, result);
        Assert.NotSame(source, result);
    }

    [Fact]
    public void RejectsInvalidIntentContractsBeforePublishingBytes()
    {
        var source = Module(ExportSection(("function", 0, 0), ("global", 3, 0)));
        var writer = new RuntimeInlineIntentWriter();
        var invalid = new ImmutableArray<RuntimeFunctionInlineIntent>[]
        {
            default,
            [null!],
            [new(" ", RuntimeFunctionInlinePreference.Aggressive)],
            [new("function", (RuntimeFunctionInlinePreference)42)],
            [new("function", RuntimeFunctionInlinePreference.Aggressive),
                new("function", RuntimeFunctionInlinePreference.Never)],
            [new("global", RuntimeFunctionInlinePreference.Aggressive)],
            [new("missing", RuntimeFunctionInlinePreference.Aggressive),
                new("global", RuntimeFunctionInlinePreference.Aggressive)],
        };

        foreach (var intents in invalid)
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                writer.Write(source, intents));
            Assert.Equal("The runtime inline-intent metadata is invalid.", exception.Message);
        }
    }

    [Fact]
    public void RejectsMalformedModuleAndAnnotationMetadata()
    {
        var writer = new RuntimeInlineIntentWriter();
        var intent = ImmutableArray.Create(
            new RuntimeFunctionInlineIntent("function", RuntimeFunctionInlinePreference.Aggressive));
        var malformed = new[]
        {
            Array.Empty<byte>(),
            "not-wasm"u8.ToArray(),
            Module(ExportSection(("function", 0, 0)), InlineSection((0, [(0u, Array.Empty<byte>())]))),
            Module(ExportSection(("function", 0, 0)), InlineSection((0, [(0u, new byte[] { 127 })])),
                InlineSection((1, [(0u, new byte[] { 127 })]))),
        };

        foreach (var module in malformed)
            Assert.Throws<InvalidOperationException>(() => writer.Write(module, intent));
    }

    private static byte[] Module(params byte[][] sections)
    {
        using var output = new MemoryStream();
        output.Write("\0asm\x01\0\0\0"u8);
        foreach (var section in sections)
            output.Write(section);
        return output.ToArray();
    }

    private static void AssertAnnotation(
        (uint Offset, byte[] Hint) annotation,
        uint expectedOffset,
        byte[] expectedHint)
    {
        Assert.Equal(expectedOffset, annotation.Offset);
        Assert.Equal(expectedHint, annotation.Hint);
    }

    private static byte[] ExportSection(params (string Name, byte Kind, uint Index)[] exports) =>
        Section(7, payload =>
        {
            Unsigned(payload, checked((uint)exports.Length));
            foreach (var export in exports)
            {
                Name(payload, export.Name);
                payload.WriteByte(export.Kind);
                Unsigned(payload, export.Index);
            }
        });

    private static byte[] CustomSection(string name, byte[] value) => Section(0, payload =>
    {
        Name(payload, name);
        payload.Write(value);
    });

    private static byte[] InlineSection(
        params (uint Function, (uint Offset, byte[] Hint)[] Annotations)[] entries) =>
        Section(0, payload =>
        {
            Name(payload, "binaryen.inline");
            Unsigned(payload, checked((uint)entries.Length));
            foreach (var entry in entries)
            {
                Unsigned(payload, entry.Function);
                Unsigned(payload, checked((uint)entry.Annotations.Length));
                foreach (var annotation in entry.Annotations)
                {
                    Unsigned(payload, annotation.Offset);
                    Unsigned(payload, checked((uint)annotation.Hint.Length));
                    payload.Write(annotation.Hint);
                }
            }
        });

    private static byte[] Section(byte id, Action<MemoryStream> write)
    {
        using var payload = new MemoryStream();
        write(payload);
        using var output = new MemoryStream();
        output.WriteByte(id);
        Unsigned(output, checked((uint)payload.Length));
        payload.Position = 0;
        payload.CopyTo(output);
        return output.ToArray();
    }

    private static string[] CustomSectionNames(byte[] module)
    {
        var reader = new Reader(module.AsSpan(8));
        var names = new List<string>();
        while (reader.Remaining != 0)
        {
            var id = reader.Byte();
            var section = new Reader(reader.Bytes(checked((int)reader.Unsigned())));
            if (id == 0)
                names.Add(section.Name());
        }
        return [.. names];
    }

    private static Dictionary<uint, (uint Offset, byte[] Hint)[]> ReadAnnotations(byte[] module)
    {
        var reader = new Reader(module.AsSpan(8));
        while (reader.Remaining != 0)
        {
            var id = reader.Byte();
            var section = new Reader(reader.Bytes(checked((int)reader.Unsigned())));
            if (id != 0 || section.Name() != "binaryen.inline")
                continue;
            var count = section.Unsigned();
            var result = new Dictionary<uint, (uint Offset, byte[] Hint)[]>();
            for (var index = 0u; index < count; index++)
            {
                var function = section.Unsigned();
                var annotations = new (uint Offset, byte[] Hint)[checked((int)section.Unsigned())];
                for (var annotation = 0; annotation < annotations.Length; annotation++)
                {
                    var offset = section.Unsigned();
                    annotations[annotation] = (offset,
                        section.Bytes(checked((int)section.Unsigned())).ToArray());
                }
                result.Add(function, annotations);
            }
            return result;
        }
        throw new InvalidOperationException("Inline annotations were not written.");
    }

    private static void Name(Stream output, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Unsigned(output, checked((uint)bytes.Length));
        output.Write(bytes);
    }

    private static void Unsigned(Stream output, uint value)
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

    private ref struct Reader
    {
        private ReadOnlySpan<byte> _remaining;

        public Reader(ReadOnlySpan<byte> source) => _remaining = source;
        public readonly int Remaining => _remaining.Length;
        public byte Byte()
        {
            var value = _remaining[0];
            _remaining = _remaining[1..];
            return value;
        }
        public ReadOnlySpan<byte> Bytes(int length)
        {
            var value = _remaining[..length];
            _remaining = _remaining[length..];
            return value;
        }
        public string Name() => Encoding.UTF8.GetString(Bytes(checked((int)Unsigned())));
        public uint Unsigned()
        {
            uint value = 0;
            for (var shift = 0; ; shift += 7)
            {
                var item = Byte();
                value |= (uint)(item & 0x7f) << shift;
                if ((item & 0x80) == 0)
                    return value;
            }
        }
    }
}
