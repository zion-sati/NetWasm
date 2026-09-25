using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class ArrayMethodResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("Rectangular.Tests");
    private static readonly CliTypeIdentity Int32 =
        CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Void =
        CliTypeIdentity.Primitive("void", CliValueKind.Void);

    [Fact]
    public void ConstructorRejectsMissingSignatureComparer()
    {
        Assert.Throws<ArgumentNullException>(() => new ArrayMethodResolver(null!));
    }

    [Fact]
    public void ResolveIgnoresOrdinaryTypesThroughItsInterface()
    {
        var resolver = CreateResolver();
        var request = Request(
            CliTypeIdentity.Named(Assembly, "Tests", "Value", false),
            ".ctor",
            new MethodSignatureModel(Void, []));

        Assert.Null(((IRuntimeProvidedMethodResolver)resolver).Resolve(request));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ResolveAcceptsExplicitBoundsForEveryDimension(int rank)
    {
        var resolver = CreateResolver();
        var array = CliTypeIdentity.Array(Int32, rank);
        var signature = new MethodSignatureModel(Void,
            [.. Enumerable.Repeat(Int32, rank * 2)]);

        var method = ((IRuntimeProvidedMethodResolver)resolver).Resolve(
            Request(array, ".ctor", signature));

        Assert.NotNull(method);
        Assert.Same(array, method.DeclaringType);
        Assert.Same(signature, method.Signature);
    }

    [Theory]
    [InlineData(".ctor")]
    [InlineData("Get")]
    [InlineData("Set")]
    [InlineData("Address")]
    public void ResolveRecognizesEveryRuntimeProvidedArrayMember(string name)
    {
        var resolver = CreateResolver();
        var array = CliTypeIdentity.Array(Int32, 2);
        var signature = Signature(name);

        var result = Assert.IsType<MethodInstanceModel>(
            ((IRuntimeProvidedMethodResolver)resolver).Resolve(
            Request(array, name, signature)));

        Assert.Same(array, result.DeclaringType);
        Assert.Equal(name, result.Definition.Name);
        Assert.False(result.Definition.IsStatic);
        Assert.False(result.Definition.HasBody);
        Assert.Equal(0x0a000001, result.Definition.Key.MetadataToken);
        Assert.Equal(0x1b000001, result.Definition.DeclaringType.MetadataToken);
        Assert.Same(signature, result.Signature);
    }

    [Theory]
    [InlineData("Unknown", true)]
    [InlineData("Get", false)]
    public void ResolveRejectsUnknownOrStaticArrayMembers(string name, bool isInstance)
    {
        var resolver = CreateResolver();
        var request = Request(
            CliTypeIdentity.Array(Int32, 2),
            name,
            Signature("Get"),
            isInstance);

        var exception = Assert.Throws<CompilerException>(() =>
            ((IRuntimeProvidedMethodResolver)resolver).Resolve(request));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Equal("Fixture.Run", exception.Diagnostic.Method);
        Assert.Equal(17, exception.Diagnostic.IlOffset);
    }

    [Theory]
    [MemberData(nameof(InvalidSignatures))]
    public void ResolveRejectsMalformedArrayMemberSignatures(
        CliTypeIdentity array,
        string name,
        MethodSignatureModel signature)
    {
        var resolver = CreateResolver();

        Assert.Throws<CompilerException>(() =>
            ((IRuntimeProvidedMethodResolver)resolver).Resolve(
            Request(array, name, signature)));
    }

    public static TheoryData<CliTypeIdentity, string, MethodSignatureModel>
        InvalidSignatures() => new()
        {
            { CliTypeIdentity.Array(Int32, 2), ".ctor", new(Void, [Int32]) },
            { CliTypeIdentity.Array(Int32, 2), ".ctor", new(Void, [Int32, Int32, Int32]) },
            { CliTypeIdentity.Array(Int32, 2), ".ctor", new(Void, [Int32, Int32, Int32, Void]) },
            { CliTypeIdentity.Array(Int32, 1), "Get", new(Int32, [Int32, Int32]) },
            { CliTypeIdentity.Array(Int32, 2), ".ctor", new(Int32, [Int32, Int32]) },
            { CliTypeIdentity.Array(Int32, 2), "Get", new(Int32, [Void, Int32]) },
            { CliTypeIdentity.Array(Int32, 2), "Get", new(Void, [Int32, Int32]) },
            { CliTypeIdentity.Array(Int32, 2), "Set", new(Void, [Int32, Int32]) },
            { CliTypeIdentity.Array(Int32, 2), "Set", new(Void, [Int32, Int32, Void]) },
            { CliTypeIdentity.Array(Int32, 2), "Address", new(Int32, [Int32, Int32]) },
            { CliTypeIdentity.Array(Int32, 2), "Address", new(
                CliTypeIdentity.ManagedByReference(Void), [Int32, Int32]) },
        };

    private static ArrayMethodResolver CreateResolver() => new(
        new SignatureTypeComparer());

    private static RuntimeProvidedMethodRequest Request(
        CliTypeIdentity declaringType,
        string name,
        MethodSignatureModel signature,
        bool isInstance = true) => new(
        Assembly,
        0x0a000001,
        0x1b000001,
        declaringType,
        name,
        isInstance,
        signature,
        "Fixture.Run",
        17);

    private static MethodSignatureModel Signature(string name) => name switch
    {
        ".ctor" => new(Void, [Int32, Int32]),
        "Get" => new(Int32, [Int32, Int32]),
        "Set" => new(Void, [Int32, Int32, Int32]),
        "Address" => new(CliTypeIdentity.ManagedByReference(Int32), [Int32, Int32]),
        _ => new(Int32, [Int32, Int32]),
    };
}
