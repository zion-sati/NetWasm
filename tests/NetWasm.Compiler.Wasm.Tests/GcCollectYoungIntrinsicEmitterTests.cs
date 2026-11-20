// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

// GC.Collect(0) under a collector with generations asks the runtime for a collection of the youngest generation,
// through an import only a program that asks has.
public sealed class GcCollectYoungIntrinsicEmitterTests
{
    [Fact]
    public void EmitCallsTheRuntimesYoungCollection()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.GcCollectYoung, []);
        request = request with
        {
            Call = request.Call with
            {
                Instruction = request.Instruction with
                {
                    Target = request.Instruction.Target with
                    {
                        RuntimeImportSelection = request.Instruction.Target.RuntimeImportSelection with
                        {
                            IncludeYoungCollection = true,
                        },
                    },
                },
            },
        };
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        new GcCollectYoungIntrinsicEmitter(imports).Emit(request, code);

        var instruction = Assert.Single(code.ToInstructions());
        Assert.Equal(WasmOpcodes.Call, instruction.Opcode);
        Assert.Equal(
            (uint)imports.Resolve(RuntimeImportSymbol.GcCollectYoung, request.Instruction.Target.RuntimeImportSelection),
            instruction.Operand.UnsignedValue);
    }

    // The import comes last of the optional ones, and only a program that asks has it.
    [Fact]
    public void OnlyAProgramThatAsksImportsTheYoungCollection()
    {
        var catalog = WasmRuntimeImports.CreateCatalog();
        var asks = new RuntimeImportSelection(WasmModuleProfile.CoreApplication, false, IncludeYoungCollection: true);
        var silent = asks with { IncludeYoungCollection = false };

        Assert.True(asks.Includes(RuntimeImportSymbol.GcCollectYoung));
        Assert.False(silent.Includes(RuntimeImportSymbol.GcCollectYoung));
        Assert.Equal("gc_collect_young", catalog.Resolve(asks)[^1].Name);
        Assert.Equal(catalog.Resolve(silent), catalog.Resolve(asks).RemoveAt(catalog.Resolve(asks).Length - 1));
    }

    [Fact]
    public void RejectsMissingCollaboratorAndArguments()
    {
        var emitter = new GcCollectYoungIntrinsicEmitter(WasmRuntimeImports.CreateCatalog());
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.GcCollectYoung, []);

        Assert.Throws<ArgumentNullException>(() => new GcCollectYoungIntrinsicEmitter(null!));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, new EmitterTestSupport.RecordingInstructionWriter()));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(request, null!));
    }
}
