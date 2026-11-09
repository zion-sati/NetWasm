using System;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.NullChecks;

internal interface IConstructorReceiverProverFactory
{
    IConstructorReceiverProver Create(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols,
        bool enabled);
}

internal sealed class ConstructorReceiverProverFactory(
    ITypedStackValidatorFactory stackValidators,
    IControlFlowGraphBuilderFactory graphBuilders,
    ICalledMethodResolverFactory calledMethods) : IConstructorReceiverProverFactory
{
    private readonly ITypedStackValidatorFactory _stackValidators = stackValidators ??
        throw new ArgumentNullException(nameof(stackValidators));
    private readonly IControlFlowGraphBuilderFactory _graphBuilders = graphBuilders ??
        throw new ArgumentNullException(nameof(graphBuilders));
    private readonly ICalledMethodResolverFactory _calledMethods = calledMethods ??
        throw new ArgumentNullException(nameof(calledMethods));

    public IConstructorReceiverProver Create(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols,
        bool enabled)
    {
        var called = _calledMethods.Create(methods, methodInstances, symbols);
        return new ConstructorReceiverProver(
            new ConstructedReceiverPlanner(new ControlFlowDominanceAnalyzer(), new StackEffectResolver(called), called),
            called,
            _graphBuilders.Create(),
            _stackValidators.Create(types, fields, methods),
            enabled);
    }
}
