using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// The rewriter looks for an entry jump to a test of the counter against a bound
// and a branch back. A jump to a counter that the method then returns ends the
// body there, with no bound after it.
public sealed class ArrayLoopIdiomRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);

    [Fact]
    public void DeclinesAJumpToACounterTheMethodReturns()
    {
        var body = Body(new CilBuilder()
            .Branch(CilOperation.Branch, "test")
            .Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadInt32, 0)
            .Op(CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(Int32))
            .Label("test").Local(CilOperation.LoadLocal, 0).Return());

        Assert.Same(body, new ArrayLoopIdiomRewriter(new NoMethods(), enabled: true).Rewrite(body));
    }

    // A local stored once from new int[length] ahead of any branch holds `length`
    // elements wherever the loop reads it, so a loop from 0 to 10 over it is in
    // bounds when length is at least 10.
    [Theory]
    [InlineData(16, "FillInBounds")]
    [InlineData(10, "FillInBounds")]
    [InlineData(9, "Fill")]
    public void FillsALocalOfAKnownLengthWithoutChecksWhenTheLoopFitsIt(int length, string expected)
    {
        var body = LocalsBody(new CilBuilder()
            .Op(CilOperation.LoadInt32, length).Op(CilOperation.NewArray, new CilOperand.TypeIdentity(Int32))
            .Local(CilOperation.StoreLocal, 1)
            .Loop(0, Fill(1))
            .Local(CilOperation.LoadLocal, 0).Return());

        Assert.Equal(expected, Called(body));
    }

    public static TheoryData<string> UnknownLengths => ["argument", "stored twice", "addressed", "after a branch"];

    // The length is unknown, so the fill keeps its checks, when the array is an
    // argument, or a local that is stored again, addressed, or stored after a
    // branch, where another path could read it unstored.
    [Theory]
    [MemberData(nameof(UnknownLengths))]
    public void KeepsTheChecksWhereTheLengthIsUnknown(string shape)
    {
        var code = new CilBuilder();
        if (shape == "after a branch")
        {
            code.Local(CilOperation.LoadArgument, 0).Branch(CilOperation.BranchIfFalse, "allocate").Label("allocate");
        }
        code.Op(CilOperation.LoadInt32, 16).Op(CilOperation.NewArray, new CilOperand.TypeIdentity(Int32))
            .Local(CilOperation.StoreLocal, 1);
        if (shape == "stored twice")
        {
            code.Local(CilOperation.LoadArgument, 0).Local(CilOperation.StoreLocal, 1);
        }
        if (shape == "addressed")
        {
            code.Local(CilOperation.LoadLocalAddress, 1).Op(CilOperation.Pop);
        }
        code.Loop(0, shape == "argument"
            ? loop => loop.Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadInt32, 7)
                .Op(CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(Int32))
            : Fill(1));

        Assert.Equal("Fill", Called(LocalsBody(code.Local(CilOperation.LoadLocal, 0).Return())));
    }

    // A copy is in bounds when both arrays are locals of known lengths that hold
    // the loop's iterations.
    [Theory]
    [InlineData(16, 16, "CopyInBounds")]
    [InlineData(16, 9, "Copy")]
    public void CopiesBetweenLocalsOfKnownLengthsWithoutChecks(int destination, int source, string expected)
    {
        var body = LocalsBody(new CilBuilder()
            .Op(CilOperation.LoadInt32, destination).Op(CilOperation.NewArray, new CilOperand.TypeIdentity(Int32))
            .Local(CilOperation.StoreLocal, 1)
            .Op(CilOperation.LoadInt32, source).Op(CilOperation.NewArray, new CilOperand.TypeIdentity(Int32))
            .Local(CilOperation.StoreLocal, 2)
            .Loop(0, loop => loop.Local(CilOperation.LoadLocal, 1).Local(CilOperation.LoadLocal, 0)
                .Local(CilOperation.LoadLocal, 2).Local(CilOperation.LoadLocal, 0)
                .Op(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Int32))
                .Op(CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(Int32)))
            .Local(CilOperation.LoadLocal, 0).Return());

        Assert.Equal(expected, Called(body));
    }

    // array[counter] = 7
    private static Func<CilBuilder, CilBuilder> Fill(int array) => loop => loop
        .Local(CilOperation.LoadLocal, array).Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadInt32, 7)
        .Op(CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(Int32));

    // The ArrayLoops helper the rewritten body calls.
    private static string Called(CilMethodBody body) =>
        Assert.IsType<CilOperand.MethodInstance>(
            new ArrayLoopIdiomRewriter(new NamedMethods(), enabled: true).Rewrite(body).Instructions
                .Single(instruction => instruction.Operation == CilOperation.Call).Operand).Value.Definition.Name;

    // static int Run(int[] array) with an int counter and two int[] locals.
    private static CilMethodBody LocalsBody(CilBuilder code) =>
        new(new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Run", true,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.ManagedReference), 1),
            4, [CliValueKind.I4, CliValueKind.ManagedReference, CliValueKind.ManagedReference], code.Build())
        { LocalSignatureTypes = [Int32, CliTypeIdentity.SzArray(Int32), CliTypeIdentity.SzArray(Int32)] };

    // static int Run(int[] array)
    private static CilMethodBody Body(CilBuilder code) =>
        new(new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Run", true,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.ManagedReference), 1),
            3, [CliValueKind.I4], code.Build())
        { LocalSignatureTypes = [Int32] };

    private sealed class NoMethods : IArrayLoopMethodResolver
    {
        public MethodInstanceModel Resolve(string name, CliTypeIdentity element) => throw new NotSupportedException();
    }

    private sealed class NamedMethods : IArrayLoopMethodResolver
    {
        public MethodInstanceModel Resolve(string name, CliTypeIdentity element)
        {
            var signature = MethodSignatureModel.Create(CliValueKind.I4);
            return new(new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), name, true, signature, 0),
                CliTypeIdentity.Named(Assembly, "System.Runtime.CompilerServices", "ArrayLoops", isValueType: false), [element],
                signature);
        }
    }
}
