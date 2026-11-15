using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Inlining;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Inlining;

// A method whose function came out over an engine limit takes no inlining; every other caller keeps the plan of
// the resolver decorated.
public sealed class CallerExcludingInliningResolverTests
{
    private static readonly AssemblyIdentity App = new("App");
    private static readonly MethodInstanceModel Excluded = Method(0x06000001, "Big");
    private static readonly MethodInstanceModel Other = Method(0x06000002, "Small");
    private static readonly MethodInstanceModel Callee = Method(0x06000003, "Callee");
    private static readonly CilInstruction Call = new(0, 5, CilOperation.Call, new CilOperand.None());

    [Fact]
    public void AnExcludedCallerTakesNoInliningAndTheResolverIsNotAsked()
    {
        var inner = new RecordingResolver();
        var resolver = new CallerExcludingInliningResolver(inner, [Excluded.Definition.Key]);

        Assert.Null(resolver.Resolve(new(Excluded, Call) { Nested = true }));
        Assert.Empty(inner.Requests);
    }

    [Fact]
    public void AnotherCallerKeepsTheResolversPlan()
    {
        var inner = new RecordingResolver();
        var resolver = new CallerExcludingInliningResolver(inner, [Excluded.Definition.Key]);
        var request = new ManagedInliningRequest(Other, Call);

        Assert.Same(inner.Plan, resolver.Resolve(request));
        Assert.Equal([request], inner.Requests);
    }

    [Fact]
    public void ItRejectsMissingInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new CallerExcludingInliningResolver(null!, []));
        Assert.Throws<ArgumentNullException>(() => new CallerExcludingInliningResolver(new RecordingResolver(), null!));
        Assert.Throws<ArgumentNullException>(() =>
            new CallerExcludingInliningResolver(new RecordingResolver(), []).Resolve(null!));
    }

    private static MethodInstanceModel Method(int token, string name)
    {
        var signature = new MethodSignatureModel(CliValueKind.I4, []);
        return new(
            new MethodDefinitionModel(new EntityKey(App, token), new EntityKey(App, 0x02000002), name, IsStatic: true,
                signature, RelativeVirtualAddress: 0),
            CliTypeIdentity.Named(App, "App", "Program", isValueType: false),
            ImmutableArray<CliTypeIdentity>.Empty,
            signature);
    }

    private sealed class RecordingResolver : IManagedInliningResolver
    {
        public List<ManagedInliningRequest> Requests { get; } = [];

        public ManagedInliningPlan Plan { get; } = new(Callee,
            new CilMethodBody(Callee.Definition, 1, [], [new(0, 1, CilOperation.Return, new CilOperand.None())]));

        public ManagedInliningPlan? Resolve(ManagedInliningRequest request)
        {
            Requests.Add(request);
            return Plan;
        }
    }
}
