// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.ModuleEncoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class FunctionInlineAnnotationWriterTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EncodesOnlyRequestedHintsWithImportOffsetsAndNeverPrecedence(bool aggressive, bool never)
    {
        var buffer = new WasmBinaryBuffer();
        var function = new WasmFunctionDefinition("Fixture", WasmFunctionType.Create(CliValueKind.Void), [])
        {
            IsAggressiveInlining = aggressive,
            IsNoInlining = never,
        };
        var unmarked = function with { IsAggressiveInlining = false, IsNoInlining = false };
        Create().Write(new WasmBinaryWriter(buffer), [unmarked, function], 257);
        var actual = new WasmBinarySnapshotReader(buffer).Read();

        if (!aggressive && !never)
        {
            Assert.Empty(actual);
            return;
        }
        using var payloadStream = new MemoryStream();
        using var payload = new BinaryWriter(payloadStream, System.Text.Encoding.UTF8, leaveOpen: true);
        payload.Write("binaryen.inline");
        payload.Write7BitEncodedInt(1);
        payload.Write7BitEncodedInt(258);
        payload.Write7BitEncodedInt(1);
        payload.Write7BitEncodedInt(0);
        payload.Write7BitEncodedInt(1);
        payload.Write(never ? (byte)0 : (byte)127);
        using var expectedStream = new MemoryStream();
        using var expected = new BinaryWriter(expectedStream);
        expected.Write((byte)0);
        expected.Write7BitEncodedInt((int)payloadStream.Length);
        expected.Write(payloadStream.ToArray());
        Assert.Equal(expectedStream.ToArray(), actual);
    }

    [Fact]
    public void EmptyFunctionsProduceNoSection()
    {
        var buffer = new WasmBinaryBuffer();
        Create().Write(new WasmBinaryWriter(buffer), [], 0);
        Assert.Equal(0, buffer.Length);
    }

    [Fact]
    public void RejectsInvalidArgumentsBeforeWriting()
    {
        var buffer = new WasmBinaryBuffer();
        var output = new WasmBinaryWriter(buffer);
        var writer = Create();
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!, [], 0));
        Assert.Throws<ArgumentNullException>(() => writer.Write(output, null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Write(output, [], -1));
        Assert.Equal(0, buffer.Length);
    }

    [Fact]
    public void RequiresEveryEncodingCapability()
    {
        var unsigned = new UnsignedLeb128Encoder();
        var utf8 = new Utf8StringEncoder(unsigned);
        var sections = new WasmSectionWriter(unsigned);
        Func<WasmBinaryBuffer, IWasmBinaryWriter> writers = buffer => new WasmBinaryWriter(buffer);
        Func<WasmBinaryBuffer, IWasmBinarySnapshotReader> snapshots = buffer => new WasmBinarySnapshotReader(buffer);
        Assert.Throws<ArgumentNullException>(() => new FunctionInlineAnnotationWriter(null!, utf8, sections, writers, snapshots));
        Assert.Throws<ArgumentNullException>(() => new FunctionInlineAnnotationWriter(unsigned, null!, sections, writers, snapshots));
        Assert.Throws<ArgumentNullException>(() => new FunctionInlineAnnotationWriter(unsigned, utf8, null!, writers, snapshots));
        Assert.Throws<ArgumentNullException>(() => new FunctionInlineAnnotationWriter(unsigned, utf8, sections, null!, snapshots));
        Assert.Throws<ArgumentNullException>(() => new FunctionInlineAnnotationWriter(unsigned, utf8, sections, writers, null!));
    }

    private static IFunctionInlineAnnotationWriter Create()
    {
        var unsigned = new UnsignedLeb128Encoder();
        return new[] { new FunctionInlineAnnotationWriter(unsigned, new Utf8StringEncoder(unsigned),
            new WasmSectionWriter(unsigned), buffer => new WasmBinaryWriter(buffer),
            buffer => new WasmBinarySnapshotReader(buffer)) }
            .Cast<IFunctionInlineAnnotationWriter>().Single();
    }
}
