// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class InstantiationStateInitializerTests
{
    // Instantiation may not wait, so it calls the state initializer, never the entries' initializer,
    // which also runs the module initializers.
    [Fact]
    public void InstantiationCallsTheStateInitializerOnly()
    {
        var code = new GeneratedFunctionWriterFactory().Create();
        var writer = new EmitterTestSupport.RecordingInstructionWriter();

        new InstantiationStateInitializer().Initialize(
            new GeneratedFunctionWriterLease(code.Bytes, code.Snapshots, writer),
            TestRuntimeInitialization.Create(512) with { InitializerFunctionIndex = 301, StateInitializerFunctionIndex = 300 });

        var call = Assert.Single(writer.ToInstructions());
        Assert.Equal(WasmOpcodes.Call, call.Opcode);
        Assert.Equal(300u, call.Operand.UnsignedValue);
    }

    [Fact]
    public void InstantiationRequiresTheStateInitializerFunction()
    {
        var initializer = new InstantiationStateInitializer();

        var exception = Assert.Throws<InvalidOperationException>(() => initializer.Initialize(
            new GeneratedFunctionWriterFactory().Create(),
            TestRuntimeInitialization.Create(512) with { StateInitializerFunctionIndex = null }));
        Assert.Contains("state initializer function", exception.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => initializer.Initialize(null!, TestRuntimeInitialization.Create(512)));
        Assert.Throws<ArgumentNullException>(() => initializer.Initialize(new GeneratedFunctionWriterFactory().Create(), null!));
    }
}
