using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.LocalTime;

namespace NetWasm.Compiler.Wasm.Tests.Emission.GeneratedFunctions.LocalTime;

public sealed class LocalTimePreflightMethodSelectorTests
{
    private static readonly AssemblyIdentity Assembly = new("LocalTimeTests");
    private static readonly EntityKey PlatformTypeKey = new(Assembly, 0x02000001);
    private static readonly EntityKey OtherTypeKey = new(Assembly, 0x02000002);
    private static readonly EntityKey MethodKey = new(Assembly, 0x06000001);

    [Fact]
    public void SelectsExactReachablePlatformPreflightMethod()
    {
        var repositories = new Repositories(CreateMethod());
        var selector = CreateSelector(repositories);

        var result = selector.Select(CreateRequest(MethodKey));

        Assert.Equal(MethodKey, result);
    }

    [Fact]
    public void IgnoresSignatureMatchOnAnotherType()
    {
        var repositories = new Repositories(CreateMethod() with
        {
            DeclaringType = OtherTypeKey,
        });
        var selector = CreateSelector(repositories);

        Assert.Null(selector.Select(CreateRequest(MethodKey)));
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
        var repositories = new Repositories(CreateMethod() with
        {
            Name = name,
            IsStatic = isStatic,
            Signature = MethodSignatureModel.Create(returnType, parameters),
        });
        var selector = CreateSelector(repositories);

        Assert.Null(selector.Select(CreateRequest(MethodKey)));
    }

    [Fact]
    public void ValidatesDependenciesAndRequest()
    {
        var repositories = new Repositories(CreateMethod());

        Assert.Throws<ArgumentNullException>(() =>
            new LocalTimePreflightMethodSelector(null!, repositories));
        Assert.Throws<ArgumentNullException>(() =>
            new LocalTimePreflightMethodSelector(repositories, null!));
        var selector = CreateSelector(repositories);
        Assert.Throws<ArgumentNullException>(() => selector.Select(null!));
    }

    private static MethodDefinitionModel CreateMethod() => new(
        MethodKey,
        PlatformTypeKey,
        "EnsureLocalTimeReady",
        true,
        MethodSignatureModel.Create(CliValueKind.Void),
        1);

    private static ILocalTimePreflightMethodSelector CreateSelector(
        Repositories repositories) =>
        new[]
        {
            new LocalTimePreflightMethodSelector(repositories, repositories),
        }.Cast<ILocalTimePreflightMethodSelector>().Single();

    private static WasmEmissionRequest CreateRequest(EntityKey key) =>
        EmitterTestSupport.CreateEmissionRequest() with
        {
            Methods = ImmutableDictionary<EntityKey,
                NetWasm.Compiler.ControlFlow.Structured.StructuredMethod>.Empty.Add(key, null!),
        };

    private sealed class Repositories(MethodDefinitionModel method) :
        ITypeRepository,
        IMethodRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => key switch
        {
            var value when value == PlatformTypeKey => new(
                PlatformTypeKey,
                "System.Runtime.InteropServices",
                "PlatformServices",
                false,
                [],
                []),
            var value when value == OtherTypeKey => new(
                OtherTypeKey,
                "Example",
                "PlatformServices",
                false,
                [],
                []),
            _ => throw new KeyNotFoundException(),
        };

        public MethodDefinitionModel GetMethod(EntityKey key) => key == method.Key
            ? method
            : throw new KeyNotFoundException();
    }
}
