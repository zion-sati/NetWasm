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
                false,
                CreateModuleData(),
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
            false,
            module,
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
            false,
            CreateModuleData((group, false), (nested, false)),
            (_, _, _, _, _) => { });

        Assert.Contains(
            WasmOpcodes.BranchIf,
            new WasmBinarySnapshotReader(buffer).Read());
    }

    [Fact]
    public void OutermostCatchLeavesTheMethodFrameBeforeRethrow()
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
            []);
        var frameExits = new RecordingMethodFrameExitEmitter();

        CreateEmitter(program, layouts, imports, frameExits).Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
            WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null),
            CreateContext(group),
            true,
            CreateModuleData(group),
            (_, _, _, _, _) => { });

        Assert.Equal(1, frameExits.Calls);
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
                true,
                CreateModuleData(group),
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
            false,
            CreateModuleData(group),
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
            false,
            CreateModuleData(group),
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
                false,
                CreateModuleData(group),
                (_, _, _, _, _) => { }));
    }

    [Fact]
    public void FinallyClearsHandledNestedExceptionBeforeTestingExceptionalExit()
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
        var outputBuffer = new WasmBinaryBuffer();
        var output = new WasmBinaryWriter(outputBuffer);
        var code = new WasmInstructionWriter(output);

        CreateEmitter(program, layouts, imports).Emit(
            code,
            WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null),
            context,
            false,
            CreateModuleData(group),
            (_, _, _, _, _) => { });

        var bytes = new WasmBinarySnapshotReader(outputBuffer).Read();
        var exceptionTemporaryStores = bytes
            .Select((value, index) => (value, index))
            .Count(item =>
                item.value == WasmOpcodes.LocalSet &&
                item.index + 1 < bytes.Length &&
                bytes[item.index + 1] == context.ExceptionTemporary);
        Assert.Equal(3, exceptionTemporaryStores);
    }

    [Fact]
    public void FinallyUsesMemory64ComparisonAndCanSuppressFrameExit()
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
        var frameExits = new RecordingMethodFrameExitEmitter();

        CreateEmitter(program, layouts, imports, frameExits).Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
            WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null),
            CreateContext(group) with { LeaveFrameOnExceptionalExit = false },
            true,
            CreateModuleData(group),
            (_, _, _, _, _) => { });

        Assert.Equal(0, frameExits.Calls);
    }

    [Fact]
    public void OutermostFinallyLeavesTheMethodFrameBeforeTestingExceptionalExit()
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
        var frameExits = new RecordingMethodFrameExitEmitter();

        CreateEmitter(program, layouts, imports, frameExits).Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
            WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null),
            CreateContext(group),
            true,
            CreateModuleData(group),
            (_, _, _, _, _) => { });

        Assert.Equal(1, frameExits.Calls);
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
            false,
            CreateModuleData((group, true)),
            (_, _, _, _, _) => { });

        Assert.Contains(
            imports.Resolve(RuntimeImportSymbol.ExceptionFrameSetEnvironment),
            Calls(new WasmBinarySnapshotReader(buffer).Read()));
    }

    [Fact]
    public void OutermostFaultLeavesTheMethodFrameBeforeRethrow()
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
            [Clause(CilExceptionRegionKind.Fault, null, handlerBody)],
            []);
        var buffer = new WasmBinaryBuffer();
        var emittedSequences = new List<StructuredSequence>();

        var context = CreateContext(group) with
        {
            ValueLayout = new ValueFrameLayout(4, [], [], [], []),
        };
        CreateEmitter(program, layouts, imports).Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(buffer)),
            WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null),
            context,
            true,
            CreateModuleData(group),
            (sequence, _, _, _, _) => emittedSequences.Add(sequence));

        Assert.Contains(handlerBody, emittedSequences);
        Assert.Contains(
            imports.Resolve(RuntimeImportSymbol.ValueFrameLeave),
            Calls(new WasmBinarySnapshotReader(buffer).Read()));
    }

    [Fact]
    public void OutermostFaultCanSuppressMethodFrameExit()
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
            [Clause(CilExceptionRegionKind.Fault, null)],
            []);
        var frameExits = new RecordingMethodFrameExitEmitter();

        CreateEmitter(program, layouts, imports, frameExits).Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
            WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null),
            CreateContext(group) with { LeaveFrameOnExceptionalExit = false },
            true,
            CreateModuleData(group),
            (_, _, _, _, _) => { });

        Assert.Equal(0, frameExits.Calls);
    }

    [Fact]
    public void NestedFaultNeverLeavesTheMethodFrame()
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
            [Clause(CilExceptionRegionKind.Fault, null)],
            []);
        var frameExits = new RecordingMethodFrameExitEmitter();

        CreateEmitter(program, layouts, imports, frameExits).Emit(
            new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
            WithGroups(method, group),
            new StructuredExceptionRegion(group.Id, null),
            CreateContext(group),
            false,
            CreateModuleData(group),
            (_, _, _, _, _) => { });

        Assert.Equal(0, frameExits.Calls);
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
            false,
            CreateModuleData(group),
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
            false,
            CreateModuleData(group),
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
            false,
            CreateModuleData(group),
            (_, _, _, _, _) => { });
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
            false,
            CreateModuleData(group),
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
                false,
                CreateModuleData(group),
                (_, _, _, _, _) => { }));

        Assert.Contains(nameof(UnrelatedPart), exception.Message);
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
        RuntimeImportCatalog imports,
        IMethodFrameExitEmitter? methodFrameExits = null)
    {
        return new[]
            {
                new ExceptionRegionEmitter(
                    layouts,
                    imports,
                    new ExceptionPayloadBlockEmitter(layouts),
                    methodFrameExits ?? CreateMethodFrameExit(imports),
                    new StructuredExceptionGroupKeyFactory()),
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
        [],
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
            18);
    }

    private static int[] Calls(byte[] code) => [.. code
        .Select((value, index) => (value, index))
        .Where(item => item.value == WasmOpcodes.Call && item.index + 1 < code.Length)
        .Select(item => (int)code[item.index + 1])];

    private sealed record UnrelatedPart : StructuredExceptionPart;

    private sealed class RecordingMethodFrameExitEmitter : IMethodFrameExitEmitter
    {
        public int Calls { get; private set; }

        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context) => Calls++;
    }
}
