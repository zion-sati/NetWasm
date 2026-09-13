using NetWasm.Compiler.ComponentModel.Functions;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Functions;

public sealed class WitCanonicalFunctionBuilderTests
{
    [Fact]
    public void ResolvesParametersInOrderAndPreservesTheExistingFunctionIdentity()
    {
        var document = new WitDocument([], [], [], [], "{}");
        var first = new WitTypeReference.Primitive("u8");
        var second = new WitTypeReference.Primitive("string");
        var result = new WitTypeReference.Primitive("u64");
        var declaration = new WitFunction("[method]file.read", [new("self", first), new("text", second)], result, new("method", 0));
        var resolver = new RecordingResolver();

        var canonical = Create(resolver).Build(document, "example:files@1.0.0/api", declaration);

        Assert.Equal("example:files@1.0.0/api", canonical.InterfaceName);
        Assert.Equal(declaration.Name, canonical.FunctionName);
        Assert.Equal(new EntityKey(new AssemblyIdentity("generated"), 0), canonical.ManagedMethod);
        Assert.Equal(CanonicalAbiFunctionKind.Function, canonical.Kind);
        Assert.Equal(["self", "text"], canonical.Parameters.Select(parameter => parameter.Name));
        Assert.Same(resolver.Values[0], canonical.Parameters[0].Type);
        Assert.Same(resolver.Values[1], canonical.Parameters[1].Type);
        Assert.Same(resolver.Values[2], canonical.Result);
        Assert.Equal([first, second, result], resolver.Requests.Select(request => request.Reference));
        Assert.All(resolver.Requests, request => Assert.Same(document, request.Document));
    }

    [Fact]
    public void AbsentResultAndEmptyRootFunctionDoNotResolveInventedTypes()
    {
        var resolver = new RecordingResolver();

        var canonical = Create(resolver).Build(new([], [], [], [], "{}"), "", new("run", [], null, new("freestanding")));

        Assert.Equal("", canonical.InterfaceName);
        Assert.Equal("run", canonical.FunctionName);
        Assert.Empty(canonical.Parameters);
        Assert.Null(canonical.Result);
        Assert.Empty(resolver.Requests);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ResolutionFailureStopsBeforeLaterTypes(int failingCall)
    {
        var resolver = new RecordingResolver { FailingCall = failingCall };
        var type = new WitTypeReference.Primitive("u8");
        var declaration = new WitFunction("run", [new("a", type), new("b", type)], type, new("freestanding"));

        Assert.Same(resolver.Failure, Assert.Throws<InvalidOperationException>(() =>
            Create(resolver).Build(new([], [], [], [], "{}"), "", declaration)));
        Assert.Equal(failingCall, resolver.Requests.Count);
    }

    [Fact]
    public void MissingInputsFailBeforeTypeResolution()
    {
        var resolver = new RecordingResolver();
        var builder = Create(resolver);
        var document = new WitDocument([], [], [], [], "{}");
        var declaration = new WitFunction("run", [], null, new("freestanding"));

        Assert.Throws<ArgumentNullException>(() => builder.Build(null!, "", declaration));
        Assert.Throws<ArgumentNullException>(() => builder.Build(document, null!, declaration));
        Assert.Throws<ArgumentNullException>(() => builder.Build(document, "", null!));
        Assert.Throws<ArgumentNullException>(() => new WitCanonicalFunctionBuilder(null!));
        Assert.Empty(resolver.Requests);
    }

    private static IWitCanonicalFunctionBuilder Create(IWitCanonicalTypeResolver resolver) =>
        Assert.IsAssignableFrom<IWitCanonicalFunctionBuilder>(new WitCanonicalFunctionBuilder(resolver));

    private sealed class RecordingResolver : IWitCanonicalTypeResolver
    {
        public List<(WitDocument Document, WitTypeReference Reference)> Requests { get; } = [];
        public List<CanonicalAbiType> Values { get; } = [];
        public int FailingCall { get; init; }
        public InvalidOperationException Failure { get; } = new();

        public CanonicalAbiType Resolve(WitDocument document, WitTypeReference reference)
        {
            Requests.Add((document, reference));
            if (Requests.Count == FailingCall) throw Failure;
            var value = new CanonicalAbiType(CanonicalAbiTypeKind.U8, CliTypeIdentity.FromStackKind(CliValueKind.Unknown));
            Values.Add(value);
            return value;
        }
    }
}
