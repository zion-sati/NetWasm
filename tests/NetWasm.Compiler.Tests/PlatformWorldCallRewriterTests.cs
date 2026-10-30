using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class PlatformWorldCallRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("CoreLib");
    private static readonly EntityKey CallerType = Key(0x02000001);
    private static readonly EntityKey PlatformWorldType = Key(0x02000002);
    private static readonly EntityKey OtherType = Key(0x02000003);
    private static readonly MethodDefinitionModel Caller = new(
        Key(0x06000001),
        CallerType,
        "ComposeSchedulers",
        IsStatic: true,
        MethodSignatureModel.Create(CliValueKind.Void),
        1);

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void ReplacesTheReactorHostSwitchWithTheWorldValue(bool reactorHost, int expected)
    {
        var body = Body(CilOperation.Call);

        var rewritten = Rewriter(Switch(), reactorHost).Rewrite(body);

        Assert.NotSame(body, rewritten);
        Assert.Equal(CilOperation.LoadInt32, rewritten.Instructions[0].Operation);
        Assert.Equal(expected, Assert.IsType<CilOperand.ConstantI4>(rewritten.Instructions[0].Operand).Value);
        Assert.Equal(body.Instructions[1], rewritten.Instructions[1]);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void ReplacesTheEventLoopSwitchWithTheWorldValue(bool eventLoop, int expected)
    {
        var body = Body(CilOperation.Call);

        var rewritten = Rewriter(Switch("ImportsEventLoop"), reactorHost: !eventLoop, eventLoop).Rewrite(body);

        Assert.Equal(CilOperation.LoadInt32, rewritten.Instructions[0].Operation);
        Assert.Equal(expected, Assert.IsType<CilOperand.ConstantI4>(rewritten.Instructions[0].Operand).Value);
    }

    [Fact]
    public void RejectsNullBody()
    {
        Assert.Throws<ArgumentNullException>(() => Rewriter(Switch(), reactorHost: true).Rewrite(null!));
    }

    [Fact]
    public void LeavesUnresolvedCallUnchanged()
    {
        var body = Body(CilOperation.Call);

        Assert.Same(body, Rewriter(null, reactorHost: true).Rewrite(body));
    }

    [Fact]
    public void LeavesVirtualCallUnchanged()
    {
        var body = Body(CilOperation.CallVirtual);

        Assert.Same(body, Rewriter(Switch(), reactorHost: true).Rewrite(body));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void LeavesOtherMethodsUnchanged(int variant)
    {
        var target = Switch() with
        {
            Definition = variant switch
            {
                0 => Switch().Definition with { DeclaringType = OtherType },
                1 => Switch().Definition with { Name = "OtherSwitch" },
                _ => Switch().Definition with { IsStatic = false },
            },
        };
        var body = Body(CilOperation.Call);

        Assert.Same(body, Rewriter(target, reactorHost: true).Rewrite(body));
    }

    [Fact]
    public void ReactorHostRequiresBothBoundImports()
    {
        Assert.False(PlatformWorldCapabilities.None.ReactorHost);
        Assert.False(PlatformWorldCapabilities.From(ComponentBoundaryContract.Empty).ReactorHost);
        Assert.False(PlatformWorldCapabilities.From(Contract(Import("watch"))).ReactorHost);
        Assert.False(PlatformWorldCapabilities.From(Contract(Import("cancel"))).ReactorHost);
        Assert.False(PlatformWorldCapabilities.From(
            Contract(Import("watch"), Import("cancel") with { HasManagedBinding = false })).ReactorHost);
        Assert.False(PlatformWorldCapabilities.From(
            Contract(Import("watch"), Import("cancel", "netwasm:runtime/reactor-guest@1.0.0"))).ReactorHost);
        Assert.True(PlatformWorldCapabilities.From(Contract(Import("watch"), Import("cancel"))).ReactorHost);
        Assert.Throws<ArgumentNullException>(() => PlatformWorldCapabilities.From(null!));
    }

    [Fact]
    public void EventLoopRequiresPollAndTheMonotonicClock()
    {
        var poll = Import("poll", "wasi:io/poll@0.2.11");
        var subscribe = Import("subscribe-duration", "wasi:clocks/monotonic-clock@0.2.11");
        Assert.False(PlatformWorldCapabilities.None.EventLoop);
        Assert.False(PlatformWorldCapabilities.From(ComponentBoundaryContract.Empty).EventLoop);
        Assert.False(PlatformWorldCapabilities.From(Contract(poll)).EventLoop);
        Assert.False(PlatformWorldCapabilities.From(Contract(subscribe)).EventLoop);
        Assert.False(PlatformWorldCapabilities.From(
            Contract(poll with { HasManagedBinding = false }, subscribe)).EventLoop);
        Assert.False(PlatformWorldCapabilities.From(
            Contract(Import("poll", "wasi:io/poll@0.2.0"), subscribe)).EventLoop);
        var both = PlatformWorldCapabilities.From(Contract(poll, subscribe));
        Assert.True(both.EventLoop);
        Assert.False(both.ReactorHost);
    }

    private static PlatformWorldCallRewriter Rewriter(
        MethodInstanceModel? target, bool reactorHost, bool eventLoop = false) => new(
        new FixedMethodResolver(target),
        new Symbols(),
        new PlatformWorldCapabilities(reactorHost, eventLoop));

    private static CilMethodBody Body(CilOperation callOperation) => new(
        Caller,
        1,
        [],
        [
            new(0, 1, callOperation, new CilOperand.None()),
            new(1, 2, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(3)),
            new(2, 3, CilOperation.Return, new CilOperand.None()),
            new(3, 4, CilOperation.Return, new CilOperand.None()),
        ]);

    private static MethodInstanceModel Switch(string name = "ImportsReactorHost")
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4);
        var definition = new MethodDefinitionModel(
            Key(0x06000002),
            PlatformWorldType,
            name,
            IsStatic: true,
            signature,
            0);
        return new(
            definition,
            CliTypeIdentity.Named(Assembly, "System.Runtime.InteropServices", "PlatformWorld", isValueType: false),
            [],
            signature);
    }

    private static ComponentBoundaryContract Contract(params CanonicalAbiFunction[] imports) =>
        new("netwasm:platform@1.0.0", "async-platform", [.. imports], []);

    private static CanonicalAbiFunction Import(
        string function,
        string interfaceName = "netwasm:runtime/reactor-host@1.0.0") =>
        new(interfaceName, function, Key(0x06000010), [], null);

    private static EntityKey Key(int token) => new(Assembly, token);

    private sealed class FixedMethodResolver(MethodInstanceModel? method) : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => method;
    }

    private sealed class Symbols : ISymbolFormatter
    {
        public string Format(EntityKey key) => key == PlatformWorldType
            ? "System.Runtime.InteropServices.PlatformWorld"
            : key == OtherType
                ? "Fixtures.Other"
                : "Fixtures.Caller";

        public string Format(MethodDefinitionModel method) =>
            $"{Format(method.DeclaringType)}::{method.Name}";
    }
}
