// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class DelegateCloneIntrinsicEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, 0)]
    [InlineData(WasmTarget.Wasm32, 2)]
    [InlineData(WasmTarget.Wasm64, 0)]
    [InlineData(WasmTarget.Wasm64, 2)]
    public void EmitsExactTypeAllocationAndCopiesEveryDelegateField(
        WasmTarget target,
        int argumentBase)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var imports = WasmRuntimeImports.CreateCatalog();
        var request = CreateRequest(layouts.Target, argumentBase);
        var writer = new EmitterTestSupport.RecordingInstructionWriter();
        var emitter = CreateEmitter(layouts, imports);
        var delegateType = Assert.Single(request.Instruction.Target.DelegateTypes.Read());
        var objectLayout = layouts.GetObjectLayout(delegateType);

        ((IRuntimeIntrinsicEmitter)emitter).Emit(request, writer);

        var instructions = writer.ToInstructions();
        var source = request.Local(0, CliValueKind.ManagedReference);
        var temporary = request.Instruction.Context.ObjectTemporary;
        Assert.Equal(delegateType, layouts.ObjectIdentityRequest);
        Assert.Single(instructions, instruction => IsCall(
            instruction,
            imports.Resolve(RuntimeImportSymbol.Allocate)));
        Assert.Contains(Local(WasmOpcodes.LocalGet, source), instructions);
        Assert.Contains(Local(WasmOpcodes.LocalSet, temporary), instructions);
        Assert.Contains(Local(WasmOpcodes.LocalSet, source), instructions);
        Assert.Contains(Constant(layouts.Target, objectLayout.Size), instructions);
        Assert.Contains(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(objectLayout.TypeId)), instructions);
        Assert.Equal(2, EmitterTestSupport.CountManagedThrows(instructions));

        AssertCopiedField(
            instructions,
            layouts.Target,
            layouts.DelegateTargetOffset,
            layouts.Target.ObjectReferenceSize);
        AssertCopiedField(
            instructions,
            layouts.Target,
            layouts.DelegateMethodIdOffset,
            sizeof(int));
        AssertCopiedField(
            instructions,
            layouts.Target,
            layouts.DelegateInvocationListOffset,
            layouts.Target.ObjectReferenceSize);
        AssertCopiedField(
            instructions,
            layouts.Target,
            layouts.DelegateInvocationCountOffset,
            sizeof(int));
    }

    [Fact]
    public void EmitsOnlyInvalidCastWhenNoDelegateTypeIsReachable()
    {
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var request = CreateRequest(layouts.Target, argumentBase: 0);
        request = request with
        {
            Call = request.Call with
            {
                Instruction = request.Instruction with
                {
                    Target = request.Instruction.Target with { DelegateTypes = Lookups.Whole<IReadOnlyList<CliTypeIdentity>>([]) },
                },
            },
        };
        var writer = new EmitterTestSupport.RecordingInstructionWriter();

        ((IRuntimeIntrinsicEmitter)CreateEmitter(layouts, imports)).Emit(request, writer);

        var instructions = writer.ToInstructions();
        Assert.DoesNotContain(instructions, instruction => IsCall(
            instruction,
            imports.Resolve(RuntimeImportSymbol.Allocate)));
        Assert.Equal(1, EmitterTestSupport.CountManagedThrows(instructions));
    }

    // Delegate types of one size share a run of type ids, whose clone allocates the source's own type; a type of
    // another size, were there one, starts a run of its own.
    [Fact]
    public void DelegateTypesOfOneSizeShareARun()
    {
        CliTypeIdentity Delegate(string name) => CliTypeIdentity.Named(EmitterTestSupport.Assembly, "Fixtures", name, isValueType: false);
        var first = Delegate("First");
        var second = Delegate("Second");
        var wide = Delegate("Wide");
        var layouts = new RecordingLayoutProvider
        {
            TypeIds = new Dictionary<string, int>
            {
                [first.CanonicalName] = 3,
                [second.CanonicalName] = 4,
                [wide.CanonicalName] = 6,
            },
            ObjectSizes = new Dictionary<string, int> { [wide.CanonicalName] = 24 },
        };
        var imports = WasmRuntimeImports.CreateCatalog();
        var request = CreateRequest(layouts.Target, argumentBase: 0);
        request = request with
        {
            Call = request.Call with
            {
                Instruction = request.Instruction with
                {
                    Target = request.Instruction.Target with
                    {
                        DelegateTypes = Lookups.Whole<IReadOnlyList<CliTypeIdentity>>([wide, second, first]),
                    },
                },
            },
        };
        var writer = new EmitterTestSupport.RecordingInstructionWriter();

        ((IRuntimeIntrinsicEmitter)CreateEmitter(layouts, imports)).Emit(request, writer);

        var instructions = writer.ToInstructions();
        Assert.Equal(2, instructions.Count(instruction => IsCall(instruction, imports.Resolve(RuntimeImportSymbol.Allocate))));
        // The run 3 through 4 is a distance below 2; 6 alone is an equality.
        Assert.Contains(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract), instructions);
        Assert.Contains(Constant(layouts.Target, 16), instructions);
        Assert.Contains(Constant(layouts.Target, 24), instructions);
    }

    // Issue 144: the clone allocates, so DelegateRuntime.Clone must be a
    // safepoint, or a multicast delegate's new invocation list, which only its
    // caller holds, is collected while the clone allocates. Any other method
    // whose call lowers to this code stops the compilation.
    [Fact]
    public void AllocatesOnlyAsTheSafepointDelegateRuntimeCloneIs()
    {
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = SafepointCheckedWriterFactoryTests.Checked(CreateEmitter(layouts, imports), imports);
        var clone = SafepointCheckedWriterFactoryTests.Request("System", "DelegateRuntime", "Clone");
        var unlisted = SafepointCheckedWriterFactoryTests.Request("System", "DelegateRuntime", "CloneUnlisted");

        emitter.Emit(clone with { Call = clone.Call with { Instruction = CreateRequest(layouts.Target, 0).Instruction } },
            new EmitterTestSupport.RecordingInstructionWriter());
        var exception = Assert.Throws<InvalidOperationException>(() => emitter.Emit(
            unlisted with { Call = unlisted.Call with { Instruction = CreateRequest(layouts.Target, 0).Instruction } },
            new EmitterTestSupport.RecordingInstructionWriter()));

        Assert.Contains(nameof(RuntimeImportSymbol.Allocate), exception.Message, StringComparison.Ordinal);
    }

    private static DelegateCloneIntrinsicEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        IRuntimeImportResolver imports) => new(
        layouts,
        layouts,
        layouts,
        imports,
        new ImplicitExceptionEmitter(layouts),
        new KeyedDispatchEmitter());

    private static RuntimeIntrinsicEmissionRequest CreateRequest(
        WasmTargetLayout target,
        int argumentBase)
    {
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.DelegateClone,
            [
                .. Enumerable.Repeat(CliValueKind.I4, argumentBase),
                CliValueKind.ManagedReference,
            ]);
        return request with
        {
            Target = target,
            Call = request.Call with { ArgumentBase = argumentBase, Consumed = 1 },
        };
    }

    private static void AssertCopiedField(
        IReadOnlyCollection<WasmInstruction> instructions,
        WasmTargetLayout target,
        int offset,
        int size)
    {
        var (load, store, alignment) = size == sizeof(long)
            ? (WasmOpcodes.I64Load, WasmOpcodes.I64Store, 3u)
            : (WasmOpcodes.I32Load, WasmOpcodes.I32Store, 2u);
        var operand = WasmInstructionOperand.Memory(alignment, checked((uint)offset));
        Assert.Contains(WasmInstruction.WithOperand(load, operand), instructions);
        Assert.Contains(WasmInstruction.WithOperand(store, operand), instructions);
        if (size == target.ObjectReferenceSize)
        {
            Assert.Contains(WasmInstruction.NoOperand(
                target.UsesMemory64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero),
                instructions);
        }
    }

    private static WasmInstruction Local(byte opcode, int index) =>
        WasmInstruction.WithOperand(
            opcode,
            WasmInstructionOperand.Unsigned(checked((uint)index)));

    private static WasmInstruction Constant(WasmTargetLayout target, int value) =>
        WasmInstruction.WithOperand(
            target.UsesMemory64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant,
            target.UsesMemory64
                ? WasmInstructionOperand.Signed64(value)
                : WasmInstructionOperand.Signed(value));

    private static bool IsCall(WasmInstruction instruction, int index) =>
        instruction.Opcode == WasmOpcodes.Call &&
        instruction.Operand.UnsignedValue == checked((uint)index);
}
