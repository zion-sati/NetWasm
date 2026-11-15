// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Emission.Arrays;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;
using NetWasm.Compiler.Wasm.Emission.Planning;

public sealed class ArrayIndexAdapterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void OmitsGuardsOnlyForProofBoundToCurrentInstructions(WasmTarget target, bool stale)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var exceptions = new RecordingImplicitExceptionEmitter();
        var request = CreateInstructionRequest(CilOperation.LoadArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4]);
        var header = request.Header with { Instructions = [request.Instruction] };
        var proof = new ArrayBoundsProof(header.Instructions, header.Locals,
            header.LocalSignatureTypes, [request.Instruction.Offset]);
        header = header with { ArrayBoundsProof = proof };
        if (stale) header = header with { Instructions = [request.Instruction with { OriginalOffset = 123 }] };
        request = request with { Header = header };
        var code = new RecordingInstructionWriter();
        var actual = CreateAdapter(layouts, exceptions).Adapt(request, code,
            GetStackLocal(request.Context, 0, CliValueKind.ManagedReference, layouts.Target), 1);
        Assert.Equal(GetStackLocal(request.Context, 1, CliValueKind.I4, layouts.Target), actual);
        if (stale)
        {
            Assert.Equal([ManagedExceptionKind.NullReference, ManagedExceptionKind.IndexOutOfRange], exceptions.Kinds);
            Assert.NotEmpty(code.ToInstructions());
        }
        else
        {
            Assert.Empty(exceptions.Kinds);
            Assert.Empty(code.ToInstructions());
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.NativeInt)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I4)]
    public void AdaptUsesOneUnsignedInt32ComparisonAndRetainsTheIndexLocal(
        WasmTarget target,
        CliValueKind indexType)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var exceptions = new RecordingImplicitExceptionEmitter();
        var adapter = CreateAdapter(layouts, exceptions);
        var request = CreateInstructionRequest(
            CilOperation.LoadArrayElement,
            [CliValueKind.ManagedReference, indexType]);
        var code = (RecordingInstructionWriter)GetCodeWriter(request);
        var arrayLocal = GetStackLocal(
            request.Context,
            0,
            CliValueKind.ManagedReference,
            layouts.Target);

        var indexLocal = adapter.Adapt(request, code, arrayLocal, 1);

        Assert.Equal(
            GetStackLocal(request.Context, 1, indexType, layouts.Target),
            indexLocal);
        Assert.Equal(
            [ManagedExceptionKind.NullReference, ManagedExceptionKind.IndexOutOfRange],
            exceptions.Kinds);
        Assert.Equal(
            [
                WasmOpcodes.LocalGet,
                target == WasmTarget.Wasm64
                    ? WasmOpcodes.I64EqualZero
                    : WasmOpcodes.I32EqualZero,
                WasmOpcodes.If,
                WasmOpcodes.End,
                WasmOpcodes.LocalGet,
                WasmOpcodes.LocalGet,
                WasmOpcodes.I32Load,
                WasmOpcodes.I32GreaterThanOrEqualUnsigned,
                WasmOpcodes.If,
                WasmOpcodes.End,
            ],
            code.ToInstructions().Select(instruction => instruction.Opcode));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdaptSkipsOnlyTheNullCheckAnEarlierAccessProved(bool stale)
    {
        var layouts = new RecordingLayoutProvider();
        var exceptions = new RecordingImplicitExceptionEmitter();
        var request = CreateInstructionRequest(CilOperation.LoadArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4]);
        var header = request.Header with { Instructions = [request.Instruction] };
        header = header with
        {
            ArrayBoundsProof = new(header.Instructions, header.Locals, header.LocalSignatureTypes, [])
            {
                NonNullOffsets = [request.Instruction.Offset],
            },
        };
        if (stale) header = header with { Instructions = [request.Instruction with { OriginalOffset = 123 }] };
        request = request with { Header = header };

        CreateAdapter(layouts, exceptions).Adapt(request, new RecordingInstructionWriter(),
            GetStackLocal(request.Context, 0, CliValueKind.ManagedReference, layouts.Target), 1);

        Assert.Equal(stale
            ? [ManagedExceptionKind.NullReference, ManagedExceptionKind.IndexOutOfRange]
            : [ManagedExceptionKind.IndexOutOfRange], exceptions.Kinds);
    }

    [Fact]
    public void AdaptLeavesTheNullCheckToTheLoopEntryThatMadeIt()
    {
        var layouts = new RecordingLayoutProvider();
        var exceptions = new RecordingImplicitExceptionEmitter();
        var slot = new ArrayReferenceSlot(false, 0);
        var request = CreateInstructionRequest(
            CilOperation.LoadArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            context: CreateMethodEmissionContext() with
            {
                ArrayMetadata = new(
                    new ArrayMetadataCachingPlan([slot], ImmutableDictionary<int, ArrayReferenceSlot>.Empty.Add(0, slot))
                    {
                        CheckedAccesses = [0],
                    },
                    ImmutableDictionary<ArrayReferenceSlot, ArrayMetadataSnapshotLocals>.Empty.Add(slot, new(30, 31))),
            });
        var code = (RecordingInstructionWriter)GetCodeWriter(request);

        CreateAdapter(layouts, exceptions).Adapt(request, code,
            GetStackLocal(request.Context, 0, CliValueKind.ManagedReference, layouts.Target), 1);

        Assert.Equal([ManagedExceptionKind.IndexOutOfRange], exceptions.Kinds);
        Assert.Contains(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(30)),
            code.ToInstructions());
    }

    [Fact]
    public void AdaptChecksWasm64NativeIndexBeforeNarrowingIt()
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        var exceptions = new RecordingImplicitExceptionEmitter();
        var adapter = CreateAdapter(layouts, exceptions);
        var request = CreateInstructionRequest(
            CilOperation.LoadArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.NativeInt]);
        var code = (RecordingInstructionWriter)GetCodeWriter(request);
        var arrayLocal = GetStackLocal(
            request.Context,
            0,
            CliValueKind.ManagedReference,
            layouts.Target);

        var indexLocal = adapter.Adapt(request, code, arrayLocal, 1);

        Assert.Equal(request.Context.NumericTemporaryI4, indexLocal);
        Assert.Equal(
            [ManagedExceptionKind.NullReference, ManagedExceptionKind.IndexOutOfRange],
            exceptions.Kinds);
        Assert.Equal(
            [
                WasmOpcodes.LocalGet,
                WasmOpcodes.I64EqualZero,
                WasmOpcodes.If,
                WasmOpcodes.End,
                WasmOpcodes.LocalGet,
                WasmOpcodes.LocalGet,
                WasmOpcodes.I32Load,
                WasmOpcodes.I64ExtendI32Unsigned,
                WasmOpcodes.I64GreaterThanOrEqualUnsigned,
                WasmOpcodes.If,
                WasmOpcodes.End,
                WasmOpcodes.LocalGet,
                WasmOpcodes.I32WrapI64,
                WasmOpcodes.LocalSet,
            ],
            code.ToInstructions().Select(instruction => instruction.Opcode));
    }

    [Fact]
    public void AdaptRejectsInvalidIndexKindBeforeWritingCode()
    {
        var layouts = new RecordingLayoutProvider();
        var exceptions = new RecordingImplicitExceptionEmitter();
        var adapter = CreateAdapter(layouts, exceptions);
        var request = CreateInstructionRequest(
            CilOperation.LoadArrayElement,
            [CliValueKind.ManagedReference, CliValueKind.I8]);
        var code = (RecordingInstructionWriter)GetCodeWriter(request);
        var arrayLocal = GetStackLocal(
            request.Context,
            0,
            CliValueKind.ManagedReference,
            layouts.Target);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            adapter.Adapt(request, code, arrayLocal, 1));

        Assert.Equal(
            "array index must be int32 or native integer",
            exception.Message);
        Assert.Empty(code.ToInstructions());
        Assert.Empty(exceptions.Kinds);
    }

    private static IArrayIndexAdapter CreateAdapter(
        RecordingLayoutProvider layouts,
        IImplicitExceptionEmitter exceptions) => new[]
        {
            new ArrayIndexAdapter(
                layouts,
                new AddressInstructionEmitter(layouts),
                layouts,
                exceptions),
        }.Cast<IArrayIndexAdapter>().Single();

    private static int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type,
        WasmTargetLayout target) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        target);

    private sealed class RecordingImplicitExceptionEmitter :
        IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(
            IWasmInstructionWriter code,
            ManagedExceptionKind kind,
            OptionalFunctionIndex throwFunctionIndex) => Kinds.Add(kind);
    }
}
