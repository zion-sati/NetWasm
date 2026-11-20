// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

// Every entry readies the runtime state, then runs the module initializers in their planned
// order through their guard functions, so each runs once, in the first entry.
public sealed class EntryInitializerFunctionEmitterTests
{
    [Fact]
    public void CallsTheStateInitializerThenEachModuleInitializersGuardFunction()
    {
        var body = new EntryInitializerFunctionEmitter(new GeneratedFunctionWriterFactory())
            .Emit(300, [new(256, 37), new(260, 41)]);

        Assert.Equal<byte>(
            [0,
                WasmOpcodes.Call, 0xac, 0x02,
                WasmOpcodes.Call, 37,
                WasmOpcodes.Call, 41,
                WasmOpcodes.End],
            body);
    }

    [Fact]
    public void RejectsANegativeStateInitializerAndAnEmptyModuleInitializerList()
    {
        var emitter = new EntryInitializerFunctionEmitter(new GeneratedFunctionWriterFactory());

        Assert.Throws<ArgumentOutOfRangeException>(() => emitter.Emit(-1, [new(256, 37)]));
        Assert.Throws<ArgumentException>(() => emitter.Emit(300, []));
        Assert.Throws<ArgumentException>(() => emitter.Emit(300, default));
    }
}
