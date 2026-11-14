using System;
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
    Arrays.IArrayBoundsCheckEliminatorFactory arrayBounds,
    Arrays.IRepeatedArrayAccessEliminatorFactory repeatedAccesses,
    NullChecks.IConstructedObjectNullCheckEliminatorFactory constructedObjects,
    IIntegerCheckEliminatorFactory integerChecks,
    ILogger<DiagnosticReachableMethodAnalyzer> logger) : IReachableMethodAnalyzerFactory
{
    internal ReachableMethodAnalyzerFactory(
        ITypedStackValidatorFactory stackValidators,
        IControlFlowGraphBuilderFactory graphBuilders,
        Arrays.IArrayBoundsCheckEliminatorFactory arrayBounds,
        Arrays.IRepeatedArrayAccessEliminatorFactory repeatedAccesses,
        NullChecks.IConstructedObjectNullCheckEliminatorFactory constructedObjects,
        IIntegerCheckEliminatorFactory integerChecks) : this(
            stackValidators,
            graphBuilders,
            arrayBounds,
            repeatedAccesses,
            constructedObjects,
            integerChecks,
            NullLogger<DiagnosticReachableMethodAnalyzer>.Instance)
    {
    }

    private readonly ITypedStackValidatorFactory _stackValidators = stackValidators ??
        throw new ArgumentNullException(nameof(stackValidators));
    private readonly IControlFlowGraphBuilderFactory _graphBuilders = graphBuilders ??
        throw new ArgumentNullException(nameof(graphBuilders));
    private readonly Arrays.IArrayBoundsCheckEliminatorFactory _arrayBounds = arrayBounds ??
        throw new ArgumentNullException(nameof(arrayBounds));
    private readonly Arrays.IRepeatedArrayAccessEliminatorFactory _repeatedAccesses = repeatedAccesses ??
        throw new ArgumentNullException(nameof(repeatedAccesses));
    private readonly NullChecks.IConstructedObjectNullCheckEliminatorFactory _constructedObjects = constructedObjects ??
        throw new ArgumentNullException(nameof(constructedObjects));
    private readonly IIntegerCheckEliminatorFactory _integerChecks = integerChecks ??
        throw new ArgumentNullException(nameof(integerChecks));
    private readonly ILogger<DiagnosticReachableMethodAnalyzer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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
            _stackValidators,
            _graphBuilders.Create(),
            instructionAnalyzer),
            _logger, new CompilerDiagnosticProvenanceBinder());

    // The managed optimizer's closing proof passes, from the same factories under the same settings.
    private MethodBodyProofChain CreateProofs(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        ICalledMethodResolver calledMethods,
        WasmOptimizationSettings settings)
    {
        var graphBuilder = _graphBuilders.Create();
        var stackValidator = _stackValidators.Create(types, fields, methods);
        var dominance = new ControlFlowDominanceAnalyzer();
        return new(
            _arrayBounds.Create(graphBuilder, stackValidator, dominance, settings),
            _repeatedAccesses.Create(graphBuilder, stackValidator, dominance, settings),
            _constructedObjects.Create(graphBuilder, stackValidator, dominance, calledMethods, settings),
            _integerChecks.Create(graphBuilder, stackValidator, calledMethods, settings));
    }
}
