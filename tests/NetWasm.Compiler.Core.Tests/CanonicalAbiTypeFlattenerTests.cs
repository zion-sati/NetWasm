using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core.Tests;

public sealed class CanonicalAbiTypeFlattenerTests
{
    private readonly CanonicalAbiTypeFlattener _flattener = new();

    [Theory]
    [InlineData(CanonicalAbiTypeKind.U32, CliValueKind.I4)]
    [InlineData(CanonicalAbiTypeKind.U64, CliValueKind.I8)]
    [InlineData(CanonicalAbiTypeKind.F32, CliValueKind.F4)]
    [InlineData(CanonicalAbiTypeKind.F64, CliValueKind.F8)]
    public void FlattensScalarsThroughTheCapabilityContract(
        CanonicalAbiTypeKind kind,
        CliValueKind expected)
    {
        var flattened = Flatten(_flattener, Type(kind));

        Assert.Equal([expected], flattened.ToArray());
    }

    [Fact]
    public void RejectsNullTypesThroughTheCapabilityContract()
    {
        Assert.Throws<ArgumentNullException>(() => Flatten(_flattener, null!));
    }

    private static ImmutableArray<CliValueKind> Flatten<TFlattener>(
        TFlattener flattener,
        CanonicalAbiType type)
        where TFlattener : ICanonicalAbiTypeFlattener => flattener.Flatten(type);

    private static CanonicalAbiType Type(CanonicalAbiTypeKind kind) =>
        new(kind, CliTypeIdentity.FromStackKind(CliValueKind.Unknown));
}
