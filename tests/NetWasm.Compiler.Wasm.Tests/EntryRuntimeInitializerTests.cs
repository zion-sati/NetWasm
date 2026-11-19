// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class EntryRuntimeInitializerTests
{
    [Fact]
    public void EntriesCallTheModulesRuntimeInitializer()
    {
        var code = new GeneratedFunctionWriterFactory().Create();
        var writer = new EmitterTestSupport.RecordingInstructionWriter();

        new EntryRuntimeInitializer().Initialize(
            new GeneratedFunctionWriterLease(code.Bytes, code.Snapshots, writer),
            TestRuntimeInitialization.Create(512) with { InitializerFunctionIndex = 300 });

        var call = Assert.Single(writer.ToInstructions());
        Assert.Equal(WasmOpcodes.Call, call.Opcode);
        Assert.Equal(300u, call.Operand.UnsignedValue);
    }

    [Fact]
    public void InitializationRequiresTheInitializerFunction()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new EntryRuntimeInitializer().Initialize(
            new GeneratedFunctionWriterFactory().Create(),
            TestRuntimeInitialization.Create(512) with { InitializerFunctionIndex = null }));

        Assert.Contains("initializer function", exception.Message, StringComparison.Ordinal);
    }
}
