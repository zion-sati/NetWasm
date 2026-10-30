using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantRewriteTestSupport;

public sealed class StaticTypeObjectRewriterTests
{
    private static readonly CliTypeIdentity Shade = CliTypeIdentity.Named(Assembly, "Fixture", "Shade", true);
    private static readonly CliTypeIdentity Leaf = CliTypeIdentity.Named(Assembly, "Fixture", "Leaf", false);
    private static readonly CliTypeIdentity Text =
        CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);

    // typeof's token and materialization share a block, so the materialization names the type.
    [Fact]
    public void TypeofNamesItsTypeWithinOneBlock()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(Shade)),
            Instruction(1, CilOperation.Nop),
            Instruction(2, CilOperation.MaterializeType),
            Instruction(3, CilOperation.Return));

        var result = new ConstantTypeMaterializationRewriter(Graph(body)).Rewrite(body);

        Assert.Equal(new CilOperand.TypeIdentity(Shade), result.Instructions[2].Operand);
        Assert.True(CilTypeMaterialization.IsConstant(result.Instructions[2]));
    }

    // c ? typeof(A) : typeof(B) reaches one materialization from two tokens.
    [Fact]
    public void AMaterializationTwoTokensReachStaysDynamic()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
            Instruction(2, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(Shade)),
            Instruction(3, CilOperation.Branch, new CilOperand.BranchTarget(5)),
            Instruction(4, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(Leaf)),
            Instruction(5, CilOperation.MaterializeType),
            Instruction(6, CilOperation.Return));

        var result = new ConstantTypeMaterializationRewriter(Graph(body)).Rewrite(body);

        Assert.Same(body, result);
    }

    // A value boxed from a constant, local or argument has the box's type, so
    // GetType becomes typeof and the box goes.
    [Theory]
    [InlineData(CilOperation.LoadInt32)]
    [InlineData(CilOperation.LoadLocal)]
    [InlineData(CilOperation.LoadArgument)]
    public void GetTypeOnABoxedValueBecomesTypeof(CilOperation value)
    {
        var body = Body(
            Instruction(0, value, value == CilOperation.LoadInt32 ? new CilOperand.ConstantI4(1) : new CilOperand.Index(0)),
            Instruction(1, CilOperation.Box, new CilOperand.TypeIdentity(Shade)),
            Instruction(2, CilOperation.GetObjectType),
            Instruction(3, CilOperation.Return));

        var result = Rule(body).Rewrite(body);

        Assert.Equal(
            [CilOperation.Nop, CilOperation.LoadTypeToken, CilOperation.MaterializeType, CilOperation.Return],
            result.Instructions.Select(static instruction => instruction.Operation));
        Assert.Equal(new CilOperand.TypeIdentity(Shade), result.Instructions[1].Operand);
    }

    // A boxed nullable is its value's type or null, and a static field load may
    // run a type initializer, so both keep the runtime lookup.
    [Theory]
    [InlineData(true, CilOperation.LoadArgument)]
    [InlineData(false, CilOperation.LoadStaticField)]
    public void GetTypeOnANullableOrObservedBoxStaysDynamic(bool nullable, CilOperation value)
    {
        var boxed = nullable
            ? CliTypeIdentity.GenericInstantiation(CliTypeIdentity.Named(Assembly, "System", "Nullable`1", true), [Shade])
            : Shade;
        var body = Body(
            Instruction(0, value, value == CilOperation.LoadArgument
                ? new CilOperand.Index(0)
                : new CilOperand.FieldInstance(Field(Shade))),
            Instruction(1, CilOperation.Box, new CilOperand.TypeIdentity(boxed)),
            Instruction(2, CilOperation.GetObjectType),
            Instruction(3, CilOperation.Return));

        Assert.Same(body, Rule(body).Rewrite(body));
    }

    // A string, a sealed class or a new object can have one type only; the
    // receiver keeps its null check and GetType names that type.
    [Theory]
    [InlineData("string")]
    [InlineData("sealed field")]
    [InlineData("new object")]
    public void GetTypeOnAReceiverOfOneTypeNamesIt(string receiver)
    {
        var facts = new MethodFacts { Definition = new(TypeKey, "Fixture", "Leaf", false, [], []) { IsSealed = true } };
        var constructor = new MethodInstanceModel(Method(2).Definition with { IsStatic = false, Name = ".ctor" }, Leaf, [],
            MethodSignatureModel.Create(CliValueKind.Void));
        var (load, expected) = receiver switch
        {
            "string" => (Instruction(0, CilOperation.LoadString, new CilOperand.UserString("text")), Text),
            "sealed field" => (Instruction(0, CilOperation.LoadStaticField, new CilOperand.FieldInstance(Field(Leaf))), Leaf),
            _ => (Instruction(0, CilOperation.NewObject, new CilOperand.MethodInstance(constructor)), Leaf),
        };
        var body = Body(load, Instruction(1, CilOperation.GetObjectType), Instruction(2, CilOperation.Return));

        var result = Rule(body, facts).Rewrite(body);

        Assert.Equal(load, result.Instructions[0]);
        Assert.Equal(new CilOperand.ExactType(expected), result.Instructions[1].Operand);
        Assert.True(CilTypeMaterialization.IsExactObjectType(result.Instructions[1]));
    }

    // A field of an unsealed class may hold a derived object.
    [Fact]
    public void GetTypeOnAnUnsealedFieldStaysDynamic()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadStaticField, new CilOperand.FieldInstance(Field(Leaf))),
            Instruction(1, CilOperation.GetObjectType),
            Instruction(2, CilOperation.Return));

        Assert.Same(body, Rule(body).Rewrite(body));
    }

    // Only the dynamic forms allocate a Type object; a receiver of one type can still be null.
    [Fact]
    public void StaticTypeObjectsAreNotSafepoints()
    {
        var dynamicType = Instruction(0, CilOperation.MaterializeType);
        var constant = Instruction(0, CilOperation.MaterializeType, new CilOperand.TypeIdentity(Shade));
        var objectType = Instruction(0, CilOperation.GetObjectType);
        var exact = Instruction(0, CilOperation.GetObjectType, new CilOperand.ExactType(Leaf));

        Assert.True(CilSafepointClassifier.RequiresUnconditionalRootDecision(dynamicType));
        Assert.True(CilSafepointClassifier.RequiresUnconditionalRootDecision(objectType));
        Assert.False(CilSafepointClassifier.RequiresUnconditionalRootDecision(constant));
        Assert.False(CilSafepointClassifier.RequiresUnconditionalRootDecision(exact));
        Assert.True(CilSafepointClassifier.MayTransferControlExceptionally(dynamicType));
        Assert.False(CilSafepointClassifier.MayTransferControlExceptionally(constant));
        Assert.True(CilSafepointClassifier.MayTransferControlExceptionally(exact));
    }

    private static ExactObjectTypeRewriter Rule(CilMethodBody body, MethodFacts? facts = null)
    {
        facts ??= new MethodFacts();
        return new(Graph(body), facts, facts, facts, new NullableTypeResolver());
    }

    private static FieldInstanceModel Field(CliTypeIdentity type) => new(
        new FieldDefinitionModel(new(Assembly, 0x04000001), TypeKey, "s_value", type.StackKind, true) { SignatureType = type },
        Owner,
        type);
}
