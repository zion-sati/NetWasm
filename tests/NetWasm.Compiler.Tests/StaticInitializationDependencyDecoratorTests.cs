using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class StaticInitializationDependencyDecoratorTests
{
    [Fact]
    public void FactoryIncludesFailureDependenciesForAnInstructionlessInitializer()
    {
        var metadata = new Metadata();
        var factory = Assert.IsAssignableFrom<IReachabilityInstructionAnalyzerFactory>(
            new ReachabilityInstructionAnalyzerFactory(null!, null!));
        // Instruction-specific collaborators are deliberately unused for an empty body.
        var analyzer = factory.Create(metadata, metadata, metadata, null!, null!, null!, null!);
        var owner = CliTypeIdentity.Named(Metadata.Assembly, "Example", "Owner", false);
        var method = Metadata.Method(7, ".cctor", owner);

        var result = analyzer.Analyze(new(method, new(method.Definition, 0, [], [])));

        Assert.Equal([Metadata.Exception], result.Types.ToArray());
        Assert.Empty(result.Methods);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitializersRetainOnlyCatchTypeWithoutAllocationsNamesOrConstructors(bool constructed)
    {
        var metadata = new Metadata();
        var inner = new RecordingInstructions();
        var decorator = Assert.IsAssignableFrom<IReachabilityInstructionAnalyzer>(
            new StaticInitializationDependencyDecorator(inner, metadata));
        var owner = CliTypeIdentity.Named(Metadata.Assembly, "Example", "Owner", false);
        if (constructed)
            owner = CliTypeIdentity.GenericInstantiation(owner, [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);
        var method = Metadata.Method(7, ".cctor", owner);
        var request = new ReachabilityInstructionRequest(method, new(method.Definition, 0, [], []));

        var result = decorator.Analyze(request);

        Assert.Same(request, inner.Request);
        Assert.Equal([Metadata.Exception], result.Types.ToArray());
        Assert.Empty(result.AllocatedTypes);
        Assert.Empty(result.Strings);
        Assert.Empty(result.Methods);
        Assert.Equal(1, metadata.Lookups);
    }

    [Fact]
    public void OrdinaryMethodsHaveNoFailureDependencyAndNullRequestFailsBeforeDelegation()
    {
        var metadata = new Metadata();
        var inner = new RecordingInstructions();
        var decorator = Assert.IsAssignableFrom<IReachabilityInstructionAnalyzer>(
            new StaticInitializationDependencyDecorator(inner, metadata));
        Assert.Throws<ArgumentNullException>(() => decorator.Analyze(null!));
        Assert.Null(inner.Request);
        var method = Metadata.Method(7, "Read", CliTypeIdentity.Named(Metadata.Assembly, "Example", "Owner", false));
        Assert.Same(inner.Result, decorator.Analyze(new(method, new(method.Definition, 0, [], []))));
        Assert.Equal(0, metadata.Lookups);
    }

    private sealed class RecordingInstructions : IReachabilityInstructionAnalyzer
    {
        public ReachabilityInstructionAnalysis Result { get; } = new([], [], [], [], [], [], [], [], [], [], []);
        public ReachabilityInstructionRequest? Request { get; private set; }
        public ReachabilityInstructionAnalysis Analyze(ReachabilityInstructionRequest request)
        {
            Request = request;
            return Result;
        }
    }

    private sealed class Metadata : ITypeFinder, ITypeIdentityResolver, ICalledMethodResolver
    {
        public static readonly AssemblyIdentity Assembly = new("in-memory");
        public static readonly EntityKey Wrapper = new(Assembly, 11);
        public static readonly EntityKey Exception = new(Assembly, 12);
        public int Lookups { get; private set; }
        public TypeDefinitionModel FindType(string fullName)
        {
            Lookups++;
            return fullName == "System.TypeInitializationException"
                ? new(Wrapper, "System", "TypeInitializationException", false, [], [new(Assembly, 1), new(Assembly, 2), new(Assembly, 3)])
                : new(Exception, "System", "Exception", false, [], []);
        }
        public CliTypeIdentity GetTypeIdentity(EntityKey type) =>
            CliTypeIdentity.Named(Assembly, "System", "TypeInitializationException", false);
        public MethodInstanceModel? Resolve(CilInstruction instruction)
        {
            var token = ((CilOperand.Entity)instruction.Operand).Key.MetadataToken;
            return token == 1 ? null : Method(token, ".ctor", GetTypeIdentity(Wrapper), token == 3 ? 2 : 0);
        }
        public static MethodInstanceModel Method(int token, string name, CliTypeIdentity owner, int parameters = 0)
        {
            var definition = new MethodDefinitionModel(new(Assembly, token), Wrapper, name, name != ".ctor",
                MethodSignatureModel.Create(CliValueKind.Void, Enumerable.Repeat(CliValueKind.ManagedReference, parameters).ToArray()), 1);
            return new(definition, owner, [], definition.Signature);
        }
    }
}
