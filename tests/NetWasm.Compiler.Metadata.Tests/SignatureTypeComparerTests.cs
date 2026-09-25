using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class SignatureTypeComparerTests
{
    [Fact]
    public void CompareHandlesNormalizedNamedAndNestedSignatureShapes()
    {
        var comparer = new SignatureTypeComparer();
        var compare = ((ISignatureTypeComparer)comparer).Compare;
        var i4 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var i8 = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
        var int32 = CliTypeIdentity.Named(new("Core"), "System", "Int32", true);
        var leftNamed = CliTypeIdentity.Named(new("Left"), "Test", "Box`1", false);
        var rightNamed = CliTypeIdentity.Named(new("Right"), "Test", "Box`1", false);

        Assert.True(compare(i4, i4));
        Assert.True(compare(int32, i4));
        Assert.True(compare(leftNamed, rightNamed));
        Assert.True(compare(
            CliTypeIdentity.SzArray(i4),
            CliTypeIdentity.SzArray(int32)));
        Assert.True(compare(
            CliTypeIdentity.ManagedByReference(i4),
            CliTypeIdentity.ManagedByReference(int32)));
        Assert.True(compare(
            CliTypeIdentity.UnmanagedPointer(i4),
            CliTypeIdentity.UnmanagedPointer(int32)));
        Assert.True(compare(
            CliTypeIdentity.Array(i4, 2),
            CliTypeIdentity.Array(int32, 2)));
        Assert.True(compare(
            CliTypeIdentity.GenericInstantiation(leftNamed, [i4]),
            CliTypeIdentity.GenericInstantiation(rightNamed, [int32])));
        Assert.False(compare(i4, i8));
        Assert.False(compare(i4, CliTypeIdentity.SzArray(i4)));
        Assert.False(compare(
            CliTypeIdentity.Array(i4, 1),
            CliTypeIdentity.Array(i4, 2)));
        Assert.False(compare(
            CliTypeIdentity.Array(i4, 2),
            CliTypeIdentity.Array(i8, 2)));
        Assert.False(compare(
            CliTypeIdentity.GenericInstantiation(leftNamed, [i4]),
            CliTypeIdentity.GenericInstantiation(rightNamed, [i4, i8])));
        Assert.False(compare(
            CliTypeIdentity.GenericInstantiation(leftNamed, [i4]),
            CliTypeIdentity.GenericInstantiation(rightNamed, [i8])));
        Assert.True(compare(CliTypeIdentity.SzArray(leftNamed), CliTypeIdentity.SzArray(rightNamed)));
    }
}
