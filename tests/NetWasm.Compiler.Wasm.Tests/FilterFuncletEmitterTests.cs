using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class FilterFuncletEmitterTests
{
    [Fact]
    public void SequenceAdapterRejectsAMissingEmissionCapability()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new FilterSequenceEmitterAdapter(null!));
    }

    [Fact]
    public void FuncletUsesIsolatedContextAndTargetWidth()
    {
        var program = new FakeProgram();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var clause = new StructuredExceptionClause(
            CilExceptionRegionKind.Filter,
            0,
            1,
            null,
            0,
            StructuredSequence.Empty,
            StructuredSequence.Empty)
        {
            HandlerBlock = method.EntryBlock,
            FilterBlock = method.EntryBlock,
        };
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        MethodEmissionContext? context = null;
        var sequence = new FilterSequenceEmitterAdapter(
            (_, _, _, emittedContext) =>
            {
                context = emittedContext;
                return new ManagedMethodSequenceEmission(
                    ImmutableDictionary<int, int>.Empty.Add(0, 1));
            });

        var emitter = new FilterFuncletEmitter(
layouts,
            new ExceptionPayloadBlockEmitter(layouts),
            new GeneratedFunctionWriterFactory());
        var emission = ((IFilterFuncletEmitter)emitter).Emit(
                    new FilterFunclet(1,
            new ManagedMethodIdentity("test-filter"), method, clause),
                    new FilterEnvironmentLayout(
                        16,
                        0,
                        8,
                        []),
                    sequence);

        Assert.NotNull(context);
        Assert.True(context.IsFilterFunclet);
        Assert.Equal(new ManagedMethodIdentity("test-filter"), context.CallerIdentity);
        Assert.False(context.LeaveFrameOnRethrow);
        Assert.False(context.LeaveFrameOnExceptionalExit);
        Assert.Contains(WasmOpcodes.I64Constant, emission.Body);
        Assert.Contains(WasmOpcodes.I64Load, emission.Body);
        Assert.Contains(WasmOpcodes.TryTable, emission.Body);
        Assert.Equal(1, emission.OriginalBlockEmissionCounts[0]);
    }

    [Fact]
    public void Memory32AtRootFrameBaseUses32BitAddressInstructions()
    {
        var program = new FakeProgram();
        var emitter = CreateEmitter(WasmTarget.Wasm32);

        var emission = emitter.Emit(
            new FilterFunclet(
                1,
                new ManagedMethodIdentity("test-filter"),
                CreateFilterMethod(program),
                CreateFilterClause(StructuredSequence.Empty)),
            CreateEnvironment(rootFrameOffset: 0),
            CreateSequence());

        Assert.Contains(WasmOpcodes.I32Load, emission.Body);
        Assert.Contains(WasmOpcodes.I32Constant, emission.Body);
    }

    [Fact]
    public void Memory32AddsNonZeroRootFrameOffset()
    {
        var program = new FakeProgram();
        var emitter = CreateEmitter(WasmTarget.Wasm32);

        var emission = emitter.Emit(
            new FilterFunclet(
                1,
                new ManagedMethodIdentity("test-filter"),
                CreateFilterMethod(program),
                CreateFilterClause(StructuredSequence.Empty)),
            CreateEnvironment(rootFrameOffset: 8),
            CreateSequence());

        Assert.Contains(WasmOpcodes.I32Add, emission.Body);
        Assert.Contains(WasmOpcodes.I32Load, emission.Body);
    }

    [Fact]
    public void RejectsNullInputs()
    {
        var program = new FakeProgram();
        var method = CreateFilterMethod(program);
        var filter = new FilterFunclet(
            1,
            new ManagedMethodIdentity("test-filter"),
            method,
            CreateFilterClause(StructuredSequence.Empty));
        var environment = CreateEnvironment(rootFrameOffset: 0);
        var sequence = CreateSequence();
        var emitter = CreateEmitter(WasmTarget.Wasm32);

        Assert.Throws<ArgumentNullException>(() =>
            emitter.Emit(null!, environment, sequence));
        Assert.Throws<ArgumentNullException>(() =>
            emitter.Emit(filter, null!, sequence));
        Assert.Throws<ArgumentNullException>(() =>
            emitter.Emit(filter, environment, null!));
    }

    [Fact]
    public void RejectsFilterWithoutFilterBody()
    {
        var program = new FakeProgram();
        var emitter = CreateEmitter(WasmTarget.Wasm32);

        Assert.Throws<InvalidOperationException>(() =>
            emitter.Emit(
                new FilterFunclet(
                    1,
                    new ManagedMethodIdentity("test-filter"),
                    CreateFilterMethod(program),
                    CreateFilterClause(filterBody: null)),
                CreateEnvironment(rootFrameOffset: 0),
                CreateSequence()));
    }

    private static IFilterFuncletEmitter CreateEmitter(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var program = new FakeProgram();
        return new[]
        {
            new FilterFuncletEmitter(
layouts,
                new ExceptionPayloadBlockEmitter(layouts),
                new GeneratedFunctionWriterFactory()),
        }.Cast<IFilterFuncletEmitter>().Single();
    }

    private static StructuredMethod CreateFilterMethod(FakeProgram program) =>
        Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));

    private static StructuredExceptionClause CreateFilterClause(
        StructuredSequence? filterBody) =>
        new(
            CilExceptionRegionKind.Filter,
            0,
            1,
            null,
            filterBody is null ? null : 0,
            StructuredSequence.Empty,
            filterBody)
        {
            HandlerBlock = new StructuredBlockId(0),
            FilterBlock = filterBody is null ? null : new StructuredBlockId(0),
        };

    private static FilterEnvironmentLayout CreateEnvironment(int rootFrameOffset) =>
        new(16, rootFrameOffset, 8, []);

    private static FilterSequenceEmitterAdapter CreateSequence() =>
        new FilterSequenceEmitterAdapter(
            (_, _, _, _) => new ManagedMethodSequenceEmission(
                ImmutableDictionary<int, int>.Empty.Add(0, 1)));
}
