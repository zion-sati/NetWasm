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
}
