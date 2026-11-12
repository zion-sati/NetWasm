using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

// Assembly.GetExecutingAssembly() compiles to RuntimeAssembly.Of<TDeclaring>() for the calling method's
// declaring type; other calls stay as they are.
public sealed class ExecutingAssemblyCallRewriterTests
{
    private static readonly AssemblyIdentity CoreLib = new("NetWasm.CoreLib");
    private static readonly AssemblyIdentity App = new("App");
    private static readonly EntityKey AssemblyType = new(CoreLib, 0x02000001);
    private static readonly EntityKey RuntimeAssemblyType = new(CoreLib, 0x02000002);
    private static readonly EntityKey CallerType = new(App, 0x02000001);
    private static readonly EntityKey OfKey = new(CoreLib, 0x06000002);
    private static readonly CliTypeIdentity Caller = CliTypeIdentity.Named(App, "App", "Caller", false);
    private static readonly CliTypeIdentity RuntimeAssembly =
        CliTypeIdentity.Named(CoreLib, "System.Reflection", "RuntimeAssembly", false);

    [Theory]
    [InlineData("GetEntryAssembly", true, false)]
    [InlineData("GetExecutingAssembly", false, false)]
    [InlineData("GetExecutingAssembly", true, true)]
    public void LeavesEveryOtherCall(string name, bool isStatic, bool parameter)
    {
        var body = Body(null, CilOperation.Call);
        var calls = new Calls(name, isStatic, parameter, AssemblyType);

        Assert.Same(body, Rewriter(calls, new Instances()).Rewrite(body));
        Assert.Same(body, Rewriter(new Calls("GetExecutingAssembly", true, false, CallerType), new Instances()).Rewrite(body));
        var virtualCall = Body(null, CilOperation.CallVirtual);
        Assert.Same(virtualCall, Rewriter(new Calls("GetExecutingAssembly", true, false, AssemblyType), new Instances())
            .Rewrite(virtualCall));
    }

    // A generic method's declaring type is its closed instance's; another method's is its definition's.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallsTheCallingTypesAssembly(bool generic)
    {
        var instances = new Instances();
        var closed = CliTypeIdentity.GenericInstantiation(Caller, [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var body = Body(generic ? closed : null, CilOperation.Call);

        var rewritten = Rewriter(new Calls("GetExecutingAssembly", true, false, AssemblyType), instances).Rewrite(body);

        var call = Assert.Single(rewritten.Instructions);
        Assert.Equal(CilOperation.Call, call.Operation);
        Assert.Same(instances.Resolved, Assert.IsType<CilOperand.MethodInstance>(call.Operand).Value);
        Assert.Equal((CoreLib, OfKey.MetadataToken), instances.Requested);
        Assert.Equal([generic ? closed : Caller], instances.Context?.MethodArguments);
    }

    [Fact]
    public void RejectsAMissingBody() =>
        Assert.Throws<ArgumentNullException>(() =>
            Rewriter(new Calls("GetExecutingAssembly", true, false, AssemblyType), new Instances()).Rewrite(null!));

    private static ExecutingAssemblyCallRewriter Rewriter(Calls calls, Instances instances)
    {
        var definitions = new Definitions();
        return new(calls, definitions, definitions, instances, new Symbols(), new Identities());
    }

    private static CilMethodBody Body(CliTypeIdentity? declaringType, CilOperation operation)
    {
        var method = new MethodDefinitionModel(new EntityKey(App, 0x06000009), CallerType, "Run", true,
            MethodSignatureModel.Create(CliValueKind.Void), 0);
        return new CilMethodBody(method, 1, [], [new CilInstruction(0, 1, operation, new CilOperand.None())])
        {
            MethodInstance = declaringType is null ? null : new MethodInstanceModel(method, declaringType, [], method.Signature),
        };
    }

    // Offset 0 calls a static or instance method of the given name, with or without a parameter.
    private sealed class Calls(string name, bool isStatic, bool parameter, EntityKey declaringType) : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction)
        {
            var signature = parameter
                ? MethodSignatureModel.Create(CliValueKind.ManagedReference, CliValueKind.ManagedReference)
                : MethodSignatureModel.Create(CliValueKind.ManagedReference);
            var definition = new MethodDefinitionModel(new EntityKey(CoreLib, 0x06000001), declaringType, name, isStatic, signature, 0);
            return new(definition, CliTypeIdentity.Named(CoreLib, "System.Reflection", "Assembly", false), [], signature);
        }
    }

    private sealed class Definitions : ITypeDefinitionResolver, IMethodRepository
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => identity.Equals(RuntimeAssembly)
            ? new(RuntimeAssemblyType, "System.Reflection", "RuntimeAssembly", false, [], [new EntityKey(CoreLib, 0x06000003), OfKey])
            : throw new InvalidOperationException(identity.ToString());

