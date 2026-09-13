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
        IImplicitExceptionDiscovery exceptionDiscovery,
        IReachabilityInstructionAnalyzer instructionAnalyzer) =>
        new DiagnosticReachableMethodAnalyzer(
            new ReachableMethodAnalyzer(
            methodBodies,
            types,
            fields,
            methods,
            specializer,
            exceptionDiscovery,
            stackValidators,
            graphBuilders.Create(),
            instructionAnalyzer),
            logger);
}
