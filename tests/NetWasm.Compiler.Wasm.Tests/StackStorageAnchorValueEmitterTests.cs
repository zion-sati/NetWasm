// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StackStorageAnchorValueEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void ReadsTheAuthoritativeLocalOrEnvironmentCell(WasmTarget target, bool shared)
    {
        var context = CreateContext(shared);
        var addresses = new RecordingAddresses();
        var code = new RecordingInstructionWriter();
        var emitter = CreateEmitter(target, addresses);
        emitter.Emit(code, context, 0);
        var instruction = Assert.Single(code.ToInstructions());
        if (shared)
        {
            Assert.Equal(24, Assert.Single(addresses.Offsets));
            Assert.Same(context, addresses.Context);
            Assert.Equal(target == WasmTarget.Wasm64 ? WasmOpcodes.I64Load : WasmOpcodes.I32Load, instruction.Opcode);
            Assert.Equal(WasmInstructionOperand.Memory(target == WasmTarget.Wasm64 ? 3u : 2u, 0), instruction.Operand);
        }
        else
        {
            Assert.Empty(addresses.Offsets);
            Assert.Equal(WasmOpcodes.LocalGet, instruction.Opcode);
            Assert.Equal(WasmInstructionOperand.Unsigned(100), instruction.Operand);
        }
    }

    [Fact]
    public void RejectsMissingInputOrStorageBeforeEmitting()
    {
        var addresses = new RecordingAddresses();
        var emitter = CreateEmitter(WasmTarget.Wasm32, addresses);
        var code = new RecordingInstructionWriter();
        var context = CreateContext(false);
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, context, 0));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(code, null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => emitter.Emit(code, context, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => emitter.Emit(code, context, 1));
        Assert.Throws<InvalidOperationException>(() => emitter.Emit(code, context with { AnchorLocals = [] }, 0));
        Assert.Empty(code.ToInstructions());
        Assert.Empty(addresses.Offsets);
    }

    private static IStackStorageAnchorValueEmitter CreateEmitter(WasmTarget target, IValueFrameAddressEmitter addresses) =>
        new IStackStorageAnchorValueEmitter[]
        { new StackStorageAnchorValueEmitter(new RecordingLayoutProvider(WasmTargetLayout.For(target)), addresses) }[0];

    private static MethodEmissionContext CreateContext(bool shared)
    {
        var anchor = new StackStorageAnchor(new(StackStorageLocationKind.Local, 0),
            CliTypeIdentity.Named(Assembly, "Tests", "Pair", true));
        return CreateMethodEmissionContext() with
        {
            RootMap = new(EntryKey, [], []) { AnchorPlan = new([anchor], []) },
            AnchorLocals = shared ? [] : ImmutableDictionary<int, int>.Empty.Add(0, 100),
            FilterEnvironment = FilterEnvironmentLayout.Empty with
            {
                AnchorOffsets = shared ? ImmutableDictionary<int, int>.Empty.Add(0, 24) : [],
            },
        };
    }

    private sealed class RecordingAddresses : IValueFrameAddressEmitter
    {
        public List<int> Offsets { get; } = [];
        public MethodEmissionContext? Context { get; private set; }

        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, int offset)
        {
            Offsets.Add(offset);
            Context = context;
        }

        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, FilterCapture capture) =>
            throw new InvalidOperationException();
    }
}
