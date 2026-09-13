using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class FilterFunctionAppenderTests
{
    [Fact]
    public void AppendsIndexedFilterAndReconcilesItsEmissionCounts()
    {
        var filter = CreateFilter();
        var functions = new List<WasmFunctionDefinition>();
        var indices = new Dictionary<int, int>();
        var sequences = new RecordingSequenceEmitter();
        var filters = new InvokingFilterEmitter();
        var merger = new RecordingCountMerger();
        var target = EmitterTestSupport.CreateInstructionModuleTarget(new FakeProgram());
        var functionIndices = new FixedFunctionIndexResolver();
        var emissions = new List<ManagedMethodEmissionRecord>();
        IFilterFunctionAppender appender = new[]
        {
            new FilterFunctionAppender(
                new FilterSequenceEmitterFactory(sequences),
                filters,
                merger),
        }.Cast<IFilterFunctionAppender>().Single();

        appender.Append(
            functions,
            4,
            indices,
            filter,
            FilterEnvironmentLayout.Empty,
            target,
            functionIndices,
            emissions);

        Assert.Equal(4, indices[filter.Id]);
        Assert.Equal("filter.9", Assert.Single(functions).Name);
        Assert.Equal([7], functions[0].Body);
        Assert.Same(target, sequences.Target);
        Assert.Same(functionIndices, sequences.FunctionIndices);
        Assert.Same(filter.Method, merger.Method);
        Assert.Equal(3, merger.Counts[1]);
    }

    [Fact]
    public void RejectsMissingInputsAndNegativeImportCount()
    {
        var appender = CreateAppender();
        var functions = new List<WasmFunctionDefinition>();
        var indices = new Dictionary<int, int>();
        var filter = CreateFilter();
        var environment = FilterEnvironmentLayout.Empty;
        var target = EmitterTestSupport.CreateInstructionModuleTarget(new FakeProgram());
        var functionIndices = new FixedFunctionIndexResolver();
        var emissions = new List<ManagedMethodEmissionRecord>();

        Assert.Throws<ArgumentNullException>(() => appender.Append(
            null!, 0, indices, filter, environment, target, functionIndices, emissions));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append(
            functions, -1, indices, filter, environment, target, functionIndices, emissions));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, null!, filter, environment, target, functionIndices, emissions));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, null!, environment, target, functionIndices, emissions));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, filter, null!, target, functionIndices, emissions));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, filter, environment, null!, functionIndices, emissions));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, filter, environment, target, null!, emissions));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, filter, environment, target, functionIndices, null!));
    }

    private static IFilterFunctionAppender CreateAppender() => new[]
    {
        new FilterFunctionAppender(
            new FilterSequenceEmitterFactory(new RecordingSequenceEmitter()),
            new InvokingFilterEmitter(),
            new RecordingCountMerger()),
    }.Cast<IFilterFunctionAppender>().Single();

    private static FilterFunclet CreateFilter()
    {
        var program = new FakeProgram();
        var method = EmitterTestSupport.Structure(
            program,
            program.GetMethod(EmitterTestSupport.EntryKey),
            EmitterTestSupport.I(
                0,
                CilOperation.LoadInt32,
                new CilOperand.ConstantI4(0)),
            EmitterTestSupport.I(1, CilOperation.Return));
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
        return new(9,
            new ManagedMethodIdentity("test-filter"), method, clause);
    }

    private sealed class RecordingSequenceEmitter : IManagedMethodSequenceEmitter
    {
        public InstructionModuleTarget? Target { get; private set; }
        public IFunctionIndexResolver? FunctionIndices { get; private set; }

        public ManagedMethodSequenceEmission Emit(
            IWasmInstructionWriter code,
            StructuredMethod method,
            StructuredSequence sequence,
            MethodEmissionContext context,
            InstructionModuleTarget target,
            IFunctionIndexResolver functionIndices,
            int? loopBreakDepth = null,
            int? loopContinueDepth = null,
            int? exceptionLeaveDepth = null)
        {
            Target = target;
            FunctionIndices = functionIndices;
            return new(ImmutableDictionary<int, int>.Empty.Add(1, 3));
        }
    }

    private sealed class InvokingFilterEmitter : IFilterFuncletEmitter
    {
        public FilterFuncletEmission Emit(
            FilterFunclet filter,
            FilterEnvironmentLayout environment,
            IFilterSequenceEmitter emitSequence)
        {
            var emission = emitSequence.Emit(
                new EmitterTestSupport.RecordingInstructionWriter(),
                filter.Method,
                StructuredSequence.Empty,
                EmitterTestSupport.CreateMethodEmissionContext());
            return new([7], emission.OriginalBlockEmissionCounts);
        }
    }

    private sealed class RecordingCountMerger : IFilterEmissionCountMerger
    {
        public StructuredMethod? Method { get; private set; }
        public ImmutableDictionary<int, int> Counts { get; private set; } = [];

        public void Merge(
            IList<ManagedMethodEmissionRecord> emissions,
            StructuredMethod filterMethod,
            ImmutableDictionary<int, int> filterCounts)
        {
            Method = filterMethod;
            Counts = filterCounts;
        }
    }

    private sealed class FixedFunctionIndexResolver : IFunctionIndexResolver
    {
        public int Resolve(EntityKey method) => 0;
        public int Resolve(string method) => 0;
        public int Resolve(MethodInstanceModel method) => 0;
    }
}
