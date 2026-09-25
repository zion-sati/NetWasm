using NetWasm.Compiler.Core;
using Xunit;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class ValueTypeDefinitionStackKindResolverTests
{
    [Theory]
    [InlineData("System.Boolean", CliValueKind.I4)]
    [InlineData("System.Byte", CliValueKind.I4)]
    [InlineData("System.Char", CliValueKind.I4)]
    [InlineData("System.Int16", CliValueKind.I4)]
    [InlineData("System.Int32", CliValueKind.I4)]
    [InlineData("System.SByte", CliValueKind.I4)]
    [InlineData("System.UInt16", CliValueKind.I4)]
    [InlineData("System.UInt32", CliValueKind.I4)]
    [InlineData("System.Int64", CliValueKind.I8)]
    [InlineData("System.UInt64", CliValueKind.I8)]
    [InlineData("System.Single", CliValueKind.F4)]
    [InlineData("System.Double", CliValueKind.F8)]
    [InlineData("System.IntPtr", CliValueKind.NativeInt)]
    [InlineData("System.UIntPtr", CliValueKind.NativeInt)]
    [InlineData("System.Void", CliValueKind.Void)]
    [InlineData("System.Decimal", CliValueKind.ValueType)]
    [InlineData("Fixtures.CustomValue", CliValueKind.ValueType)]
    public void ResolverMapsValueTypeDefinitionsToTheirCliStackKind(
        string canonicalName,
        CliValueKind expected)
    {
        var resolver = Assert.IsAssignableFrom<IValueTypeDefinitionStackKindResolver>(
            new ValueTypeDefinitionStackKindResolver());

        var actual = resolver.Resolve(canonicalName);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ResolverRejectsMissingCanonicalNames(string? canonicalName)
    {
        var resolver = Assert.IsAssignableFrom<IValueTypeDefinitionStackKindResolver>(
            new ValueTypeDefinitionStackKindResolver());

        Assert.ThrowsAny<ArgumentException>(() => resolver.Resolve(canonicalName!));
    }
}
