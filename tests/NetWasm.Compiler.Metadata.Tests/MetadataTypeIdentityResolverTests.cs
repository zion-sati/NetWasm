using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataTypeIdentityResolverTests
{
    [Fact]
    public void GetTypeIdentityPreservesDefinitionIdentityAndSelectsItsStackKind()
    {
        TypeDefinitionModel[] definitions =
        [
            Type(1, "Test", "Reference", false),
            Type(2, "Test", "Value", true),
            Type(3, "Test", "Choice", true) with
            {
                IsEnum = true,
                EnumUnderlyingType = CliTypeIdentity.FromStackKind(CliValueKind.I8),
            },
            Type(4, "System", "RuntimeTypeHandle", true),
            Type(5, "System", "StringComparison", true),
            Type(6, "System", "IntPtr", true),
            Type(7, "System", "UIntPtr", true),
        ];
        var repository = new MetadataTypeRepository(definitions.ToImmutableDictionary(
            type => type.Key), MetadataActorTestData.CreateAvailabilityValidator());
        var resolver = new MetadataTypeIdentityResolver(repository);
        Assert.Equal(CliValueKind.ManagedReference, Identity(resolver, definitions[0]).StackKind);
        Assert.Equal(CliValueKind.ValueType, Identity(resolver, definitions[1]).StackKind);
        Assert.Equal(CliValueKind.I8, Identity(resolver, definitions[2]).StackKind);
        Assert.Equal(CliValueKind.I4, Identity(resolver, definitions[3]).StackKind);
        Assert.Equal(CliValueKind.I4, Identity(resolver, definitions[4]).StackKind);
        Assert.Equal(CliValueKind.NativeInt, Identity(resolver, definitions[5]).StackKind);
        Assert.Equal(CliValueKind.NativeInt, Identity(resolver, definitions[6]).StackKind);
    }

    private static TypeDefinitionModel Type(
        int token,
        string @namespace,
        string name,
        bool isValueType) => new(
            new(new("Test.Assembly"), token),
            @namespace,
            name,
            isValueType,
            [],
            []);

    private static CliTypeIdentity Identity(
        MetadataTypeIdentityResolver resolver,
        TypeDefinitionModel type) =>
        ((ITypeIdentityResolver)resolver).GetTypeIdentity(type.Key);
}
