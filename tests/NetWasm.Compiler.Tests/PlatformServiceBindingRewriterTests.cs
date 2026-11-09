using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.PlatformServices;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class PlatformServiceBindingRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("CoreLib");
    private static readonly EntityKey CallerType = Key(0x02000001);
    private static readonly EntityKey BindingsType = Key(0x02000002);
    private static readonly EntityKey ClockType = Key(0x02000003);
    private static readonly MethodDefinitionModel Caller = new(
        Key(0x06000001),
        CallerType,
        "get_SystemClock",
        IsStatic: true,
        MethodSignatureModel.Create(CliValueKind.ManagedReference),
        1);
    private static readonly MethodInstanceModel Constructor = Method(ClockType, ".ctor", isStatic: false);

    [Fact]
    public void ReplacesThePlatformFactoryWithTheDeclaredPlatformsConstructor()
    {
        var rewritten = Rewriter(Factory("CreatePlatform")).Rewrite(Body(CilOperation.Call));

        Assert.Equal(CilOperation.NewObject, rewritten.Instructions[0].Operation);
        Assert.Same(Constructor, Assert.IsType<CilOperand.MethodInstance>(rewritten.Instructions[0].Operand).Value);
        Assert.Equal(CilOperation.Return, rewritten.Instructions[1].Operation);
    }

    [Fact]
    public void ReplacesThePlatformDescriptionWithTheDeclaredPlatformsName()
    {
        var rewritten = Rewriter(Factory("DescribeDeclaredPlatform")).Rewrite(Body(CilOperation.Call));

        Assert.Equal(CilOperation.LoadString, rewritten.Instructions[0].Operation);
        Assert.Equal(Description, Assert.IsType<CilOperand.UserString>(rewritten.Instructions[0].Operand).Value);
    }

    [Fact]
    public void LeavesOtherCallsAndUndeclaredProgramsUnchanged()
    {
        var body = Body(CilOperation.Call);

        Assert.Same(body, Rewriter(Factory("CreateClock")).Rewrite(body));
        Assert.Same(body, Rewriter(Method(CallerType, "CreatePlatform", isStatic: true)).Rewrite(body));
        Assert.Same(body, Rewriter(Method(BindingsType, "CreatePlatform", isStatic: false)).Rewrite(body));
        Assert.Same(body, Rewriter(null).Rewrite(body));
        var virtualCall = Body(CilOperation.CallVirtual);
        Assert.Same(virtualCall, Rewriter(Factory("CreatePlatform")).Rewrite(virtualCall));
        Assert.Same(body, new PlatformServiceBindingRewriter(
            new FixedMethodResolver(Factory("CreatePlatform")),
            new Symbols(),
            new PlatformWorldCapabilities(true, true, true)).Rewrite(body));
        Assert.Throws<ArgumentNullException>(() => Rewriter(null).Rewrite(null!));
    }

    private const string Description = "'Fixtures.Clock', declared in Fixtures";

    private static PlatformServiceBindingRewriter Rewriter(MethodInstanceModel? target) => new(
        new FixedMethodResolver(target),
        new Symbols(),
        new PlatformWorldCapabilities(true, true, true)
        {
            DeclaredPlatform = new DeclaredPlatform(Constructor, Description),
        });

    private static CilMethodBody Body(CilOperation callOperation) => new(
        Caller,
        1,
        [],
        [
            new(0, 1, callOperation, new CilOperand.None()),
            new(1, 2, CilOperation.Return, new CilOperand.None()),
        ]);

    private static MethodInstanceModel Factory(string name) => Method(BindingsType, name, isStatic: true);

    private static MethodInstanceModel Method(EntityKey type, string name, bool isStatic)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.ManagedReference);
        var definition = new MethodDefinitionModel(Key(0x06000002), type, name, isStatic, signature, 0);
        return new(
            definition,
            CliTypeIdentity.Named(Assembly, "System.Runtime.InteropServices", "PlatformServiceBindings", isValueType: false),
            [],
            signature);
    }

    private static EntityKey Key(int token) => new(Assembly, token);

    private sealed class FixedMethodResolver(MethodInstanceModel? method) : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => method;
    }

    private sealed class Symbols : ISymbolFormatter
    {
        public string Format(EntityKey key) => key == BindingsType
            ? "System.Runtime.InteropServices.PlatformServiceBindings"
            : key == ClockType
                ? "Fixtures.Clock"
                : "Fixtures.Caller";

        public string Format(MethodDefinitionModel method) =>
            $"{Format(method.DeclaringType)}::{method.Name}";
    }
}
