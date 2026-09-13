namespace NetWasm.Compiler.Core.Tests;

public sealed class TypeSystemCoverageTests
{
    private static readonly AssemblyIdentity Assembly = new("TypeSystemTests");

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
