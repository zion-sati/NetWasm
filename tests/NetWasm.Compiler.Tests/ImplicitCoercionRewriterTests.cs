using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// Each int32 and native int coerced where ECMA-335 III.1.6 allows becomes an explicit
// conversion: after its producer when the consumer's block pushed it (the top copy a dup
// pushed included), or else just before the consumer, a call's later arguments, calli's
// function pointer among them, held in new locals of their declared types meanwhile. A
// body without coercions comes back as it is.
public sealed class ImplicitCoercionRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly CliTypeIdentity NativeInt = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
    private static readonly CliTypeIdentity Pair = CliTypeIdentity.Named(Assembly, "Fixture", "Pair", isValueType: true);
    private static readonly MethodSignatureModel SumSignature = new(Int32, [Int32, NativeInt, Pair]);
    private static readonly CilOperand.MethodInstance Sum = new(new MethodInstanceModel(
        new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), "Sum", true, SumSignature, 1),
        CliTypeIdentity.Named(Assembly, "Fixture", "Owner", isValueType: false), [], SumSignature));

    [Fact]
    public void RejectsMissingCollaboratorsAndBodies()
    {
        var validator = new TypedStackValidatorFactory(new StackTypeCompatibilityValidator())
            .Create(new Program(), new Program(), new Program());
        Assert.Throws<ArgumentNullException>(() => new ImplicitCoercionRewriter(null!, validator));
        Assert.Throws<ArgumentNullException>(() =>
            new ImplicitCoercionRewriter(new ControlFlowGraphBuilderFactory().Create(), null!));
        Assert.Throws<ArgumentNullException>(() => Rewriter().Rewrite(null!));
    }

    [Fact]
    public void LeavesABodyWithoutCoercionsAsItIs()
    {
        var body = Body(code => code.Local(CilOperation.LoadLocal, 0).Op(CilOperation.ConvertInt32)
            .Local(CilOperation.StoreLocal, 1).Return());

        Assert.Same(body, Rewriter().Rewrite(body));
    }

    [Fact]
    public void ConvertsEachArgumentAfterItsProducerInTheCallsBlock()
    {
        var body = Body(code => code.Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadLocal, 1)
            .Local(CilOperation.LoadLocal, 2).Op(CilOperation.Call, Sum).Op(CilOperation.Pop).Return());

        var rewritten = Rewriter().Rewrite(body);

        Assert.Equal(
            [
                Load(0), (CilOperation.ConvertInt32, None), Load(1), (CilOperation.ConvertNativeInt, None), Load(2),
                (CilOperation.Call, Sum), (CilOperation.Pop, None), (CilOperation.Return, None),
            ],
            Code(rewritten));
        Assert.Equal(body.LocalSignatureTypes, rewritten.LocalSignatureTypes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConvertsTheTopCopyADupPushedAfterItAndTheLowerBeforeItsStore(bool topCoerced)
    {
        var body = Body(code => code.Local(CilOperation.LoadLocal, 0).Op(CilOperation.Duplicate)
            .Local(CilOperation.StoreLocal, topCoerced ? 1 : 0).Local(CilOperation.StoreLocal, topCoerced ? 0 : 1)
            .Return());

        (CilOperation, CilOperand)[] expected = topCoerced
            ? [Load(0), (CilOperation.Duplicate, None), (CilOperation.ConvertInt32, None), Store(1), Store(0),
                (CilOperation.Return, None)]
            : [Load(0), (CilOperation.Duplicate, None), Store(0), (CilOperation.ConvertInt32, None), Store(1),
                (CilOperation.Return, None)];
        Assert.Equal(expected, Code(Rewriter().Rewrite(body)));
    }

    // The int32 argument comes from either of two paths, below a native int argument
    // given an int32 in the call's block and a struct; the call's int32 result is
    // stored in a native int local.
    [Fact]
    public void HoldsLaterArgumentsInLocalsWhileConvertingOnePushedOnTwoPaths()
    {
        var body = Body(code => code.Local(CilOperation.LoadArgument, 0).Branch(CilOperation.BranchIfTrue, "other")
            .Local(CilOperation.LoadLocal, 0).Branch(CilOperation.Branch, "join")
            .Label("other").Local(CilOperation.LoadLocal, 0)
            .Label("join").Local(CilOperation.LoadLocal, 1).Local(CilOperation.LoadLocal, 2)
            .Op(CilOperation.Call, Sum).Local(CilOperation.StoreLocal, 0).Return());

        var rewritten = Rewriter().Rewrite(body);

        Assert.Equal(
            [
                (CilOperation.LoadArgument, new CilOperand.Index(0)), (CilOperation.BranchIfTrue, Target(4)),
                Load(0), (CilOperation.Branch, Target(5)), Load(0),
                Load(1), (CilOperation.ConvertNativeInt, None), Load(2),
                Store(3), Store(4), (CilOperation.ConvertInt32, None), Load(4), Load(3),
                (CilOperation.Call, Sum), (CilOperation.ConvertNativeInt, None), Store(0), (CilOperation.Return, None),
            ],
            Code(rewritten));
        Assert.Equal([NativeInt, Int32, Pair, Pair, NativeInt], rewritten.LocalSignatureTypes);
        Assert.Equal([CliValueKind.NativeInt, CliValueKind.I4, CliValueKind.ValueType, CliValueKind.ValueType,
            CliValueKind.NativeInt], rewritten.Locals);
    }

    // Both paths push the first two arguments, so both convert at the call: the second,
    // held with the struct and calli's function pointer, before it is stored.
    [Fact]
    public void HoldsCallisFunctionPointerWithTheLaterArgumentsConvertingThoseCoerced()
    {
        var body = Body(code => code.Local(CilOperation.LoadArgument, 0).Branch(CilOperation.BranchIfTrue, "other")
            .Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadLocal, 1).Branch(CilOperation.Branch, "join")
            .Label("other").Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadLocal, 1)
            .Label("join").Local(CilOperation.LoadLocal, 2)
            .Op(CilOperation.LoadFunction, Sum).Op(CilOperation.CallIndirect, new CilOperand.CallSite(SumSignature))
            .Op(CilOperation.Pop).Return());

        var rewritten = Rewriter().Rewrite(body);

        Assert.Equal(
            [
                (CilOperation.LoadArgument, new CilOperand.Index(0)), (CilOperation.BranchIfTrue, Target(5)),
                Load(0), Load(1), (CilOperation.Branch, Target(7)), Load(0), Load(1), Load(2),
                (CilOperation.LoadFunction, Sum), Store(3), Store(4), (CilOperation.ConvertNativeInt, None), Store(5),
                (CilOperation.ConvertInt32, None), Load(5), Load(4), Load(3),
                (CilOperation.CallIndirect, new CilOperand.CallSite(SumSignature)), (CilOperation.Pop, None),
                (CilOperation.Return, None),
            ],
            Code(rewritten));
        Assert.Equal([NativeInt, Int32, Pair, NativeInt, Pair, NativeInt], rewritten.LocalSignatureTypes);
    }

    // A store's value is the only operand: a value pushed on two paths is converted at
    // the join, where both branches now land.
    [Fact]
    public void ConvertsATopValuePushedOnTwoPathsJustBeforeItsConsumer()
    {
        var body = Body(code => code.Local(CilOperation.LoadArgument, 0).Branch(CilOperation.BranchIfTrue, "other")
            .Local(CilOperation.LoadLocal, 0).Branch(CilOperation.Branch, "join")
            .Label("other").Local(CilOperation.LoadLocal, 0)
            .Label("join").Local(CilOperation.StoreLocal, 1).Return());

        var rewritten = Rewriter().Rewrite(body);

        Assert.Equal(
            [
                (CilOperation.LoadArgument, new CilOperand.Index(0)), (CilOperation.BranchIfTrue, Target(4)),
                Load(0), (CilOperation.Branch, Target(5)), Load(0), (CilOperation.ConvertInt32, None), Store(1),
                (CilOperation.Return, None),
            ],
            Code(rewritten));
        Assert.Equal(body.Locals, rewritten.Locals);
    }

    private static readonly CilOperand None = new CilOperand.None();

    private static (CilOperation, CilOperand) Load(int local) => (CilOperation.LoadLocal, new CilOperand.Index(local));

    private static (CilOperation, CilOperand) Store(int local) => (CilOperation.StoreLocal, new CilOperand.Index(local));

    private static CilOperand.BranchTarget Target(int index) => new(index);

    private static (CilOperation, CilOperand)[] Code(CilMethodBody body) =>
        [.. body.Instructions.Select(static instruction => (instruction.Operation, instruction.Operand))];

    private static ImplicitCoercionRewriter Rewriter()
    {
        var program = new Program();
        return new(new ControlFlowGraphBuilderFactory().Create(),
            new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program));
    }

    // Locals: a native int, an int32 and a struct; one int32 argument.
    private static CilMethodBody Body(Func<CilBuilder, CilBuilder> write)
    {
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Run", true,
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4), 1);
        CliTypeIdentity[] locals = [NativeInt, Int32, Pair];
        return new(method, 8, [.. locals.Select(static local => local.StackKind)], write(new CilBuilder()).Build())
        {
            LocalSignatureTypes = [.. locals],
        };
    }

    private sealed class Program : ITypeRepository, IFieldRepository, IMethodRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => throw new NotSupportedException();

        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();

        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }
}
