using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

// receiver; ldftn M; newobj D(object, native int); call Delegate.Method folds to M's MethodInfo. A read of another
// member, or a creation that is not a delegate constructor, keeps its instructions.
public sealed class DelegateMethodRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("DelegateFixture");
    private static readonly EntityKey Owner = new(Assembly, 0x02000001);
    private static readonly EntityKey Delegate = new(Assembly, 0x02000002);
    private static readonly EntityKey Reflection = new(Assembly, 0x02000003);
    private static readonly EntityKey Other = new(Assembly, 0x02000004);

    [Theory]
    [InlineData("delegate", true)]
    [InlineData("reflection", true)]
    [InlineData("delegate member", false)]
    [InlineData("reflection member", false)]
    [InlineData("other type", false)]
    [InlineData("unresolved read", false)]
    [InlineData("unresolved creation", false)]
    [InlineData("not a constructor", false)]
    [InlineData("constructor shape", false)]
    public void OnlyADelegateMethodReadOfANewDelegateFolds(string scenario, bool folds)
    {
        var target = Method(0x06000001, Owner, "Target", MethodSignatureModel.Create(CliValueKind.Void));
        var (readOwner, readName) = scenario switch
        {
            "reflection" => (Reflection, "GetMethodInfo"),
            "delegate member" => (Delegate, "get_Target"),
            "reflection member" => (Reflection, "GetRuntimeMethod"),
            "other type" => (Other, "get_Method"),
            _ => (Delegate, "get_Method"),
        };
        var read = Method(0x06000002, readOwner, readName, MethodSignatureModel.Create(CliValueKind.ManagedReference));
        var creation = Method(0x06000003, Delegate, scenario == "not a constructor" ? "Create" : ".ctor",
            scenario == "constructor shape"
                ? MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.ManagedReference, CliValueKind.ManagedReference)
                : MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.ManagedReference, CliValueKind.NativeInt));
        CilOperand Operand(MethodInstanceModel method, string unresolved) =>
            scenario == unresolved ? new CilOperand.None() : new CilOperand.MethodInstance(method);
        var body = new CilMethodBody(target.Definition, 2, [],
        [
            new(0, 1, CilOperation.LoadNull, new CilOperand.None()),
            new(1, 2, CilOperation.LoadFunction, new CilOperand.MethodInstance(target)),
            new(2, 3, CilOperation.NewObject, Operand(creation, "unresolved creation")),
            new(3, 4, CilOperation.Call, Operand(read, "unresolved read")),
            new(4, 5, CilOperation.Pop, new CilOperand.None()),
            new(5, 6, CilOperation.Return, new CilOperand.None()),
        ]);
        var rewriter = new DelegateMethodRewriter(new ControlFlowGraphBuilderFactory().Create(),
            new OperandMethods(), new Names());

        var rewritten = rewriter.Rewrite(body);

        if (!folds)
        {
            Assert.Same(body, rewritten);
            return;
        }
        Assert.Equal([CilOperation.Nop, CilOperation.LoadMethodToken, CilOperation.LoadTypeToken, CilOperation.MaterializeMethod,
            CilOperation.Pop, CilOperation.Return], rewritten.Instructions.Select(instruction => instruction.Operation));
    }

    private static MethodInstanceModel Method(int token, EntityKey owner, string name, MethodSignatureModel signature)
    {
        var definition = new MethodDefinitionModel(new(Assembly, token), owner, name, name != ".ctor", signature, 0);
        return new(definition, CliTypeIdentity.Named(Assembly, "Fixture", "Owner", false), [], signature);
    }

    private sealed class OperandMethods : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => (instruction.Operand as CilOperand.MethodInstance)?.Value;
    }

    private sealed class Names : ISymbolFormatter
    {
        public string Format(EntityKey key) =>
            key == Delegate ? "System.Delegate" : key == Reflection ? "System.Reflection.RuntimeReflectionExtensions" : "Fixture.Other";

        public string Format(MethodDefinitionModel method) => method.Name;
    }
}
