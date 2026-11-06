using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class FinalizationPlanBuilderTests
{
    [Fact]
    public void RejectsMissingDependenciesAndDefaultMethodSet()
    {
        var descriptors = new DescriptorSource();
        var intrinsics = new IntrinsicRegistry([]);

        Assert.Throws<ArgumentNullException>(() =>
            new FinalizationPlanBuilder(null!, intrinsics));
        Assert.Throws<ArgumentNullException>(() =>
            new FinalizationPlanBuilder(descriptors, null!));
        Assert.Throws<ArgumentException>(() =>
            new FinalizationPlanBuilder(descriptors, intrinsics).Build(default));
        Assert.False(FinalizationPlan.Empty.RequiresRuntime);
    }

    [Fact]
    public void OrdinaryReachableProgramRequiresNoFinalizationRuntime()
    {
        var plan = new FinalizationPlanBuilder(
            new DescriptorSource(),
            new IntrinsicRegistry([
                (StringLengthKey, RuntimeIntrinsic.StringLength),
            ])).Build([
                Emission(
                    I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                    I(1, CilOperation.Call, new CilOperand.Entity(StringLengthKey)))
            ]);

        Assert.False(plan.RequiresRuntime);
        Assert.False(plan.UsesExplicitFinalizationApi);
        Assert.Empty(plan.FinalizableTypeIds);
        Assert.False(plan.FinalizableTypeIds.Contains(7));
    }

    [Fact]
    public void DirectAndConstructedFinalizersProduceExactTypeSet()
    {
        var finalizer = Finalizer();
        var descriptors = new DescriptorSource
        {
            Direct = [new(TypeKey, 7, 0, 16, 0, 0, finalizer)],
            Constructed = [new(
                finalizer.DeclaringType,
                11,
                0,
                16,
                0,
                0,
                finalizer)],
        };

        var plan = new FinalizationPlanBuilder(
            descriptors,
            new IntrinsicRegistry([])).Build([]);

        Assert.True(plan.RequiresRuntime);
        Assert.False(plan.UsesExplicitFinalizationApi);
        Assert.Equal([7, 11], plan.FinalizableTypeIds.Order());
        Assert.True(plan.FinalizableTypeIds.Contains(7));
        Assert.True(plan.FinalizableTypeIds.Contains(11));
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.ReRegisterForFinalize, true)]
    [InlineData(RuntimeIntrinsic.GcWaitForPendingFinalizers, false)]
    public void StandaloneFinalizationApisNeedNoRuntimeWithoutFinalizableTypes(
        RuntimeIntrinsic intrinsic,
        bool useMethodInstance)
    {
        var key = Key(0x06000079 + (int)intrinsic);
        var finalizer = Finalizer() with
        {
            Definition = Finalizer().Definition with { Key = key },
        };
        CilOperand operand = useMethodInstance
            ? new CilOperand.MethodInstance(finalizer)
            : new CilOperand.Entity(key);

        var plan = new FinalizationPlanBuilder(
            new DescriptorSource(),
            new IntrinsicRegistry([(key, intrinsic)])).Build([
                Emission(I(0, CilOperation.CallVirtual, operand))
            ]);

        Assert.False(plan.RequiresRuntime);
        Assert.True(plan.UsesExplicitFinalizationApi);
        Assert.Empty(plan.FinalizableTypeIds);
    }

    [Fact]
    public void SuppressFinalizeNeedsNoRuntimeWhenNoFinalizableTypeExists()
    {
        var key = Key(0x06000079 + (int)RuntimeIntrinsic.SuppressFinalize);
        var plan = new FinalizationPlanBuilder(
            new DescriptorSource(),
            new IntrinsicRegistry([(key, RuntimeIntrinsic.SuppressFinalize)])).Build([
                Emission(I(0, CilOperation.Call, new CilOperand.Entity(key)))
            ]);

        Assert.False(plan.RequiresRuntime);
        Assert.False(plan.UsesExplicitFinalizationApi);
        Assert.Empty(plan.FinalizableTypeIds);
    }

    private static StructuredMethodEmission Emission(params CilInstruction[] instructions)
    {
        var method = new FakeProgram().GetMethod(EntryKey);
        var body = new CilMethodBody(method, 1, [], [.. instructions]);
        return new(
            new ManagedMethodIdentity("Test.Type::Run"),
            new StructuredMethod(
                new StructuredMethodHeader(
                    body.Method,
                    body.MethodInstance,
                    body.MaxStack,
                    body.Locals,
                    body.LocalSignatureTypes,
                    body.Instructions),
                new StructuredBlockId(0),
                ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty,
                StructuredSequence.Empty,
                [],
                ImmutableDictionary<
                    StructuredExceptionGroupId,
                    StructuredExceptionGroup>.Empty,
                ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
    }

    private static MethodInstanceModel Finalizer()
    {
        var program = new FakeProgram();
        var definition = program.GetMethod(ConstructorKey);
        return new(
            definition,
            new TestCilTypeIdentityResolver().Resolve(definition.DeclaringType),
            [],
            definition.Signature);
    }

    private sealed class IntrinsicRegistry(
        IEnumerable<(EntityKey Method, RuntimeIntrinsic Intrinsic)> entries) :
        IRuntimeIntrinsicRegistry
    {
        private readonly Dictionary<EntityKey, RuntimeIntrinsic> _entries =
            entries.ToDictionary(static entry => entry.Method, static entry => entry.Intrinsic);

        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic) =>
            _entries.TryGetValue(method, out intrinsic);
    }

    private sealed class DescriptorSource : ITypeDescriptorSource
    {
        public ImmutableArray<TypeDescriptorLayout> Direct { get; init; } = [];
        public ImmutableArray<ConstructedTypeDescriptorLayout> Constructed { get; init; } = [];
        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors => Direct;
        public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors =>
            Constructed;
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];
    }
}
