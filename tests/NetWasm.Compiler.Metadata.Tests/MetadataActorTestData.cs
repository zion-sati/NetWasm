using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

internal static class MetadataActorTestData
{
    internal static AssemblyIdentity Assembly { get; } = new("Test.Assembly");
    internal static EntityKey TypeKey { get; } = new(Assembly, 1);
    internal static EntityKey FieldKey { get; } = new(Assembly, 2);
    internal static EntityKey MethodKey { get; } = new(Assembly, 3);

    internal static TypeDefinitionModel Type { get; } = new(
        TypeKey,
        "Test.Namespace",
        "Sample",
        false,
        [FieldKey],
        [MethodKey]);

    internal static FieldDefinitionModel Field { get; } = new(
        FieldKey,
        TypeKey,
        "Value",
        CliValueKind.I4,
        false);

    internal static MethodDefinitionModel Method { get; } = new(
        MethodKey,
        TypeKey,
        "Run",
        true,
        MethodSignatureModel.Create(CliValueKind.Void),
        0);

    internal static ImmutableDictionary<EntityKey, T> One<T>(EntityKey key, T value)
        where T : notnull => ImmutableDictionary<EntityKey, T>.Empty.Add(key, value);

    internal static MetadataAvailabilityValidator CreateAvailabilityValidator() =>
        new(new MetadataLifetime());
}
