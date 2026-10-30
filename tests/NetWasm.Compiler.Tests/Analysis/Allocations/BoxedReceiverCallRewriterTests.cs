using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Allocations;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Tests.Analysis.Allocations;

public sealed class BoxedReceiverCallRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("BoxedReceiverFixture");
    private static readonly EntityKey ValueKey = new(Assembly, 0x02000001);
    private static readonly EntityKey ShapeKey = new(Assembly, 0x02000002);
    private static readonly CliTypeIdentity Value = CliTypeIdentity.Named(Assembly, "Fixture", "Value", true);
    private static readonly CliTypeIdentity Shape = CliTypeIdentity.Named(Assembly, "Fixture", "IShape", false);
    private static readonly MethodSignatureModel AreaSignature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4);
    private static readonly MethodDefinitionModel Declaration =
        new(new(Assembly, 0x06000001), ShapeKey, "Area", false, AreaSignature, 0) { IsVirtual = true, IsAbstract = true };
    private static readonly MethodDefinitionModel Implementation =
        new(new(Assembly, 0x06000002), ValueKey, "Area", false, AreaSignature, 1) { IsVirtual = true };
    private static readonly CilOperand Area = new CilOperand.MethodInstance(new(Declaration, Shape, [], AreaSignature));

    [Fact]
    public void ABoxThatOnlyCarriesTheReceiverBecomesACopyWithAConstrainedCall()
    {
        var body = Body(
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.Box, new CilOperand.TypeIdentity(Value)),
            I(2, CilOperation.CastClass, new CilOperand.TypeIdentity(Shape)),
            I(3, CilOperation.LoadArgument, new CilOperand.Index(1)),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(5, CilOperation.Add),
            I(6, CilOperation.CallVirtual, Area),
            I(7, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(9)),
            I(8, CilOperation.Return),
            I(9, CilOperation.Return));
        var fixture = new Fixture();

        var result = fixture.Rule().Rewrite(body);

        Assert.Equal(
            [
                CilOperation.LoadArgument, CilOperation.StoreLocal, CilOperation.LoadLocalAddress, CilOperation.Nop,
                CilOperation.LoadArgument, CilOperation.LoadInt32, CilOperation.Add, CilOperation.Constrained,
                CilOperation.CallVirtual, CilOperation.BranchIfTrue, CilOperation.Return, CilOperation.Return,
            ],
            result.Instructions.Select(static instruction => instruction.Operation));
        // The copy lives in a new local of the value's type.
        Assert.Equal([CliValueKind.I4, Value.StackKind], result.Locals.ToArray());
        Assert.Equal(Value, result.LocalSignatureTypes[^1]);
        Assert.Equal(new CilOperand.Index(1), result.Instructions[1].Operand);
        Assert.Equal(new CilOperand.Index(1), result.Instructions[2].Operand);
        Assert.Equal(new CilOperand.TypeIdentity(Value), result.Instructions[7].Operand);
        Assert.Equal(Area, result.Instructions[8].Operand);
        // Branches follow their targets; every instruction keeps its source offset.
        Assert.Equal(new CilOperand.BranchTarget(11), result.Instructions[9].Operand);
        Assert.Equal([0, 1, 1, 2, 3, 4, 5, 6, 6, 7, 8, 9], result.Instructions.Select(static instruction => instruction.SourceOffset));
        Assert.Equal([.. Enumerable.Range(0, 12)], result.Instructions.Select(static instruction => instruction.Offset));
        Assert.Equal([(Value, Shape)], fixture.Relationships.Calls);
    }

    [Theory]
    // An inherited or default implementation takes the object, a cast that can
    // fail, a box read twice or stored, a branch before the call, a call that is
    // a branch target, a non-virtual call, a reference, open or nullable box, an
    // instruction the rule does not follow, an enum, whose methods System.Enum
    // implements on the box.
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void LeavesOtherShapesAlone(int shape)
    {
        var nullable = CliTypeIdentity.GenericInstantiation(CliTypeIdentity.Named(Assembly, "System", "Nullable`1", true), [Value]);
        var boxed = shape switch
        {
            7 => Shape,
            8 => CliTypeIdentity.GenericParameter(true, 0),
            9 => nullable,
            _ => Value,
        };
        CilInstruction[] between = shape switch
        {
            2 => [I(3, CilOperation.Duplicate), I(4, CilOperation.Pop)],
            3 => [I(3, CilOperation.StoreLocal, new CilOperand.Index(0)), I(4, CilOperation.LoadLocal, new CilOperand.Index(0))],
            4 => [I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(4, CilOperation.Branch, new CilOperand.BranchTarget(5))],
            10 => [I(3, CilOperation.LoadFunction, Area), I(4, CilOperation.Pop)],
            _ => [I(3, CilOperation.Nop), I(4, CilOperation.Nop)],
        };
        var body = Body(
        [
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.Box, new CilOperand.TypeIdentity(boxed)),
            I(2, CilOperation.CastClass, new CilOperand.TypeIdentity(Shape)),
            .. between,
            shape == 4 ? I(5, CilOperation.Pop) : I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(3)),
            I(6, shape == 6 ? CilOperation.Call : CilOperation.CallVirtual, Area),
            shape == 5 ? I(7, CilOperation.Branch, new CilOperand.BranchTarget(6)) : I(7, CilOperation.Return),
        ]);
        var fixture = new Fixture(
            implementation: shape == 0 ? Implementation with { DeclaringType = ShapeKey } : Implementation,
            assignable: shape != 1,
            isEnum: shape == 11);

        Assert.Same(body, fixture.Rule().Rewrite(body));
    }

    [Fact]
    public void RejectsMissingDependenciesAndBody()
    {
        var fixture = new Fixture();
        var nullableTypes = new NullableTypeResolver();
        var effects = new StackEffectResolver(fixture);
        Assert.Throws<ArgumentNullException>(() => new BoxedReceiverCallRewriter(null!, fixture, fixture, nullableTypes, fixture.Relationships, fixture, effects));
        Assert.Throws<ArgumentNullException>(() => new BoxedReceiverCallRewriter(fixture, null!, fixture, nullableTypes, fixture.Relationships, fixture, effects));
        Assert.Throws<ArgumentNullException>(() => new BoxedReceiverCallRewriter(fixture, fixture, null!, nullableTypes, fixture.Relationships, fixture, effects));
        Assert.Throws<ArgumentNullException>(() => new BoxedReceiverCallRewriter(fixture, fixture, fixture, null!, fixture.Relationships, fixture, effects));
        Assert.Throws<ArgumentNullException>(() => new BoxedReceiverCallRewriter(fixture, fixture, fixture, nullableTypes, null!, fixture, effects));
        Assert.Throws<ArgumentNullException>(() => new BoxedReceiverCallRewriter(fixture, fixture, fixture, nullableTypes, fixture.Relationships, null!, effects));
        Assert.Throws<ArgumentNullException>(() => new BoxedReceiverCallRewriter(fixture, fixture, fixture, nullableTypes, fixture.Relationships, fixture, null!));
        Assert.Throws<ArgumentNullException>(() => fixture.Rule().Rewrite(null!));
    }

    private static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    private static CilMethodBody Body(params CilInstruction[] instructions) =>
        new(Implementation, 4, [CliValueKind.I4], [.. instructions]);

    private sealed class Fixture(MethodDefinitionModel? implementation = null, bool assignable = true, bool isEnum = false) :
        ITypeDefinitionResolver, IMethodRepository, ICalledMethodResolver, IConstrainedCallTargetResolver
    {
        internal TestTypeRelationshipClassifier Relationships { get; } = new(assignable);

        internal BoxedReceiverCallRewriter Rule() =>
            new(this, this, this, new NullableTypeResolver(), Relationships, this, new StackEffectResolver(this));

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            new(ValueKey, "Fixture", "Value", true, [], [Implementation.Key]) { IsEnum = isEnum };

        public MethodDefinitionModel GetMethod(EntityKey key) => Declaration;

        public MethodInstanceModel? Resolve(CilInstruction instruction) => (instruction.Operand as CilOperand.MethodInstance)?.Value;

        public MethodDefinitionModel Resolve(ConstrainedCallTargetRequest request)
        {
            Assert.False(isEnum);
            Assert.Equal(Value, request.ConstrainedType);
            Assert.Same(Declaration, request.Declaration);
            Assert.Equal(AreaSignature, request.CallSignature);
            return implementation ?? Implementation;
        }
    }
}
