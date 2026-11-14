using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NetWasm.Compiler.Analysis;

internal sealed class ReachableMethodAnalyzerFactory(
    ITypedStackValidatorFactory stackValidators,
    IControlFlowGraphBuilderFactory graphBuilders,
    ILogger<DiagnosticReachableMethodAnalyzer> logger) : IReachableMethodAnalyzerFactory
{
    internal ReachableMethodAnalyzerFactory(
        ITypedStackValidatorFactory stackValidators,
        IControlFlowGraphBuilderFactory graphBuilders) : this(
            stackValidators,
            graphBuilders,
            NullLogger<DiagnosticReachableMethodAnalyzer>.Instance)
    {
    }

    public IReachableMethodAnalyzer Create(
        IMethodBodyReader methodBodies,
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        IMethodSpecializer specializer,
        IManagedMethodOptimizer optimizer,
        ICalledMethodResolver calledMethods,
        WasmOptimizationSettings settings,
        IImplicitExceptionDiscovery exceptionDiscovery,
        IReachabilityInstructionAnalyzer instructionAnalyzer) =>
        new DiagnosticReachableMethodAnalyzer(
            new ReachableMethodAnalyzer(
            methodBodies,
            types,
            fields,
            methods,
            specializer,
            optimizer,
            // A body a whole-program reduction rewrote finds its proofs again.
            CreateProofs(types, fields, methods, calledMethods, settings),
            exceptionDiscovery,
            stackValidators,
            graphBuilders.Create(),
            instructionAnalyzer),
            logger, new CompilerDiagnosticProvenanceBinder());

    // The managed optimizer's closing proof passes, under the same settings.
    private MethodBodyProofChain CreateProofs(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        ICalledMethodResolver calledMethods,
        WasmOptimizationSettings settings)
    {
        var graphBuilder = graphBuilders.Create();
        var stackValidator = stackValidators.Create(types, fields, methods);
        var dominance = new ControlFlowDominanceAnalyzer();
        var arrayChecks = settings.ArrayBoundsCheckElimination == ArrayBoundsCheckEliminationPolicy.Conservative;
        return new(
            new Arrays.ArrayBoundsCheckEliminator(graphBuilder, stackValidator,
                new Arrays.ArrayBoundsCheckEliminationPlanner(
                    new Arrays.ArrayLoopInductionAnalyzer(new ControlFlowNaturalLoopAnalyzer(dominance), dominance), dominance),
                arrayChecks),
            new Arrays.RepeatedArrayAccessEliminator(graphBuilder, stackValidator, new Arrays.RepeatedArrayAccessPlanner(dominance),
                arrayChecks),
            new NullChecks.ConstructedObjectNullCheckEliminator(graphBuilder, stackValidator,
                new NullChecks.ConstructedReceiverPlanner(dominance, new StackEffectResolver(calledMethods), calledMethods),
                settings.NewObjectNullCheckElimination != NewObjectNullCheckEliminationPolicy.Disabled),
            new IntegerCheckEliminator(graphBuilder, stackValidator,
                new IntegerRangeAnalysis(new StackEffectResolver(calledMethods)),
                settings.IntegerCheckElimination == IntegerCheckEliminationPolicy.ProvenRanges));
    }
}
