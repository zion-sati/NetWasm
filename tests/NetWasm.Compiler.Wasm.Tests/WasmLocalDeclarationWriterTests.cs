using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class WasmLocalDeclarationWriterTests
{
    [Fact]
    public void WritesCompactWasm32LocalGroups()
    {
        var code = new RawWriter();

        WasmLocalDeclarationWriter.Write(
            code,
            [CliValueKind.I4, CliValueKind.ManagedReference, CliValueKind.ManagedAddress],
            WasmTargetLayout.Wasm32);

        Assert.Equal([0x01, 0x03, 0x7f], code.ToArray());
    }

    [Fact]
    public void PreservesPhysicalTypeBoundariesForMemory64Locals()
    {
        var code = new RawWriter();

        WasmLocalDeclarationWriter.Write(
            code,
            [
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
            ],
            WasmTargetLayout.Wasm64);

        Assert.Equal([0x03, 0x01, 0x7f, 0x02, 0x7e, 0x01, 0x7f], code.ToArray());
    }

    [Fact]
    public void WritesEmptyLocalVectorAndValidatesArguments()
    {
        var code = new RawWriter();

        WasmLocalDeclarationWriter.Write(code, [], WasmTargetLayout.Wasm64);

        Assert.Equal([0x00], code.ToArray());
        Assert.Throws<ArgumentNullException>(() =>
            WasmLocalDeclarationWriter.Write(null!, [], WasmTargetLayout.Wasm32));
        Assert.Throws<ArgumentNullException>(() =>
            WasmLocalDeclarationWriter.Write(code, null!, WasmTargetLayout.Wasm32));
        Assert.Throws<ArgumentNullException>(() =>
            WasmLocalDeclarationWriter.Write(code, [], null!));
    }

    private sealed class RawWriter : IWasmBinaryWriter
    {
        private readonly List<byte> _bytes = [];

        public void Write(ReadOnlySpan<byte> bytes)
        {
            foreach (var value in bytes)
            {
                _bytes.Add(value);
            }
        }

        public byte[] ToArray() => [.. _bytes];
    }
}
