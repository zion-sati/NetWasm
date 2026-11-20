// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

// The WASI event loop asks the runtime for idle and quiet collections through imports only a program whose loop
// asks has; both can collect, so a caller's references are published before either.
public sealed class EventLoopCollectionIntrinsicEmitterTests
{
    [Theory]
    [InlineData(RuntimeIntrinsic.GcCollectWhenIdle)]
    [InlineData(RuntimeIntrinsic.GcCollectWhenQuiet)]
    public void EmitCallsTheRuntimeEntryAndPublishesItsAnswer(RuntimeIntrinsic intrinsic)
    {
        var symbol = intrinsic == RuntimeIntrinsic.GcCollectWhenIdle
            ? RuntimeImportSymbol.CollectWhenIdle
            : RuntimeImportSymbol.CollectWhenQuiet;
        var imports = WasmRuntimeImports.CreateCatalog();
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(intrinsic, []);
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
                            IncludeEventLoopCollection = true,
                        },
                    },
                },
            },
        };
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        Emitter(intrinsic, imports).Emit(request, code);

        var instructions = code.ToInstructions();
        Assert.Equal([WasmOpcodes.Call, WasmOpcodes.LocalSet], instructions.Select(instruction => instruction.Opcode));
        Assert.Equal(
            (uint)imports.Resolve(symbol, request.Instruction.Target.RuntimeImportSelection),
            instructions[0].Operand.UnsignedValue);
        Assert.Equal((uint)request.Local(0, CliValueKind.I4), instructions[1].Operand.UnsignedValue);
    }

    // The two imports follow every other optional import, and only a program whose loop asks has them.
    [Fact]
    public void OnlyAProgramWhoseLoopAsksImportsTheEntries()
    {
        var catalog = WasmRuntimeImports.CreateCatalog();
        var asks = new RuntimeImportSelection(WasmModuleProfile.CoreApplication, false, IncludeEventLoopCollection: true);
        var silent = asks with { IncludeEventLoopCollection = false };

        Assert.True(asks.Includes(RuntimeImportSymbol.CollectWhenIdle));
        Assert.True(asks.Includes(RuntimeImportSymbol.CollectWhenQuiet));
        Assert.False(silent.Includes(RuntimeImportSymbol.CollectWhenIdle));
        Assert.False(silent.Includes(RuntimeImportSymbol.CollectWhenQuiet));
        Assert.Equal(["collect_when_idle", "collect_when_quiet"], catalog.Resolve(asks)[^2..].Select(import => import.Name));
        Assert.Equal(catalog.Resolve(silent), catalog.Resolve(asks).RemoveRange(catalog.Resolve(asks).Length - 2, 2));
        Assert.Contains(RuntimeImportSymbol.CollectWhenIdle, (IReadOnlySet<RuntimeImportSymbol>)CollectingRuntimeImports.Symbols);
        Assert.Contains(RuntimeImportSymbol.CollectWhenQuiet, (IReadOnlySet<RuntimeImportSymbol>)CollectingRuntimeImports.Symbols);
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.GcCollectWhenIdle)]
    [InlineData(RuntimeIntrinsic.GcCollectWhenQuiet)]
    public void RejectsMissingCollaboratorAndArguments(RuntimeIntrinsic intrinsic)
    {
        var emitter = Emitter(intrinsic, WasmRuntimeImports.CreateCatalog());
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(intrinsic, []);

        Assert.Throws<ArgumentNullException>(() => Emitter(intrinsic, null!));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, new EmitterTestSupport.RecordingInstructionWriter()));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(request, null!));
    }

    private static IRuntimeIntrinsicEmitter Emitter(RuntimeIntrinsic intrinsic, IRuntimeImportResolver imports) =>
        intrinsic == RuntimeIntrinsic.GcCollectWhenIdle
            ? new GcCollectWhenIdleIntrinsicEmitter(imports)
            : new GcCollectWhenQuietIntrinsicEmitter(imports);
}
