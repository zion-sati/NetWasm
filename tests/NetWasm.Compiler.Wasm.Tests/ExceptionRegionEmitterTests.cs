// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ExceptionRegionEmitterTests
{
    [Theory]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    [InlineData(false, WasmTarget.Wasm64)]
    public void SharedExceptionOccurrencesExecuteWithoutReclaimingDefinitionOwnership(bool ownsParent, WasmTarget target)
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var method = Structure(program, program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(1, CilOperation.Return));
        var body = StructuredSequence.Empty;
        var group = Group(0, 1, [new StructuredExceptionCode(body)],
            [Clause(CilExceptionRegionKind.Catch, TypeKey, body)], [Continuation(42, body)]);
        var context = CreateContext(group) with { RecordOriginalBlockOwnership = ownsParent };
        var visits = new List<bool>();
        var emitter = CreateEmitter(program, layouts, imports);
        for (var occurrence = 0; occurrence < 2; occurrence++)
        {
            emitter.Emit(new RecordingInstructionWriter(), WithGroups(method, group),
                new StructuredExceptionRegion(group.Id, null), context, Lookups.Of(CreateModuleData(group).ExceptionMetadata),
                (sequence, current, _, _, _) =>
                {
                    Assert.Same(body, sequence);
                    visits.Add(current.RecordOriginalBlockOwnership);
                });
        }
        Assert.Equal([ownsParent, ownsParent, ownsParent, false, false, false], visits);
        Assert.Equal([group.Id], context.EmittedExceptionGroups);
    }

    [Theory]
    [InlineData(CilExceptionRegionKind.Finally, WasmTarget.Wasm32)]
    [InlineData(CilExceptionRegionKind.Finally, WasmTarget.Wasm64)]
    [InlineData(CilExceptionRegionKind.Catch, WasmTarget.Wasm32)]
    [InlineData(CilExceptionRegionKind.Catch, WasmTarget.Wasm64)]
    [InlineData(CilExceptionRegionKind.Fault, WasmTarget.Wasm32)]
    [InlineData(CilExceptionRegionKind.Fault, WasmTarget.Wasm64)]
    public void FinallyBodyDoesNotBranchOnTheAlreadyPendingNormalContinuation(
        CilExceptionRegionKind kind,
        WasmTarget target)
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var protectedBody = new StructuredSequence([]);
        var handlerBody = new StructuredSequence([]);
        var continuationBody = new StructuredSequence([]);
        var group = Group(0, 1,
            [new StructuredExceptionCode(protectedBody)],
            [Clause(kind, kind == CilExceptionRegionKind.Catch ? TypeKey : null, handlerBody)],
            [Continuation(42, continuationBody)]);
        var visits = new List<StructuredSequence>();

        CreateEmitter(program, layouts, imports).Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
            WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null),
            CreateContext(group),
            Lookups.Of(CreateModuleData(group).ExceptionMetadata),
            (sequence, context, _, _, leaveDepth) =>
            {
                visits.Add(sequence);
                if (ReferenceEquals(sequence, protectedBody))
                {
                    Assert.Same(group, context.ActiveExceptionGroup);
                    Assert.Equal(0, leaveDepth);
                }
                else if (ReferenceEquals(sequence, handlerBody))
                {
                    Assert.Same(group, context.ActiveExceptionGroup);
                    Assert.Equal(kind == CilExceptionRegionKind.Finally ? (int?)null : 0, leaveDepth);
                }
                else
                {
                    Assert.Same(continuationBody, sequence);
                    Assert.Null(context.ActiveExceptionGroup);
                    Assert.Null(leaveDepth);
                }
            });

        Assert.Equal([protectedBody, handlerBody, continuationBody], visits);
    }

    [Fact]
    public void RegionRejectsAnUndefinedExceptionGroup()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));

        Assert.Throws<InvalidOperationException>(() =>
            CreateEmitter(program, layouts, imports).Emit(
                new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
                method,
                new StructuredExceptionRegion(new(99), null),
                CreateContext(),
                Lookups.Of(CreateModuleData().ExceptionMetadata),
                (_, _, _, _, _) => { }));
    }

    [Fact]
    public void CatchRegionOwnsFrameAndHandlerTransitions()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var emitter = CreateEmitter(program, layouts, imports);
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var clause = Clause(CilExceptionRegionKind.Catch, TypeKey);
        var group = Group(
            0,
            1,
            [new StructuredExceptionCode(StructuredSequence.Empty)],
            [clause],
            []);
        var context = CreateContext(group);
        var module = CreateModuleData(group);
        var outputBuffer = new WasmBinaryBuffer();
        var output = new WasmBinaryWriter(outputBuffer);
        var code = new WasmInstructionWriter(output);
        var sequences = 0;

        emitter.Emit(
            code,
            WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null),
            context,
            Lookups.Of(module.ExceptionMetadata),
            (_, _, _, _, _) => sequences++);

        Assert.Equal(2, sequences);
        var calls = Calls(new WasmBinarySnapshotReader(outputBuffer).Read());
        Assert.Contains(imports.Resolve(RuntimeImportSymbol.ExceptionFrameEnter), calls);
        Assert.Contains(imports.Resolve(RuntimeImportSymbol.ExceptionFrameLeave), calls);
        Assert.Contains(imports.Resolve(RuntimeImportSymbol.ExceptionFrameTargetClause), calls);
        Assert.Contains(imports.Resolve(RuntimeImportSymbol.EndCatch), calls);
    }

    [Fact]
    public void NestedProtectedRegionPropagatesItsContinuationToTheParent()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var nested = Group(
            2,
            1,
            [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Catch, TypeKey)],
            []);
        var group = Group(
            0,
            3,
            [new StructuredNestedExceptionGroup(nested.Id)],
            [Clause(CilExceptionRegionKind.Catch, TypeKey)],
            []);
        var buffer = new WasmBinaryBuffer();

        CreateEmitter(program, layouts, imports).Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(buffer)),
            WithGroups(method, group, nested),
            new StructuredExceptionRegion(group.Id, null),
            CreateContext(group, nested),
            Lookups.Of(CreateModuleData((group, false), (nested, false)).ExceptionMetadata),
            (_, _, _, _, _) => { });

        Assert.Contains(
            WasmOpcodes.BranchIf,
            new WasmBinarySnapshotReader(buffer).Read());
    }

    [Theory]
    [InlineData(CilExceptionRegionKind.Catch)]
    [InlineData(CilExceptionRegionKind.Finally)]
    [InlineData(CilExceptionRegionKind.Fault)]
    public void LandingPadRewindsTheFramesTheExceptionLeftWithoutLeavingTheMethod(
        CilExceptionRegionKind kind)
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var handlerBody = new StructuredSequence([new StructuredLoopContinue()]);
        var group = Group(
            0,
            1,
            [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(kind, kind == CilExceptionRegionKind.Catch ? TypeKey : null, handlerBody)],
            []);
        var code = new RecordingInstructionWriter();
        var emittedSequences = new List<StructuredSequence>();
        var context = CreateContext(group) with
        {
            ValueLayout = new ValueFrameLayout(4, [], [], [], []),
            StackTraceMethodId = 17,
        };

        CreateEmitter(program, layouts, imports).Emit(
            code,
            WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null),
            context,
            Lookups.Of(CreateModuleData(group).ExceptionMetadata),
            (sequence, _, _, _, _) => emittedSequences.Add(sequence));

        Assert.Contains(handlerBody, emittedSequences);
        var calls = code.ToInstructions()
            .Where(instruction => instruction.Opcode == WasmOpcodes.Call)
            .Select(instruction => (int)instruction.Operand.UnsignedValue)
            .ToList();
        var unwind = imports.Resolve(RuntimeImportSymbol.ExceptionFrameUnwind);
        var leave = imports.Resolve(RuntimeImportSymbol.ExceptionFrameLeave);
        if (kind == CilExceptionRegionKind.Catch)
        {
            // The normal exit leaves the exception frame; the landing pad unwinds it.
            Assert.Equal(1, calls.Count(call => call == unwind));
            Assert.Equal(1, calls.Count(call => call == leave));
            Assert.True(calls.IndexOf(unwind) > calls.IndexOf(leave));
        }
        else
        {
            // A block without catch clauses has no frame; its landing pad rewinds to the marks it kept.
            Assert.DoesNotContain(unwind, calls);
            Assert.DoesNotContain(leave, calls);
            Assert.Equal(1, calls.Count(call =>
                call == imports.Resolve(RuntimeImportSymbol.FrameMarksRewind, context.RuntimeImportSelection)));
        }
        // Whatever catches a rethrow rewinds this method's frames, so it never leaves them here; a catch handler that ends
        // normally leaves only its own exception cell.
        var instructions = code.ToInstructions().ToList();
        Assert.Equal(-1, CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.RootFrameLeave)));
        var leaves = instructions.Select((instruction, index) => (instruction, index)).Where(pair =>
            pair.instruction.Opcode == WasmOpcodes.Call && pair.instruction.Operand.UnsignedValue ==
                (uint)imports.Resolve(RuntimeImportSymbol.ExceptionCellLeave, context.RuntimeImportSelection)).ToList();
        Assert.Equal(kind == CilExceptionRegionKind.Catch ? 1 : 0, leaves.Count);
        Assert.All(leaves, pair => Assert.Equal((uint)context.ExceptionCellFrames[group.Id],
            instructions[pair.index - 1].Operand.UnsignedValue));
        Assert.DoesNotContain(imports.Resolve(RuntimeImportSymbol.ValueFrameLeave), calls);
        Assert.DoesNotContain(imports.Resolve(RuntimeImportSymbol.StackTraceFrameLeave), calls);
    }

    // A block without catch clauses registers no exception frame. Where its restore reads marks as it begins, it keeps the
    // root cursor and the value list's head and arena position in its locals before its try_table, and its landing pad
    // returns the arenas to them, once.
    [Theory]
    [InlineData(CilExceptionRegionKind.Finally)]
    [InlineData(CilExceptionRegionKind.Fault)]
    public void ABlockWithoutCatchClausesKeepsItsMarksInLocals(CilExceptionRegionKind kind)
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var group = Group(
            0,
            1,
            [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(kind, null, new StructuredSequence([new StructuredLoopContinue()]))],
            []);
        var marks = new RegionMarkLocals(40, 41, 42);
        var code = new RecordingInstructionWriter();
        var context = CreateContext(group) with
        {
            RegionMarkLocals = ImmutableDictionary<StructuredExceptionGroupId, RegionMarkLocals>.Empty.Add(group.Id, marks),
        };

        CreateEmitter(program, layouts, imports).Emit(
            code,
            WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null),
            context,
            Lookups.Of(CreateModuleData(group).ExceptionMetadata),
            (_, _, _, _, _) => { });

        var instructions = code.ToInstructions().ToList();
        var calls = instructions.Where(instruction => instruction.Opcode == WasmOpcodes.Call)
            .Select(instruction => (int)instruction.Operand.UnsignedValue).ToList();
        foreach (var frameStep in new[]
                 {
                     RuntimeImportSymbol.ExceptionFrameEnter, RuntimeImportSymbol.ExceptionFrameLeave,
                     RuntimeImportSymbol.ExceptionFrameUnwind, RuntimeImportSymbol.ExceptionFrameTargetClause,
                 })
        {
            Assert.DoesNotContain(imports.Resolve(frameStep), calls);
        }
        var reads = new[]
        {
            RuntimeImportSymbol.FrameMarkRoot, RuntimeImportSymbol.FrameMarkValueCursor,
        };
        var tryTable = instructions.FindIndex(instruction => instruction.Opcode == WasmOpcodes.TryTable);
        for (var index = 0; index < reads.Length; index++)
        {
            var read = instructions.FindIndex(instruction =>
                instruction.Opcode == WasmOpcodes.Call &&
                instruction.Operand.UnsignedValue == (uint)imports.Resolve(reads[index], context.RuntimeImportSelection));
            Assert.InRange(read, 0, tryTable);
            Assert.Equal(WasmOpcodes.LocalSet, instructions[read + 1].Opcode);
            Assert.Equal((uint)(40 + index), instructions[read + 1].Operand.UnsignedValue);
        }
        var rewind = imports.Resolve(RuntimeImportSymbol.FrameMarksRewind, context.RuntimeImportSelection);
        Assert.Equal(1, calls.Count(call => call == rewind));
        var rewindAt = instructions.FindIndex(instruction =>
            instruction.Opcode == WasmOpcodes.Call && instruction.Operand.UnsignedValue == (uint)rewind);
        Assert.True(rewindAt > tryTable);
        // The root and value cursors it read, and no stack-trace depth.
        Assert.Equal(
            [(WasmOpcodes.LocalGet, 40L), (WasmOpcodes.LocalGet, 41L), (WasmOpcodes.I32Constant, -1L)],
            instructions[(rewindAt - 3)..rewindAt].Select(instruction => (instruction.Opcode,
                instruction.Opcode == WasmOpcodes.LocalGet ? (long)instruction.Operand.UnsignedValue : instruction.Operand.SignedValue)));
    }

    // A method that enters its root frame lazily keeps the frame local as a block begins, entering the frame first when
    // the block is eager, and restores it after the landing pad's unwind.
    [Theory]
    [InlineData(CilExceptionRegionKind.Catch, false)]
    [InlineData(CilExceptionRegionKind.Catch, true)]
    [InlineData(CilExceptionRegionKind.Finally, false)]
    public void ALazyFrameLocalIsSavedAsABlockBeginsAndRestoredAtItsPad(CilExceptionRegionKind kind, bool eager)
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(program, program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(1, CilOperation.Return));
        var group = Group(0, 1, [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(kind, kind == CilExceptionRegionKind.Catch ? TypeKey : null, new StructuredSequence([new StructuredLoopContinue()]))],
            []);
        var context = CreateContext(group) with
        {
            RootMap = new MethodRootMap(EntryKey,
                ImmutableDictionary<RootSource, int>.Empty.Add(new(RootSourceKind.Local, 0), 0), []),
            LazyRootFrame = new(eager ? [group.Id] : [], []),
            RootFrameSaves = ImmutableDictionary<StructuredExceptionGroupId, int>.Empty.Add(group.Id, 50),
            RegionMarkLocals = ImmutableDictionary<StructuredExceptionGroupId, RegionMarkLocals>.Empty
                .Add(group.Id, new(60, 61, 62)),
        };
        var code = new RecordingInstructionWriter();

        CreateEmitter(program, layouts, imports).Emit(code, WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null), context,
            Lookups.Of(CreateModuleData(group).ExceptionMetadata), (_, _, _, _, _) => { });

        var instructions = code.ToInstructions().ToList();
        int Index(byte opcode, uint operand) => instructions.FindIndex(instruction =>
            instruction.Opcode == opcode && instruction.Operand.UnsignedValue == operand);
        var save = Index(WasmOpcodes.LocalSet, 50);
        var tryTable = instructions.FindIndex(instruction => instruction.Opcode == WasmOpcodes.TryTable);
        var restore = Index(WasmOpcodes.LocalGet, 50);
        var rewound = kind == CilExceptionRegionKind.Finally
            ? CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.FrameMarksRewind, context.RuntimeImportSelection))
            : CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionFrameUnwind));
        Assert.InRange(save, 1, tryTable);
        Assert.Equal((WasmOpcodes.LocalGet, (uint)context.RootFrame), (instructions[save - 1].Opcode, instructions[save - 1].Operand.UnsignedValue));
        Assert.True(restore > rewound && rewound > tryTable);
        Assert.Equal((WasmOpcodes.LocalSet, (uint)context.RootFrame), (instructions[restore + 1].Opcode, instructions[restore + 1].Operand.UnsignedValue));
        var enter = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.RootFrameEnter));
        Assert.Equal(eager, enter >= 0 && enter < save);
    }

    // A catch without filters registers its frame only while a frame with filters is registered: it keeps its marks
    // before its try_table, asks the runtime, and registers only in that branch; its landing pad unwinds the frame or
    // rewinds to the marks, takes the selected clause or selects its own, and ends the catch either way. A catch with a
    // filter registers unconditionally and never selects its own clause.
    [Theory]
    [InlineData(CilExceptionRegionKind.Catch)]
    [InlineData(CilExceptionRegionKind.Filter)]
    public void ACatchWithoutFiltersRegistersOnlyWhileAFilterFrameIs(CilExceptionRegionKind kind)
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(program, program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(1, CilOperation.Return));
        var group = Group(0, 1, [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(kind, kind == CilExceptionRegionKind.Catch ? TypeKey : null)], []);
        var context = CreateContext(group);
        var code = new RecordingInstructionWriter();

        CreateEmitter(program, layouts, imports).Emit(code, WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null), context,
            Lookups.Of(CreateModuleData(group).ExceptionMetadata), (_, _, _, _, _) => { });

        var instructions = code.ToInstructions().ToList();
        var live = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionFilterFramesLive, context.RuntimeImportSelection));
        var enter = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionFrameEnter));
        var select = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionCatchSelect, context.RuntimeImportSelection));
        var endCatch = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.EndCatch));
        var tryTable = instructions.FindIndex(instruction => instruction.Opcode == WasmOpcodes.TryTable);
        Assert.InRange(enter, 0, tryTable);
        Assert.InRange(endCatch, tryTable, instructions.Count);
        if (kind == CilExceptionRegionKind.Filter)
        {
            Assert.Equal(-1, live);
            Assert.Equal(-1, select);
            Assert.Equal(-1, CallIndex(instructions,
                imports.Resolve(RuntimeImportSymbol.FrameMarkRoot, context.RuntimeImportSelection)));
            return;
        }
        var marks = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.FrameMarkRoot, context.RuntimeImportSelection));
        Assert.True(marks >= 0 && marks < live && live < enter);
        Assert.Equal(WasmOpcodes.If, instructions[live + 1].Opcode);
        var unwind = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionFrameUnwind));
        var rewind = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.FrameMarksRewind, context.RuntimeImportSelection));
        var target = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionFrameTargetClause));
        Assert.True(tryTable < unwind && unwind < rewind && rewind < target && target < select && select < endCatch);
        // The selection reads the exception the landing pad caught, then the group's clauses.
        Assert.Equal((WasmOpcodes.LocalGet, (uint)context.ExceptionTemporary),
            (instructions[select - 3].Opcode, instructions[select - 3].Operand.UnsignedValue));
    }

    // In a program that reaches no filter, no first pass runs one, so a catch without filters never registers: it keeps
    // its marks before its try_table and asks the runtime nothing; its landing pad rewinds to the marks, selects its own
    // clause and ends the catch.
    [Fact]
    public void InAProgramThatReachesNoFilterACatchNeverRegisters()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(program, program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(1, CilOperation.Return));
        var group = Group(0, 1, [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Catch, TypeKey)], []);
        var context = CreateContext(group) with { ReachesFilters = false };
        var code = new RecordingInstructionWriter();

        CreateEmitter(program, layouts, imports).Emit(code, WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null), context,
            Lookups.Of(CreateModuleData(group).ExceptionMetadata), (_, _, _, _, _) => { });

        var instructions = code.ToInstructions().ToList();
        var tryTable = instructions.FindIndex(instruction => instruction.Opcode == WasmOpcodes.TryTable);
        var marks = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.FrameMarkRoot, context.RuntimeImportSelection));
        var rewind = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.FrameMarksRewind, context.RuntimeImportSelection));
        var select = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionCatchSelect, context.RuntimeImportSelection));
        var endCatch = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.EndCatch));
        Assert.Equal(-1, CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionFilterFramesLive, context.RuntimeImportSelection)));
        Assert.Equal(-1, CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionFrameEnter)));
        Assert.Equal(-1, CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionFrameLeave)));
        Assert.Equal(-1, CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionFrameUnwind)));
        Assert.Equal(-1, CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionFrameTargetClause)));
        Assert.True(marks >= 0 && marks < tryTable && tryTable < rewind && rewind < select && select < endCatch);
    }

    // A block that keeps its marks in locals has no frame to select a clause in or end a catch of.
    [Fact]
    public void ABlockWithoutCatchClausesHasNoClauseStep()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var group = Group(0, 1, [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Finally, null)], []);
        var context = CreateContext(group) with
        {
            RegionMarkLocals = ImmutableDictionary<StructuredExceptionGroupId, RegionMarkLocals>.Empty
                .Add(group.Id, new RegionMarkLocals(40, 41, 42)),
        };
        var frames = TestExceptionFrames.Steps(layouts, imports);

        foreach (var step in new[] { ExceptionFrameStep.TargetClause, ExceptionFrameStep.EndCatch })
        {
            Assert.Throws<InvalidOperationException>(() => frames.Emit(new RecordingInstructionWriter(), step,
                WithGroups(method, group), group, context, Lookups.Of(CreateModuleData(group).ExceptionMetadata)));
        }
    }

    [Fact]
    public void MixedCatchAndFinallyFailsWithManagedCompilerDiagnostic()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var group = Group(
            0,
            1,
            [],
            [
                Clause(CilExceptionRegionKind.Catch, TypeKey),
                Clause(CilExceptionRegionKind.Finally, null),
            ],
            []);

        var exception = Assert.Throws<CompilerException>(() =>
            CreateEmitter(program, layouts, imports).Emit(
                new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
                WithGroups(method, group),
                new StructuredExceptionRegion(group.Id, null),
                CreateContext(group),
                Lookups.Of(CreateModuleData(group).ExceptionMetadata),
                (_, _, _, _, _) => { }));

        Assert.Equal(DiagnosticCode.UnsupportedCil, exception.Diagnostic.Code);
        Assert.Contains("typed catches or one finally clause", exception.Message);
    }

    [Fact]
    public void SelectedContinuationCanBranchToTheSurroundingLoop()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var group = Group(
            0,
            1,
            [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Catch, TypeKey)],
            [
                Continuation(
                    42,
                    new StructuredSequence([new StructuredLoopContinue()])),
            ]);
        int? capturedBreakDepth = null;
        int? capturedContinueDepth = null;

        CreateEmitter(program, layouts, imports).Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
            WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null),
            CreateContext(group),
            Lookups.Of(CreateModuleData(group).ExceptionMetadata),
            (sequence, _, loopBreakDepth, loopContinueDepth, _) =>
            {
                if (sequence.Regions.Any(region => region is StructuredLoopContinue))
                {
                    capturedBreakDepth = loopBreakDepth;
                    capturedContinueDepth = loopContinueDepth;
                }
            },
            loopBreakDepth: 2,
            loopContinueDepth: 3);

        Assert.Equal(3, capturedBreakDepth);
        Assert.Equal(4, capturedContinueDepth);
    }

    [Fact]
    public void SelectedContinuationCanReenterItsSurroundingDispatcher()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var continuationBody = new StructuredSequence([new StructuredLoopContinue()]);
        var group = Group(
            0,
            1,
            [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Catch, TypeKey)],
            [Continuation(42, continuationBody)]);
        var region = new StructuredExceptionRegion(group.Id, null)
        {
            DispatcherContinuations = [new StructuredContinuationId(0)],
        };
        var context = CreateContext(group);
        var buffer = new WasmBinaryBuffer();
        var emittedContinuationBody = false;

        CreateEmitter(program, layouts, imports).Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(buffer)),
            WithGroups(method, group),
            region,
            context,
            Lookups.Of(CreateModuleData(group).ExceptionMetadata),
            (sequence, _, _, _, _) =>
                emittedContinuationBody |= ReferenceEquals(sequence, continuationBody),
            dispatcherContinueDepth: 2);

        Assert.False(emittedContinuationBody);
        var bytes = new WasmBinarySnapshotReader(buffer).Read();
        Assert.Contains(WasmOpcodes.I32Or, bytes);
        Assert.True(bytes.AsSpan().IndexOf(new byte[]
        {
            WasmOpcodes.I32Constant,
            42,
            WasmOpcodes.LocalSet,
            (byte)context.DispatcherProgramCounter,
            WasmOpcodes.Branch,
            3,
        }) >= 0);
    }

    [Fact]
    public void DispatcherContinuationRequiresItsSurroundingDispatcherDepth()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var group = Group(
            0,
            1,
            [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Catch, TypeKey)],
            [Continuation(42, StructuredSequence.Empty)]);
        var region = new StructuredExceptionRegion(group.Id, null)
        {
            DispatcherContinuations = [new StructuredContinuationId(0)],
        };

        Assert.Throws<InvalidOperationException>(() =>
            CreateEmitter(program, layouts, imports).Emit(
                new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
                WithGroups(method, group),
                region,
                CreateContext(group),
                Lookups.Of(CreateModuleData(group).ExceptionMetadata),
                (_, _, _, _, _) => { }));
    }

    [Fact]
    public void FinallyEntersItsExceptionCellOnlyAtItsLandingPad()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var group = Group(
            0,
            1,
            [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Finally, null)],
            []);
        var context = CreateContext(group);
        var code = new RecordingInstructionWriter();

        CreateEmitter(program, layouts, imports).Emit(
            code,
            WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null),
            context,
            Lookups.Of(CreateModuleData(group).ExceptionMetadata),
            (_, _, _, _, _) => { });

        var instructions = code.ToInstructions().ToList();
        // The normal path starts with no cell; the landing pad has the runtime enter one, holding the exception, after its
        // unwind, and the finally rethrows it from there when the cell exists. A rethrow abandons the cell to whatever
        // catches it.
        Assert.Equal(0, instructions.Count(instruction => instruction.Opcode == WasmOpcodes.I32Store));
        Assert.Equal(1, instructions.Count(instruction => instruction.Opcode == WasmOpcodes.I32Load));
        Assert.All(instructions.Where(instruction => instruction.Opcode is WasmOpcodes.I32Store or WasmOpcodes.I32Load),
            instruction => Assert.Equal(0u, instruction.Operand.Offset));
        var tryTable = instructions.FindIndex(instruction => instruction.Opcode == WasmOpcodes.TryTable);
        var reset = instructions.FindIndex(instruction =>
            instruction.Opcode == WasmOpcodes.LocalSet && instruction.Operand.UnsignedValue == 40u);
        Assert.InRange(reset, 1, tryTable);
        Assert.Equal(WasmOpcodes.I32Constant, instructions[reset - 1].Opcode);
        var enter = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionCellEnter, context.RuntimeImportSelection));
        Assert.True(enter > tryTable);
        Assert.True(enter > CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionFrameUnwind)));
        Assert.Equal((uint)context.ExceptionTemporary, instructions[enter - 1].Operand.UnsignedValue);
        Assert.Equal((WasmOpcodes.LocalSet, 40u), (instructions[enter + 1].Opcode, instructions[enter + 1].Operand.UnsignedValue));
        Assert.Equal(-1, CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionCellLeave, context.RuntimeImportSelection)));
        Assert.Equal(-1, CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.RootFrameEnter)));
    }

    // A catch's landing pad unwinds, then enters the cell that holds the exception while its handler runs; a handler
    // that ends normally leaves the cell, and an exception no clause takes is thrown on from it.
    [Fact]
    public void CatchEntersItsExceptionCellAfterTheUnwindAndLeavesItAfterItsHandler()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var handler = new StructuredSequence([new StructuredLoopContinue()]);
        var group = Group(0, 1, [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Catch, TypeKey, handler)], []);
        var code = new RecordingInstructionWriter();
        int? handlerCell = null;

        var context = CreateContext(group);
        CreateEmitter(program, layouts, imports).Emit(code, WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null), context,
            Lookups.Of(CreateModuleData(group).ExceptionMetadata),
            (sequence, actual, _, _, _) =>
            {
                if (ReferenceEquals(sequence, handler)) handlerCell = actual.ActiveCatchCellFrame;
            });

        var instructions = code.ToInstructions().ToList();
        Assert.Equal(40, handlerCell);
        var unwind = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionFrameUnwind));
        var enter = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionCellEnter, context.RuntimeImportSelection));
        var leave = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.ExceptionCellLeave, context.RuntimeImportSelection));
        Assert.True(unwind >= 0 && enter > unwind && leave > enter);
        Assert.Equal(WasmOpcodes.LocalSet, instructions[enter + 1].Opcode);
        Assert.Equal(40u, instructions[enter + 1].Operand.UnsignedValue);
        Assert.Equal(40u, instructions[leave - 1].Operand.UnsignedValue);
        Assert.Contains(instructions.Select((instruction, index) => (instruction, index)), pair =>
            pair.instruction.Opcode == WasmOpcodes.Throw && instructions[pair.index - 2].Operand.UnsignedValue == 40u);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CilExceptionRegionKind.Catch)]
    [InlineData(WasmTarget.Wasm64, CilExceptionRegionKind.Catch)]
    [InlineData(WasmTarget.Wasm32, CilExceptionRegionKind.Finally)]
    [InlineData(WasmTarget.Wasm64, CilExceptionRegionKind.Finally)]
    [InlineData(WasmTarget.Wasm32, CilExceptionRegionKind.Fault)]
    [InlineData(WasmTarget.Wasm64, CilExceptionRegionKind.Fault)]
    public void NestedHandlerOwnsItsRootAndContinuationRetainsTheEnclosingCatch(
        WasmTarget target, CilExceptionRegionKind kind)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var imports = WasmRuntimeImports.CreateCatalog();
        var method = Structure(program, program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(1, CilOperation.Return));
        var handler = new StructuredSequence([new StructuredLoopContinue()]);
        var continuation = new StructuredSequence([new StructuredLoopBreak()]);
        var group = Group(1, 1, [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(kind, kind == CilExceptionRegionKind.Catch ? TypeKey : null, handler)],
            [Continuation(20, continuation)]);
        var context = CreateContext(group) with
        {
            ActiveCatchCellFrame = 5,
            ExceptionCellFrames = ImmutableDictionary<StructuredExceptionGroupId, int>.Empty.Add(group.Id, 46),
        };
        var code = new RecordingInstructionWriter();
        var handlers = 0;
        var continuations = 0;
        CreateEmitter(program, layouts, imports).Emit(code, WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null), context, Lookups.Of(CreateModuleData(group).ExceptionMetadata),
            (sequence, actual, _, _, _) =>
            {
                if (ReferenceEquals(sequence, handler))
                {
                    handlers++;
                    Assert.Equal(kind == CilExceptionRegionKind.Catch ? 46 : 5, actual.ActiveCatchCellFrame);
                }
                if (ReferenceEquals(sequence, continuation))
                {
                    continuations++;
                    Assert.Equal(5, actual.ActiveCatchCellFrame);
                }
            });
        Assert.Equal(1, handlers);
        Assert.Equal(1, continuations);
        var memory = code.ToInstructions().Where(instruction => instruction.Opcode is
            WasmOpcodes.I32Load or WasmOpcodes.I64Load or WasmOpcodes.I32Store or WasmOpcodes.I64Store).ToArray();
        Assert.NotEmpty(memory);
        // The handler's exception goes through its own cell's one slot.
        Assert.All(memory, instruction => Assert.Equal(0u, instruction.Operand.Offset));
    }

    [Fact]
    public void FinallyTestsItsPendingExceptionWithAMemory64Comparison()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var group = Group(
            0,
            1,
            [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Finally, null)],
            []);
        var code = new RecordingInstructionWriter();

        CreateEmitter(program, layouts, imports).Emit(
            code,
            WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null),
            CreateContext(group),
            Lookups.Of(CreateModuleData(group).ExceptionMetadata),
            (_, _, _, _, _) => { });

        Assert.Contains(code.ToInstructions(), instruction => instruction.Opcode == WasmOpcodes.I64EqualZero);
    }

    [Fact]
    public void FilteredCatchBindsTheValueFrameEnvironment()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var group = Group(
            0,
            1,
            [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Filter, null)],
            []);
        var buffer = new WasmBinaryBuffer();

        CreateEmitter(program, layouts, imports).Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(buffer)),
            WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null),
            CreateContext(group),
            Lookups.Of(CreateModuleData((group, true)).ExceptionMetadata),
            (_, _, _, _, _) => { });

        Assert.Contains(
            imports.Resolve(RuntimeImportSymbol.ExceptionFrameSetEnvironment),
            Calls(new WasmBinarySnapshotReader(buffer).Read()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void ContinuationDispatcherSelectsDefaultAndExplicitTargets(int? fallthroughContinuation)
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var firstTarget = Block(10);
        var secondTarget = Block(20);
        var group = Group(
            0,
            1,
            [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Catch, TypeKey)],
            [
                Continuation(10, StructuredSequence.Empty),
                Continuation(20, StructuredSequence.Empty),
            ],
            new StructuredDispatcher(
                null,
                [
                    new StructuredDispatcherBlock(firstTarget, null, null),
                    new StructuredDispatcherBlock(secondTarget, null, null),
                ],
                []));
        var buffer = new WasmBinaryBuffer();

        CreateEmitter(program, layouts, imports).Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(buffer)),
            WithGroups(method, group),
            new StructuredExceptionRegion(
                group.Id,
                fallthroughContinuation is null
                    ? null
                    : new(fallthroughContinuation.Value)),
            CreateContext(group),
            Lookups.Of(CreateModuleData(group).ExceptionMetadata),
            (_, _, _, _, _) => { });

        var bytes = new WasmBinarySnapshotReader(buffer).Read();
        Assert.Contains(WasmOpcodes.I32Equal, bytes);
        Assert.Contains(WasmOpcodes.LocalSet, bytes);
    }

    [Fact]
    public void NonDispatcherContinuationSkipsItsFallthroughTarget()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var group = Group(
            0,
            1,
            [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Catch, TypeKey)],
            [
                Continuation(10, StructuredSequence.Empty),
                Continuation(20, StructuredSequence.Empty),
            ]);

        CreateEmitter(program, layouts, imports).Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
            WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, new(1)),
            CreateContext(group),
            Lookups.Of(CreateModuleData(group).ExceptionMetadata),
            (_, _, _, _, _) => { });
    }

    [Fact]
    public void ContinuationCanPropagateToAnActiveParentGroup()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var parent = Group(
            0,
            1,
            [],
            [Clause(CilExceptionRegionKind.Catch, TypeKey)],
            [Continuation(42, StructuredSequence.Empty)]);
        var group = Group(
            2,
            1,
            [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Catch, TypeKey)],
            [Continuation(42, StructuredSequence.Empty)]);

        CreateEmitter(program, layouts, imports).Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
            WithGroups(method, group, parent),
            new StructuredExceptionRegion(group.Id, null),
            CreateContext(group, parent) with { ActiveExceptionGroup = parent },
            Lookups.Of(CreateModuleData(group).ExceptionMetadata),
            (_, _, _, _, _) => { });
    }

    // A leave in a continuation body to an enclosing group's continuation must end that
    // body: code after the leave in the same body would otherwise still run.
    [Theory]
    [InlineData(CilExceptionRegionKind.Catch, null, null)]
    [InlineData(CilExceptionRegionKind.Catch, 3, 4)]
    [InlineData(CilExceptionRegionKind.Finally, null, null)]
    [InlineData(CilExceptionRegionKind.Finally, 3, 4)]
    [InlineData(CilExceptionRegionKind.Fault, null, null)]
    [InlineData(CilExceptionRegionKind.Fault, 3, 4)]
    public void ContinuationBodyLeavesTheEnclosingCodeInsideItsSelector(
        CilExceptionRegionKind kind,
        int? enclosingLeaveDepth,
        int? expectedLeaveDepth)
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var continuation = new StructuredSequence([new StructuredLoopBreak()]);
        var group = Group(
            0,
            1,
            [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(kind, kind == CilExceptionRegionKind.Catch ? TypeKey : null)],
            [Continuation(42, continuation)]);
        var emitted = 0;
        int? capturedLeaveDepth = -1;

        CreateEmitter(program, layouts, imports).Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
            WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null),
            CreateContext(group),
            Lookups.Of(CreateModuleData(group).ExceptionMetadata),
            (sequence, _, _, _, leaveDepth) =>
            {
                if (ReferenceEquals(sequence, continuation))
                {
                    emitted++;
                    capturedLeaveDepth = leaveDepth;
                }
            },
            enclosingLeaveDepth: enclosingLeaveDepth);

        Assert.Equal(1, emitted);
        Assert.Equal(expectedLeaveDepth, capturedLeaveDepth);
    }

    [Fact]
    public void NestedGroupContinuationLeavesTheParentProtectedCode()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var continuation = new StructuredSequence([new StructuredLoopBreak()]);
        var nested = Group(
            2,
            1,
            [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Finally, null)],
            [Continuation(42, continuation)]);
        var parent = Group(
            0,
            3,
            [new StructuredNestedExceptionGroup(nested.Id)],
            [Clause(CilExceptionRegionKind.Catch, TypeKey)],
            [Continuation(99, StructuredSequence.Empty)]);
        StructuredExceptionGroupId? activeGroup = null;
        int? capturedLeaveDepth = -1;

        CreateEmitter(program, layouts, imports).Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
            WithGroups(method, parent, nested),
            new StructuredExceptionRegion(parent.Id, null),
            CreateContext(parent, nested),
            Lookups.Of(CreateModuleData((parent, false), (nested, false)).ExceptionMetadata),
            (sequence, context, _, _, leaveDepth) =>
            {
                if (ReferenceEquals(sequence, continuation))
                {
                    activeGroup = context.ActiveExceptionGroup?.Id;
                    capturedLeaveDepth = leaveDepth;
                }
            });

        Assert.Equal(parent.Id, activeGroup);
        Assert.Equal(1, capturedLeaveDepth);
    }

    [Fact]
    public void ContinuationFallsThroughWhenTheActiveParentHasNoMatchingTarget()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var parent = Group(
            0,
            1,
            [],
            [Clause(CilExceptionRegionKind.Catch, TypeKey)],
            [Continuation(99, StructuredSequence.Empty)]);
        var group = Group(
            2,
            1,
            [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Catch, TypeKey)],
            [Continuation(42, StructuredSequence.Empty)]);

        CreateEmitter(program, layouts, imports).Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
            WithGroups(method, group, parent),
            new StructuredExceptionRegion(group.Id, null),
            CreateContext(group, parent) with { ActiveExceptionGroup = parent },
            Lookups.Of(CreateModuleData(group).ExceptionMetadata),
            (_, _, _, _, _) => { });
    }

    [Fact]
    public void RejectsUnknownProtectedPartThroughItsContract()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var group = Group(
            0,
            1,
            [new UnrelatedPart()],
            [Clause(CilExceptionRegionKind.Catch, TypeKey)],
            []);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateEmitter(program, layouts, imports).Emit(
                new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
                WithGroups(method, group),
                new StructuredExceptionRegion(group.Id, null),
                CreateContext(group),
                Lookups.Of(CreateModuleData(group).ExceptionMetadata),
                (_, _, _, _, _) => { }));

        Assert.Contains(nameof(UnrelatedPart), exception.Message);
    }

    // A method's own frames say where it stood at a block when it enters them in its prologue on every path: an eager
    // root frame and a value frame, with no localloc block and no block inside another's handler. With no frame in an
    // arena, the read after the prologue serves a block in a loop and several blocks at once, and a method's only such
    // block outside every loop reads as it begins (it runs at most once a call, and a path that skips it reads nothing);
    // a lazy root frame, a localloc block or a handler the block is in reads a mark as the block begins; stack-trace depth
    // is read after the prologue when the program records traces.
    [Fact]
    public void ALandingPadRestoresFromTheMethodsOwnFramesWhereTheySay()
    {
        var program = new FakeProgram();
        var plain = Structure(program, program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(1, CilOperation.Return));
        var looped = Structure(program, program.GetMethod(EntryKey),
            I(0, CilOperation.Nop), I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(2, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(0)),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(4, CilOperation.Return));
        var allocating = Structure(program, program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(8)), I(1, CilOperation.LocalAllocate),
            I(2, CilOperation.Pop), I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(4, CilOperation.Return));
        var group = Group(0, 1, [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Finally, null)], []);
        var outer = Group(5, 1, [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Catch, TypeKey) with { HandlerOffset = 0, HandlerLength = 5 }], []);
        var framed = CreateContext(group) with
        {
            RootMap = new MethodRootMap(EntryKey, ImmutableDictionary<RootSource, int>.Empty.Add(new(RootSourceKind.Local, 0), 0), []),
            ValueLayout = new ValueFrameLayout(16, [], [], [], []),
        };
        var planner = new LandingPadRestorePlanner();

        Assert.Equal(new LandingPadRestore(ArenaRestoreSource.OwnedFrame, ArenaRestoreSource.OwnedFrame, ArenaRestoreSource.None),
            planner.Plan(WithGroups(plain, group), framed)[group.Id]);
        var tracing = CreateContext(group) with
        {
            RuntimeImportSelection = CreateContext(group).RuntimeImportSelection with { IncludeStackTrace = true },
        };
        Assert.Equal(new LandingPadRestore(ArenaRestoreSource.BlockMark, ArenaRestoreSource.BlockMark, ArenaRestoreSource.EntryMark),
            planner.Plan(WithGroups(plain, group), tracing)[group.Id]);
        Assert.Equal(new LandingPadRestore(ArenaRestoreSource.EntryMark, ArenaRestoreSource.EntryMark, ArenaRestoreSource.EntryMark),
            planner.Plan(WithGroups(looped, group), tracing)[group.Id]);
        var second = Group(1, 1, [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Finally, null) with { HandlerOffset = 1, HandlerLength = 1 }], []);
        var both = planner.Plan(WithGroups(plain, group, second), tracing);
        Assert.All([both[group.Id], both[second.Id]], plan => Assert.Equal(
            new LandingPadRestore(ArenaRestoreSource.EntryMark, ArenaRestoreSource.EntryMark, ArenaRestoreSource.EntryMark), plan));
        Assert.Equal(ArenaRestoreSource.BlockMark, planner.Plan(WithGroups(plain, group), framed with
        {
            LazyRootFrame = new([], []),
        })[group.Id].Root);
        Assert.Equal(ArenaRestoreSource.BlockMark, planner.Plan(WithGroups(allocating, group), framed)[group.Id].Value);
        var nested = planner.Plan(WithGroups(plain, group, outer), framed);
        Assert.Equal(ArenaRestoreSource.BlockMark, nested[group.Id].Root);
        Assert.Equal(ArenaRestoreSource.OwnedFrame, nested[outer.Id].Root);
    }

    // A block whose arenas the method's own frames restore reads nothing as it begins: its pad passes the root frame's
    // end, the value frame and its size, and the stack-trace depth read after the prologue.
    [Fact]
    public void ABlockRestoredFromOwnedFramesReadsNothingAsItBegins()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var method = Structure(program, program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(1, CilOperation.Return));
        var group = Group(0, 1, [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Finally, null)], []);
        var context = CreateContext(group) with
        {
            RootMap = new MethodRootMap(EntryKey, ImmutableDictionary<RootSource, int>.Empty
                .Add(new(RootSourceKind.Local, 0), 0).Add(new(RootSourceKind.Local, 1), 1), []),
            ValueLayout = new ValueFrameLayout(16, [], [], [], []),
            LandingPadRestores = ImmutableDictionary<StructuredExceptionGroupId, LandingPadRestore>.Empty.Add(group.Id,
                new(ArenaRestoreSource.OwnedFrame, ArenaRestoreSource.OwnedFrame, ArenaRestoreSource.EntryMark)),
        };
        var code = new RecordingInstructionWriter();

        CreateEmitter(program, layouts, imports).Emit(code, WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null), context,
            Lookups.Of(CreateModuleData(group).ExceptionMetadata), (_, _, _, _, _) => { });

        var instructions = code.ToInstructions().ToList();
        var tryTable = instructions.FindIndex(instruction => instruction.Opcode == WasmOpcodes.TryTable);
        var unwind = CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.FramesUnwind, context.RuntimeImportSelection));
        Assert.Equal(-1, CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.FrameMarkRoot, context.RuntimeImportSelection)));
        Assert.Equal(-1, CallIndex(instructions, imports.Resolve(RuntimeImportSymbol.FrameMarkValueCursor, context.RuntimeImportSelection)));
        Assert.True(unwind > tryTable);
        Assert.Equal(
            [(WasmOpcodes.LocalGet, (long)context.RootFrame), (WasmOpcodes.I32Constant, 2L * layouts.Target.ObjectReferenceSize),
             (WasmOpcodes.I32Add, 0L), (WasmOpcodes.LocalGet, context.ValueFrame), (WasmOpcodes.I32Constant, 16L),
             (WasmOpcodes.LocalGet, 92L)],
            instructions[(unwind - 6)..unwind].Select(instruction => (instruction.Opcode,
                instruction.Opcode == WasmOpcodes.LocalGet ? (long)instruction.Operand.UnsignedValue : instruction.Operand.SignedValue)));
    }

    // After the prologue a method reads only the arenas some block restores from that read.
    [Fact]
    public void AMethodReadsAfterItsPrologueOnlyTheMarksItsBlocksUse()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = new LandingPadRestoreEmitter(new RecordingLayoutProvider(), imports);
        var group = Group(0, 1, [new StructuredExceptionCode(StructuredSequence.Empty)],
            [Clause(CilExceptionRegionKind.Finally, null)], []);
        var context = CreateContext(group) with
        {
            LandingPadRestores = ImmutableDictionary<StructuredExceptionGroupId, LandingPadRestore>.Empty.Add(group.Id,
                new(ArenaRestoreSource.EntryMark, ArenaRestoreSource.OwnedFrame, ArenaRestoreSource.EntryMark)),
        };
        var code = new RecordingInstructionWriter();

        emitter.Emit(code, LandingPadRestoreStep.MethodEntry, context);

        Assert.Equal(
            [(WasmOpcodes.Call, (uint)imports.Resolve(RuntimeImportSymbol.FrameMarkRoot, context.RuntimeImportSelection)),
             (WasmOpcodes.LocalSet, 90u),
             (WasmOpcodes.Call, (uint)imports.Resolve(RuntimeImportSymbol.FrameMarkStackTrace, context.RuntimeImportSelection)),
             (WasmOpcodes.LocalSet, 92u)],
            code.ToInstructions().Select(instruction => (instruction.Opcode, instruction.Operand.UnsignedValue)));
    }

    private static StructuredExceptionClause Clause(
        CilExceptionRegionKind kind,
        EntityKey? catchType,
        StructuredSequence? handlerBody = null) => new(
        kind,
        0,
        1,
        catchType,
        kind == CilExceptionRegionKind.Filter ? 0 : null,
        handlerBody ?? StructuredSequence.Empty,
        kind == CilExceptionRegionKind.Filter ? StructuredSequence.Empty : null)
        {
            HandlerBlock = new StructuredBlockId(0),
            FilterBlock = kind == CilExceptionRegionKind.Filter
            ? new StructuredBlockId(0)
            : null,
        };

    private static StructuredExceptionGroup Group(
        int tryOffset,
        int tryLength,
        ImmutableArray<StructuredExceptionPart> protectedParts,
        ImmutableArray<StructuredExceptionClause> clauses,
        ImmutableArray<StructuredExceptionContinuation> continuations,
        StructuredDispatcher? continuationDispatcher = null) => new(
        new StructuredExceptionGroupId(tryOffset),
        null,
        tryOffset,
        tryLength,
        protectedParts,
        clauses,
        continuations,
        continuationDispatcher,
        null)
        {
            ProtectedBlocks = [],
        };

    private static StructuredExceptionContinuation Continuation(
        int targetOffset,
        StructuredSequence body) => new(
        new StructuredContinuationId(targetOffset),
        targetOffset,
        new StructuredBlockId(targetOffset),
        body);

    private static StructuredBlockOccurrence Block(int index) => new(
        new StructuredBlockId(index),
        StructuredBlockRole.Owner);

    private static StructuredMethod WithGroups(
        StructuredMethod method,
        params StructuredExceptionGroup[] groups) => method with
        {
            TopLevelExceptionGroups = [.. groups.Select(group => group.Id)],
            ExceptionGroups = groups.ToImmutableDictionary(group => group.Id),
        };

    private static IExceptionRegionEmitter CreateEmitter(
        FakeProgram program,
        RecordingLayoutProvider layouts,
        RuntimeImportCatalog imports)
    {
        return new[]
            {
                new ExceptionRegionEmitter(
                    layouts,
                    new ExceptionPayloadBlockEmitter(layouts),
                    TestExceptionFrames.Steps(layouts, imports),
                    new PassThroughCatchLocalFinder(),
                    imports),
            }
            .Cast<IExceptionRegionEmitter>()
            .Single();
    }

    private static ModuleDataPlan CreateModuleData(
        StructuredExceptionGroup group) => CreateModuleData((group, false));

    private static ModuleDataPlan CreateModuleData(
        params (StructuredExceptionGroup Group, bool HasFilters)[] entries) => new(
        new Dictionary<StructuredExceptionGroupKey, ExceptionGroupMetadata>(
            entries.Select((entry, index) => new KeyValuePair<
                    StructuredExceptionGroupKey,
                ExceptionGroupMetadata>(
                    new StructuredExceptionGroupKey(EntryKey, null, entry.Group.Id),
                new ExceptionGroupMetadata(
                    128 + index * 16,
                    entry.Group.Clauses.Length,
                    entry.HasFilters)))),
        [],
        [],
        ImmutableDictionary<string, StaticInitializerGuard>.Empty,
        132);

    private static MethodEmissionContext CreateContext(
        params StructuredExceptionGroup[] groups)
    {
        var roots = new MethodRootMap(
            EntryKey,
            [],
            []);
        return new MethodEmissionContext(
            roots,
            0,
            0,
            WasmLocalLayoutPlanner.CreateEvaluationStack(0, 1),
            6,
            7,
            8,
            new Dictionary<StructuredExceptionGroupId, int>(
                groups.Select((group, index) =>
                    new KeyValuePair<StructuredExceptionGroupId, int>(
                        group.Id,
                        9 + index))),
            new Dictionary<StructuredExceptionGroupId, int>(
                groups.Select((group, index) =>
                    new KeyValuePair<StructuredExceptionGroupId, int>(
                        group.Id,
                        10 + index))),
            11,
            new ValueFrameLayout(
                0,
                [],
                [],
                [],
                []),
            12,
            FilterEnvironmentLayout.Empty,
            0,
            13,
            14,
            15,
            16,
            17,
            18)
        {
            ManagedThrowFunction = OptionalFunctionIndex.At(7),
            ExceptionCellFrames = groups.Select((group, index) => (group.Id, index))
                .ToImmutableDictionary(item => item.Id, item => 40 + item.index),
            // A block without catch clauses, and a catch without filters that is not a pass-through one, keeps its
            // marks in locals, as the method's context factory gives it.
            RegionMarkLocals = groups.Select((group, index) => (group, index))
                .Where(item => item.group.Clauses.All(clause =>
                    clause.Kind is CilExceptionRegionKind.Finally or CilExceptionRegionKind.Fault) ||
                    item.group.Clauses.All(clause => clause is { Kind: CilExceptionRegionKind.Catch, PassThroughLocal: null }))
                .ToImmutableDictionary(item => item.group.Id, item => new RegionMarkLocals(
                    60 + 3 * item.index, 61 + 3 * item.index, 62 + 3 * item.index)),
            // Each reads its marks as it begins, as a method whose frames do not say where it stood does.
            LandingPadRestores = groups.ToImmutableDictionary(group => group.Id,
                _ => new LandingPadRestore(ArenaRestoreSource.BlockMark, ArenaRestoreSource.BlockMark, ArenaRestoreSource.None)),
            EntryMarks = new RegionMarkLocals(90, 91, 92),
        };
    }

    private static int CallIndex(List<WasmInstruction> instructions, int function) => instructions.FindIndex(instruction =>
        instruction.Opcode == WasmOpcodes.Call && instruction.Operand.UnsignedValue == (uint)function);

    private static int[] Calls(byte[] code) => [.. code
        .Select((value, index) => (value, index))
        .Where(item => item.value == WasmOpcodes.Call && item.index + 1 < code.Length)
        .Select(item => (int)code[item.index + 1])];

    private sealed record UnrelatedPart : StructuredExceptionPart;
}
