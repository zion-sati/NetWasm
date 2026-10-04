namespace NetWasm.Compiler.Core.Tests;

public sealed class TypeSystemCoverageTests
{
    private static readonly AssemblyIdentity Assembly = new("TypeSystemTests");

    [Theory]
    [InlineData("u2", CliValueKind.I4)]
    [InlineData("u8", CliValueKind.I8)]
    public void GenericEnumInstantiationPreservesUnderlyingStorage(string primitive, CliValueKind kind)
    {
        var storage = CliTypeIdentity.Primitive(primitive, kind);
        var definition = CliTypeIdentity.Named(Assembly, "Example", "Outer`1+Code", true)
            .WithStackStorageType(storage);
        var argument = CliTypeIdentity.FromStackKind(CliValueKind.I4);

        var closed = CliTypeIdentity.GenericInstantiation(definition, [argument]);

        Assert.Same(storage, closed.StackStorageType);
        Assert.Equal(kind, closed.StackKind);
        Assert.True(closed.IsValueType);
        Assert.Same(argument, Assert.Single(closed.TypeArguments));
        Assert.Same(storage, closed.Substitute([]).StackStorageType);
    }

    [Fact]
    public void SubstitutionPreservesManagedReferencesAndUnmanagedPointers()
    {
        var parameter = CliTypeIdentity.GenericParameter(method: false, index: 0);
        var argument = CliTypeIdentity.Named(
            Assembly,
            "Example",
            "Node",
            isValueType: false);

        var managedReference = CliTypeIdentity.ManagedByReference(parameter)
            .Substitute([argument]);
        var unmanagedPointer = CliTypeIdentity.UnmanagedPointer(parameter)
            .Substitute([argument]);

        Assert.Equal($"{argument.CanonicalName}&", managedReference.CanonicalName);
        Assert.Equal(CliTypeShape.ManagedByReference, managedReference.Shape);
        Assert.Same(argument, managedReference.ElementType);
        Assert.Equal($"{argument.CanonicalName}*", unmanagedPointer.CanonicalName);
        Assert.Equal(CliTypeShape.UnmanagedPointer, unmanagedPointer.Shape);
        Assert.Same(argument, unmanagedPointer.ElementType);
    }
}
