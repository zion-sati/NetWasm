using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Analysis.LoopInvariantFields;
using NetWasm.Compiler.Analysis.Allocations;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Analysis;

internal sealed class MethodSpecializerFactory(
    IControlFlowGraphBuilderFactory graphBuilders,
    IControlFlowGraphAnalyzerFactory graphAnalyzers,
    ITypedStackValidatorFactory stackValidators) : IMethodSpecializerFactory
{
    private readonly IControlFlowGraphBuilderFactory _graphBuilders = graphBuilders ??
        throw new ArgumentNullException(nameof(graphBuilders));
    private readonly IControlFlowGraphAnalyzerFactory _graphAnalyzers = graphAnalyzers ??
        throw new ArgumentNullException(nameof(graphAnalyzers));
    private readonly ITypedStackValidatorFactory _stackValidators = stackValidators ??
        throw new ArgumentNullException(nameof(stackValidators));

    public IMethodSpecializer Create(
        ITypeDefinitionResolver typeDefinitions,
        ITypeRepository typeRepository,
        IMethodRepository methods,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols,
        IFieldRepository fields,
        ITypeFinder types,
        ICalledMethodResolver calledMethods,
        IMethodImplementationResolver methodImplementations,
        ITypeRelationshipClassifier relationships,
        IMethodBodyReader methodBodies,
        WasmOptimizationSettings optimizationSettings,
        IRuntimeIntrinsicRegistry intrinsics,
        PlatformWorldCapabilities world,
        ITypeIdentityResolver typeIdentities)
    {
        var constrainedTargets = new ConstrainedCallTargetResolver(
            types,
            methods,
            methodImplementations);
        var stackEffects = new StackEffectResolver(calledMethods);
        var stackConsumers = new StackValueConsumerFinder(stackEffects);
        return new MethodSpecializer(
        [
            // First, so every later rule sees an argument or store of the kind declared.
            new ImplicitCoercionRewriter(_graphBuilders.Create(), _stackValidators.Create(typeRepository, fields, methods)),
            // Second, so every later rule meets each leave on an empty stack, exiting a block.
            new LeaveRewriter(_graphBuilders.Create(), _stackValidators.Create(typeRepository, fields, methods)),
            .. optimizationSettings.SwitchBitTests == SwitchBitTestPolicy.Conservative
                ? [new SwitchBitTestRewriter()]
                : Array.Empty<IMethodRewriteRule>(),
            new SharedReturnEpilogueRewriter(),
            new Int32LocalInitializerRewriter(),
            new ValueTypeFieldReadRewriter(),
            new AggregateFieldHoistingRewriter(
                new AggregateFieldHoistingPlanner(
                    _graphBuilders,
                    _graphAnalyzers,
                    new FieldProjectionResolver(
                        methodBodies,
                        calledMethods,
                        fields,
                        typeDefinitions),
                    optimizationSettings.LoopInvariantFieldHoisting)),
            new RectangularArrayCallRewriter(),
            new IntrinsicCallRewriter(calledMethods, symbols),
            new PlatformWorldCallRewriter(calledMethods, symbols, world),
            new PlatformServices.PlatformServiceBindingRewriter(calledMethods, symbols, world),
            new ClosedTypeIsValueTypeCallRewriter(calledMethods, symbols),
            new ClosedTypeContainsReferencesCallRewriter(calledMethods, symbols, typeDefinitions, fields),
            new InterlockedCallRewriter(calledMethods, symbols),
            new ActivatorCallRewriter(
                typeDefinitions,
                methods,
                methodInstances,
                symbols),
            new StringConstructorRewriter(calledMethods, types, methods, methodInstances, symbols),
            new CreateSpanCallRewriter(
                calledMethods,
                new ReadOnlySpanDataResolver()),
            new RvaPointerSpanRewriter(
                calledMethods,
                fields,
                typeIdentities,
                typeDefinitions,
                methods),
            new InitializeArrayCallRewriter(
                calledMethods,
                symbols),
            new DefaultComparerCallRewriter(
                typeDefinitions,
                methods,
                methodInstances,
                types,
                calledMethods,
                symbols,
                relationships,
                constrainedTargets,
                stackConsumers,
                optimizationSettings.Devirtualization == DevirtualizationPolicy.ExactType),
            new EnumEqualsCallRewriter(typeDefinitions, calledMethods, symbols,
                optimizationSettings.AllocationElimination != AllocationEliminationPolicy.Disabled),
            new BoxedReceiverCallRewriter(
                typeDefinitions,
                methods,
                calledMethods,
                new NullableTypeResolver(),
                relationships,
                constrainedTargets,
                stackEffects),
            new ConstrainedCallRewriter(
                types,
                typeDefinitions,
                methods,
                methodInstances,
                symbols,
                constrainedTargets),
            new ClosedTypeComparisonRewriter(
                _graphBuilders.Create(),
                calledMethods,
                new PureReferenceComparisonClassifier(methodBodies, calledMethods, typeDefinitions, methods, new SharedReturnEpilogueRewriter(), intrinsics),
                new ClosedTypeIdentityComparer(typeDefinitions)),
            new ExactObjectTypeRewriter(_graphBuilders.Create(), calledMethods, fields, typeDefinitions,
                new NullableTypeResolver()),
            new ConstantTypeMaterializationRewriter(_graphBuilders.Create()),
            new BoxedNullComparisonRewriter(_graphBuilders.Create(), new NullableTypeResolver()),
            new BoxedTypeTestRewriter(_graphBuilders.Create(), new NullableTypeResolver(), relationships),
            new NonReturningCallRewriter(calledMethods, methodBodies, intrinsics),
            new KnownSizeRewriter(calledMethods),
            new BitCastCallRewriter(calledMethods, types, methods, methodInstances, symbols),
            new VectorElementReadRewriter(calledMethods, types, methods, methodInstances, symbols),
            new ConstantStaticFieldRewriter(fields, typeIdentities, typeDefinitions, methods, methodBodies),
            ConstantBranchFoldingFactory.Create(_graphBuilders.Create()),
        ]);
    }
}