        public MethodDefinitionModel GetMethod(EntityKey key) => new(key, RuntimeAssemblyType,
            key == OfKey ? "Of" : "FromIndex", true, MethodSignatureModel.Create(CliValueKind.ManagedReference), 0);
    }

    private sealed class Instances : IMethodInstanceResolver
    {
        internal MethodInstanceModel Resolved { get; } = new(
            new MethodDefinitionModel(OfKey, RuntimeAssemblyType, "Of", true, MethodSignatureModel.Create(CliValueKind.ManagedReference), 0),
            RuntimeAssembly,
            [Caller],
            MethodSignatureModel.Create(CliValueKind.ManagedReference));

        internal (AssemblyIdentity, int)? Requested { get; private set; }

        internal CliGenericContext? Context { get; private set; }

        public MethodInstanceModel ResolveMethodInstance(
            AssemblyIdentity source, int metadataToken, string methodDisplayName, int ilOffset,
            CliGenericContext? genericContext = null)
        {
            Requested = (source, metadataToken);
            Context = genericContext;
            return Resolved;
        }
    }

    private sealed class Symbols : ISymbolFormatter
    {
        public string Format(EntityKey key) => key == AssemblyType ? "System.Reflection.Assembly" : "App.Caller";

        public string Format(MethodDefinitionModel method) => method.Name;
    }

    private sealed class Identities : ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey type) => type == CallerType ? Caller : throw new InvalidOperationException();
    }
}

// A type's assembly as Type.Assembly reports it.
public sealed class RuntimeTypeAssemblyResolverTests
{
    private static readonly AssemblyIdentity CoreLib = new("NetWasm.CoreLib");
    private static readonly AssemblyIdentity App = new("App");
    private static readonly AssemblyIdentity Forwarded = new("Forwarded");
    private static readonly CliTypeIdentity Sample = CliTypeIdentity.Named(App, "App", "Sample", false);
    private static readonly CliTypeIdentity Moved = CliTypeIdentity.Named(App, "App", "Moved", false);
    private static readonly CliTypeIdentity Box = CliTypeIdentity.Named(CoreLib, "System", "Box`1", false);

    [Fact]
    public void RejectsMissingInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeAssemblyResolver(null!));
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeAssemblyResolver(new Definitions()).Resolve(null!));
    }

    [Fact]
    public void TheFactoryResolvesOverTheGivenDefinitions()
    {
        var resolver = new RuntimeTypeAssemblyResolverFactory().Create(new Definitions());

        Assert.Equal(App, resolver.Resolve(Sample));
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeAssemblyResolverFactory().Create(null!));
    }

    [Fact]
    public void ADefinitionReportsTheAssemblyThatDefinesIt()
    {
        var resolver = new RuntimeTypeAssemblyResolver(new Definitions());

        Assert.Equal(App, resolver.Resolve(Sample));
        Assert.Equal(Forwarded, resolver.Resolve(Moved));
        Assert.Equal(CoreLib, resolver.Resolve(CliTypeIdentity.Primitive("i4", CliValueKind.I4)));
    }

    [Fact]
    public void ACompositeTypeReportsItsElementOrDefinitionsAssembly()
    {
        var resolver = new RuntimeTypeAssemblyResolver(new Definitions());

        Assert.Equal(App, resolver.Resolve(CliTypeIdentity.SzArray(Sample)));
        Assert.Equal(App, resolver.Resolve(CliTypeIdentity.Array(Sample, 2)));
        Assert.Equal(App, resolver.Resolve(CliTypeIdentity.UnmanagedPointer(Sample)));
        Assert.Equal(App, resolver.Resolve(CliTypeIdentity.ManagedByReference(Sample)));
        Assert.Equal(CoreLib, resolver.Resolve(CliTypeIdentity.GenericInstantiation(Box, [Sample])));
    }

    [Fact]
    public void AFunctionPointerTypeIsCoreLibs() =>
        Assert.Equal(CoreLib, new RuntimeTypeAssemblyResolver(new Definitions()).Resolve(
            CliTypeIdentity.FunctionPointer(new(0, 0, 0, MethodSignatureModel.Create(CliValueKind.Void)))));

    [Fact]
    public void AGenericParameterHasNoAssemblyOfItsOwn() =>
        Assert.Equal(DiagnosticCode.RuntimeContract, Assert.Throws<CompilerException>(() =>
            new RuntimeTypeAssemblyResolver(new Definitions()).Resolve(CliTypeIdentity.GenericParameter(method: false, 0)))
            .Diagnostic.Code);

    private sealed class Definitions : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => new(
            new EntityKey(identity.Equals(Moved) ? Forwarded : identity.Assembly ?? CoreLib, 1),
            "Fixtures", identity.CanonicalName, false, [], []);
    }
}
