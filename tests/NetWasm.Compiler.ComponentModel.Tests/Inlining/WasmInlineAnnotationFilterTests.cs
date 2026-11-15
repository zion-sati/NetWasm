// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Inlining;

public sealed class WasmInlineAnnotationFilterTests
{
    private static readonly byte[] Header = [0, 97, 115, 109, 1, 0, 0, 0];

    [Theory]
    [InlineData(WasmInlineAnnotationSelection.Preserve)]
    [InlineData(WasmInlineAnnotationSelection.SizeAware)]
    [InlineData(WasmInlineAnnotationSelection.Remove)]
    public void PreservesUnrelatedBytesWhileSelectingOnlyFunctionLevelHints(WasmInlineAnnotationSelection selection)
    {
        var opaque = Section(0, Payload(writer => { writer.Write("other"); writer.Write((byte)42); }));
        var code = Section(10, [0]);
        var annotations = Annotations(
            (130, [(0, new byte[] { 127 })]),
            (131, [(0, new byte[] { 0 })]),
            (132, [(10, new byte[] { 127 }), (0, new byte[] { 25 }), (0, new byte[] { 127, 1 })]));
        var source = Module(opaque, annotations, code);
        var expectedAnnotations = selection switch
        {
            WasmInlineAnnotationSelection.Preserve => annotations,
            WasmInlineAnnotationSelection.SizeAware => Annotations(
                (131, [(0, new byte[] { 0 })]),
                (132, [(10, new byte[] { 127 }), (0, new byte[] { 25 }), (0, new byte[] { 127, 1 })])),
            _ => [],
        };

        var actual = Create().Filter(source, selection);
        Assert.Equal(Module(opaque, expectedAnnotations, code), actual);
        Assert.Equal(Module(opaque, annotations, code), source);
    }

    [Fact]
    public void RemovesOnlyAggressiveEntryWhenFunctionHasMixedAnnotations()
    {
        var source = Module(Annotations((1, [(0, new byte[] { 127 }), (0, new byte[] { 0 })])));
        var expected = Module(Annotations((1, [(0, new byte[] { 0 })])));
        Assert.Equal(expected, Create().Filter(source, WasmInlineAnnotationSelection.SizeAware));
    }

    [Fact]
    public void DropsEmptyFilteredSectionAndPreservesAlreadyEmptyInput()
    {
        var filter = Create();
        Assert.Equal(Header, filter.Filter(Module(Annotations((1, [(0, new byte[] { 127 })]))), WasmInlineAnnotationSelection.SizeAware));
        var empty = Module(Annotations());
        Assert.Equal(empty, filter.Filter(empty, WasmInlineAnnotationSelection.SizeAware));
        Assert.Equal(Header, filter.Filter(empty, WasmInlineAnnotationSelection.Remove));
        Assert.Equal(Header, filter.Filter(Header, WasmInlineAnnotationSelection.Preserve));
        var noEntries = Module(Annotations((1, [])));
        Assert.Equal(noEntries, filter.Filter(noEntries, WasmInlineAnnotationSelection.Preserve));
    }

    [Fact]
    public void PreservesPaddedSectionLengthWhenNoAnnotationChanges()
    {
        var encoded = Annotations((1, [(0, new byte[] { 0 })]));
        var padded = new byte[] { encoded[0], (byte)(encoded[1] | 128), 0 }.Concat(encoded.Skip(2)).ToArray();
        var source = Module(padded);
        Assert.Equal(source, Create().Filter(source, WasmInlineAnnotationSelection.SizeAware));
    }

    [Fact]
    public void RejectsInvalidSelectionAndMissingInput()
    {
        var filter = Create();
        Assert.Throws<ArgumentNullException>(() => filter.Filter(null!, WasmInlineAnnotationSelection.Preserve));
        Assert.Throws<ArgumentOutOfRangeException>(() => filter.Filter(Header, (WasmInlineAnnotationSelection)255));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void RejectsMalformedSectionsBeforeReturningOutput(int invalidCase)
    {
        var section = Annotations((1, [(0, new byte[] { 0 })]));
        var bytes = invalidCase switch
        {
            0 => Array.Empty<byte>(),
            1 => new byte[8],
            2 => Module([10]),
            3 => Module([10, 128, 128, 128, 128, 16]),
            4 => Module([10, 255, 255, 255, 255, 15]),
            5 => Module(Section(0, [1, 255])),
            6 => Module(Section(0, [5, 1])),
            7 => Module(section, section),
            8 => Module(Annotations((1, [(0, new byte[] { 0 })]), (1, [(0, new byte[] { 0 })]))),
            9 => Module(Section(0, Payload(writer => { writer.Write("binaryen.inline"); writer.Write((byte)0); writer.Write((byte)1); }))),
            _ => Module(Section(0, Payload(writer => { writer.Write("binaryen.inline"); writer.Write((byte)1); }))),
        };
        Assert.Throws<CompilerException>(() => Create().Filter(bytes, WasmInlineAnnotationSelection.SizeAware));
    }

    private static IWasmInlineAnnotationFilter Create() =>
        Assert.IsAssignableFrom<IWasmInlineAnnotationFilter>(new WasmInlineAnnotationFilter());

    private static byte[] Module(params byte[][] sections) => [.. Header, .. sections.SelectMany(section => section)];

    private static byte[] Section(byte id, byte[] payload) => Payload(writer =>
    {
        writer.Write(id);
        writer.Write7BitEncodedInt(payload.Length);
        writer.Write(payload);
    });

    private static byte[] Annotations(params (int Index, (int Offset, byte[] Hint)[] Entries)[] functions) =>
        Section(0, Payload(writer =>
        {
            writer.Write("binaryen.inline");
            writer.Write7BitEncodedInt(functions.Length);
            foreach (var function in functions)
            {
                writer.Write7BitEncodedInt(function.Index);
                writer.Write7BitEncodedInt(function.Entries.Length);
                foreach (var (offset, hint) in function.Entries)
                {
                    writer.Write7BitEncodedInt(offset);
                    writer.Write7BitEncodedInt(hint.Length);
                    writer.Write(hint);
                }
            }
        }));

    private static byte[] Payload(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        write(writer);
        return stream.ToArray();
    }
}
