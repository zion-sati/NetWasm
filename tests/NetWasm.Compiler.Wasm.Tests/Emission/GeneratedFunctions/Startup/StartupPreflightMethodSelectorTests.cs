using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.Startup;

namespace NetWasm.Compiler.Wasm.Tests.Emission.GeneratedFunctions.Startup;

public sealed class StartupPreflightMethodSelectorTests
{
    private static readonly AssemblyIdentity Assembly = new("StartupTests");
    private static readonly EntityKey PlatformTypeKey = new(Assembly, 0x02000001);
    private static readonly EntityKey SidecarsTypeKey = new(Assembly, 0x02000002);
    private static readonly EntityKey OtherTypeKey = new(Assembly, 0x02000003);
    private static readonly EntityKey LocalTimeKey = new(Assembly, 0x06000001);
    private static readonly EntityKey TypeSymbolsKey = new(Assembly, 0x06000002);
    private static readonly EntityKey MethodSymbolsKey = new(Assembly, 0x06000003);

    // Symbols load first, so names and frames are ready for anything local time does.
    [Fact]
    public void SelectsReachablePreflightsInOrder()
    {
        var repositories = new Repositories(
            Method(LocalTimeKey, PlatformTypeKey, "EnsureLocalTimeReady"),
            Method(MethodSymbolsKey, SidecarsTypeKey, "LoadMethodSymbols"),
            Method(TypeSymbolsKey, SidecarsTypeKey, "LoadTypeSymbols"));

        var result = CreateSelector(repositories).Select(CreateRequest(LocalTimeKey, MethodSymbolsKey, TypeSymbolsKey));

        Assert.Equal([TypeSymbolsKey, MethodSymbolsKey, LocalTimeKey], result.Methods);
        Assert.True(result.UsesLocalTime);
    }

    // A module without a process entry initializes in its first export's task; a synchronous WIT
    // export's task may not wait, so a module with one reads no symbols files. One without (an async
    // export, or a core function) loads them, and so does a raw module, whose JavaScript host completes
    // the read.
    [Theory]
    [InlineData(false, null, true)]
    [InlineData(false, CanonicalAbiFunctionKind.Function, false)]
    [InlineData(false, CanonicalAbiFunctionKind.ExportedResourceDestructor, false)]
    [InlineData(true, CanonicalAbiFunctionKind.Function, true)]
    public void AModuleWithSynchronousExportsLoadsNoSymbolsWhenItInitializes(
        bool rawModule, CanonicalAbiFunctionKind? export, bool loadsSymbols)
    {
        var repositories = new Repositories(
            Method(LocalTimeKey, PlatformTypeKey, "EnsureLocalTimeReady"),
            Method(MethodSymbolsKey, SidecarsTypeKey, "LoadMethodSymbols"),
            Method(TypeSymbolsKey, SidecarsTypeKey, "LoadTypeSymbols"));

        var result = CreateSelector(repositories).Select(CreateRequest(LocalTimeKey, MethodSymbolsKey, TypeSymbolsKey) with
        {
            EntryPointProfile = WasmEntryPointProfile.None,
            UseJavaScriptExportBoundary = rawModule,
            ComponentContract = export is { } kind
                ? new("example:component@1", "component", [],
                    [new("example:component/api@1.0.0", "run", LocalTimeKey, [],
                        new(CanonicalAbiTypeKind.S32, CliTypeIdentity.FromStackKind(CliValueKind.I4))) { Kind = kind }])
                : ComponentBoundaryContract.Empty,
        });

        Assert.Equal(loadsSymbols ? [TypeSymbolsKey, MethodSymbolsKey, LocalTimeKey] : [LocalTimeKey], result.Methods);
        Assert.True(result.UsesLocalTime);
    }

    [Fact]
    public void SymbolsAloneDoNotUseLocalTime()
    {
        var repositories = new Repositories(Method(TypeSymbolsKey, SidecarsTypeKey, "LoadTypeSymbols"));

        var result = CreateSelector(repositories).Select(CreateRequest(TypeSymbolsKey));

        Assert.Equal([TypeSymbolsKey], result.Methods);
        Assert.False(result.UsesLocalTime);
    }

    [Fact]
    public void IgnoresSignatureMatchOnAnotherType()
    {
        var repositories = new Repositories(Method(LocalTimeKey, OtherTypeKey, "EnsureLocalTimeReady"));

        Assert.Equal(StartupPreflights.None.Methods, CreateSelector(repositories).Select(CreateRequest(LocalTimeKey)).Methods);
    }

    [Theory]
    [InlineData("Other", true, CliValueKind.Void, 0)]
    [InlineData("EnsureLocalTimeReady", false, CliValueKind.Void, 0)]
    [InlineData("EnsureLocalTimeReady", true, CliValueKind.I4, 0)]
    [InlineData("EnsureLocalTimeReady", true, CliValueKind.Void, 1)]
    public void IgnoresMethodsOutsideExactSignature(
        string name,
        bool isStatic,
        CliValueKind returnType,
        int parameterCount)
    {
        var parameters = Enumerable.Repeat(CliValueKind.I4, parameterCount).ToArray();
        var repositories = new Repositories(Method(LocalTimeKey, PlatformTypeKey, name) with
        {
            IsStatic = isStatic,
            Signature = MethodSignatureModel.Create(returnType, parameters),
        });

        var result = CreateSelector(repositories).Select(CreateRequest(LocalTimeKey));

        Assert.Empty(result.Methods);
        Assert.False(result.UsesLocalTime);
    }

    [Fact]
    public void ValidatesDependenciesAndRequest()
    {
        var repositories = new Repositories();

        Assert.Throws<ArgumentNullException>(() => new StartupPreflightMethodSelector(null!, repositories));
        Assert.Throws<ArgumentNullException>(() => new StartupPreflightMethodSelector(repositories, null!));
        Assert.Throws<ArgumentNullException>(() => CreateSelector(repositories).Select(null!));
    }

    private static MethodDefinitionModel Method(EntityKey key, EntityKey type, string name) => new(
        key, type, name, true, MethodSignatureModel.Create(CliValueKind.Void), 1);

    private static StartupPreflightMethodSelector CreateSelector(Repositories repositories) =>
        new StartupPreflightMethodSelector(repositories, repositories);

    private static WasmEmissionRequest CreateRequest(params EntityKey[] keys) =>
        EmitterTestSupport.CreateEmissionRequest() with
        {
            Methods = keys.ToImmutableDictionary(
                key => key, _ => (NetWasm.Compiler.ControlFlow.Structured.StructuredMethod)null!),
        };

    private sealed class Repositories(params MethodDefinitionModel[] methods) :
        ITypeRepository,
        IMethodRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => key switch
        {
            var value when value == PlatformTypeKey => new(
                PlatformTypeKey, "System.Runtime.InteropServices", "PlatformServices", false, [], []),
            var value when value == SidecarsTypeKey => new(
                SidecarsTypeKey, "System.Runtime.InteropServices", "ProgramSidecars", false, [], []),
            var value when value == OtherTypeKey => new(
                OtherTypeKey, "Example", "PlatformServices", false, [], []),
            _ => throw new KeyNotFoundException(),
        };

        public MethodDefinitionModel GetMethod(EntityKey key) => methods.Single(method => method.Key == key);
    }
}
