using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedMethodOptimizerFactoryTests
{
    [Fact]
    public void FactoryCreatesTheSelectedManagedOptimizer()
    {
        var dependencies = new Dependencies();

        var optimizer = Factory().Create(
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            new(
                NetWasm.Compiler.ComponentModel.FinalWasmOptimization.O3,
                WasmInlineAnnotationSelection.Preserve,
                WasmInlineAnnotationSelection.Preserve,
                LoopInvariantFieldHoistingPolicy.Conservative,
                StaticDataViewEliminationPolicy.Conservative));

        Assert.IsType<ManagedMethodOptimizationPipeline>(optimizer);
    }

    [Fact]
    public void FactoryRejectsEveryMissingDependency()
    {
        var dependencies = new Dependencies();
        var settings = new WasmOptimizationSettings(
            NetWasm.Compiler.ComponentModel.FinalWasmOptimization.O3,
            WasmInlineAnnotationSelection.Preserve,
            WasmInlineAnnotationSelection.Preserve);
        var factory = Factory();

        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizerFactory(
                null!,
                new ControlFlowGraphBuilderFactory()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodOptimizerFactory(
                new TypedStackValidatorFactory(
                    new StackTypeCompatibilityValidator()),
                null!));

        Assert.Throws<ArgumentNullException>(() => factory.Create(
            null!, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, settings));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            dependencies, null!, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, settings));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            dependencies, dependencies, null!, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, settings));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            dependencies, dependencies, dependencies, null!, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, settings));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            dependencies, dependencies, dependencies, dependencies, null!, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, settings));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            dependencies, dependencies, dependencies, dependencies, dependencies, null!, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, settings));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, null!, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, settings));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, null!, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, settings));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, null!, dependencies, dependencies, dependencies, dependencies, dependencies, settings));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, null!, dependencies, dependencies, dependencies, dependencies, settings));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, null!, dependencies, dependencies, dependencies, settings));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, null!, dependencies, dependencies, settings));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, null!, dependencies, settings));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, null!, settings));
        Assert.Throws<ArgumentNullException>(() => factory.Create(
            dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, null!));
    }

    private static ManagedMethodOptimizerFactory Factory() => new(
        new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()),
        new ControlFlowGraphBuilderFactory());

    private sealed class Dependencies :
        NetWasm.Compiler.Metadata.IMethodBodyReader,
        NetWasm.Compiler.Core.ITypeRepository,
        NetWasm.Compiler.Core.IFieldRepository,
        NetWasm.Compiler.Core.IMethodRepository,
        IMethodSpecializer,
        ICalledMethodResolver,
        NetWasm.Compiler.Core.IRuntimeIntrinsicRegistry,
        IDispatchTargetResolver,
        IFinalizerResolver,
        NetWasm.Compiler.Core.ITypeDefinitionResolver,
        NetWasm.Compiler.Analysis.Loops.IArrayLoopMethodResolver,
        NetWasm.Compiler.Analysis.Loops.ISpanLengthResolver,
        NetWasm.Compiler.Analysis.Loops.IVectorLoopMethodResolver
    {
        public NetWasm.Compiler.Core.MethodInstanceModel Resolve(
            NetWasm.Compiler.Analysis.Loops.VectorLoopMethod method, NetWasm.Compiler.Core.CliTypeIdentity lane) =>
            throw new InvalidOperationException();

        public NetWasm.Compiler.Core.MethodInstanceModel Resolve(string name, NetWasm.Compiler.Core.CliTypeIdentity element) =>
            throw new InvalidOperationException();

        public NetWasm.Compiler.Core.MethodInstanceModel Resolve(NetWasm.Compiler.Core.MethodInstanceModel indexer) =>
            throw new InvalidOperationException();

        public NetWasm.Compiler.Core.TypeDefinitionModel ResolveTypeIdentity(NetWasm.Compiler.Core.CliTypeIdentity identity) =>
            throw new NotSupportedException();

        public NetWasm.Compiler.Core.MethodInstanceModel? Resolve(NetWasm.Compiler.Core.CliTypeIdentity allocatedType) => null;

        public NetWasm.Compiler.Core.DispatchTargetModel? Resolve(
            NetWasm.Compiler.Core.MethodInstanceModel declaration,
            NetWasm.Compiler.Core.CliTypeIdentity receiver) => null;

        public NetWasm.Compiler.Core.CilMethodBody ReadMethodBody(
            NetWasm.Compiler.Core.MethodDefinitionModel method) =>
            throw new InvalidOperationException();

        public NetWasm.Compiler.Core.CilMethodBody ReadMethodBody(
            NetWasm.Compiler.Core.MethodInstanceModel method) =>
            throw new InvalidOperationException();

        public NetWasm.Compiler.Core.TypeDefinitionModel GetTypeDefinition(
            NetWasm.Compiler.Core.EntityKey key) =>
            throw new InvalidOperationException();

        public NetWasm.Compiler.Core.MethodDefinitionModel GetMethod(
            NetWasm.Compiler.Core.EntityKey key) =>
            throw new InvalidOperationException();

        public NetWasm.Compiler.Core.FieldDefinitionModel GetField(
            NetWasm.Compiler.Core.EntityKey key) =>
            throw new InvalidOperationException();

        public NetWasm.Compiler.Core.CilMethodBody Rewrite(
            NetWasm.Compiler.Core.CilMethodBody body) => body;

        public NetWasm.Compiler.Core.MethodInstanceModel? Resolve(
            NetWasm.Compiler.Core.CilInstruction instruction) => null;

        public bool TryGetIntrinsic(NetWasm.Compiler.Core.EntityKey method, out NetWasm.Compiler.Core.RuntimeIntrinsic intrinsic)
        {
            intrinsic = default;
            return false;
        }
    }
}
