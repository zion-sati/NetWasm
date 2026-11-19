// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

// GC.GetGeneration asks the runtime for the object's generation, through an import only a program that asks has.
public sealed class GcObjectGenerationIntrinsicEmitterTests
{
    [Fact]
    public void EmitAsksTheRuntimeForTheObjectsGenerationAndPublishesInt32()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.GcObjectGeneration, [CliValueKind.ManagedReference]);
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
                            IncludeObjectGeneration = true,
                        },
                    },
                },
            },
        };
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        new GcObjectGenerationIntrinsicEmitter(imports).Emit(request, code);

        var instructions = code.ToInstructions();
        Assert.Equal([WasmOpcodes.LocalGet, WasmOpcodes.Call, WasmOpcodes.LocalSet],
            instructions.Select(instruction => instruction.Opcode));
        Assert.Equal((uint)request.Local(0, CliValueKind.ManagedReference), instructions[0].Operand.UnsignedValue);
        Assert.Equal(
            (uint)imports.Resolve(RuntimeImportSymbol.GcObjectGeneration, request.Instruction.Target.RuntimeImportSelection),
            instructions[1].Operand.UnsignedValue);
        Assert.Equal((uint)request.Local(0, CliValueKind.I4), instructions[2].Operand.UnsignedValue);
    }

    // The import follows every other optional import, and only a program that asks has it.
    [Fact]
    public void OnlyAProgramThatAsksImportsTheGeneration()
    {
        var catalog = WasmRuntimeImports.CreateCatalog();
        var asks = new RuntimeImportSelection(WasmModuleProfile.CoreApplication, false, IncludeObjectGeneration: true);
        var silent = asks with { IncludeObjectGeneration = false };

        Assert.True(asks.Includes(RuntimeImportSymbol.GcObjectGeneration));
        Assert.False(silent.Includes(RuntimeImportSymbol.GcObjectGeneration));
        Assert.Equal("gc_object_generation", catalog.Resolve(asks)[^1].Name);
        Assert.Equal(catalog.Resolve(silent), catalog.Resolve(asks).RemoveAt(catalog.Resolve(asks).Length - 1));
    }

    [Fact]
    public void RejectsMissingCollaboratorAndArguments()
    {
        var emitter = new GcObjectGenerationIntrinsicEmitter(WasmRuntimeImports.CreateCatalog());
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.GcObjectGeneration, [CliValueKind.ManagedReference]);

        Assert.Throws<ArgumentNullException>(() => new GcObjectGenerationIntrinsicEmitter(null!));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, new EmitterTestSupport.RecordingInstructionWriter()));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(request, null!));
    }
}
