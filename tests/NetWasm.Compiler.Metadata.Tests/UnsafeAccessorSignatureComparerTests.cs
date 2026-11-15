// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class UnsafeAccessorSignatureComparerTests
{
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly CliTypeIdentity Int64 = CliTypeIdentity.FromStackKind(CliValueKind.I8);

    [Fact]
    public void NamedTypesUseResolvedAssemblyIdentityAndPreserveValueKind()
    {
        var first = Named("First");
        var alias = Named("Alias");
        var other = Named("Other");
        var firstDefinition = Definition("First");
        var definitions = new Definitions(new()
        {
            [first] = firstDefinition,
            [alias] = firstDefinition,
            [other] = Definition("Other"),
        });
        var comparer = Assert.IsAssignableFrom<IUnsafeAccessorSignatureComparer>(new UnsafeAccessorSignatureComparer(definitions));
        Assert.True(comparer.Compare(first, alias, true));
        Assert.False(comparer.Compare(first, other, true));
        Assert.False(comparer.Compare(first, CliTypeIdentity.Named(new("First"), "Test", "Type", true), true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericParametersKeepTheirKindAndIndex(bool includeModifiers)
    {
        var comparer = Comparer();
        Assert.True(comparer.Compare(Int32, Int32, includeModifiers));
        Assert.False(comparer.Compare(Int32, Int64, includeModifiers));
        Assert.True(comparer.Compare(CliTypeIdentity.GenericParameter(false, 0), CliTypeIdentity.GenericParameter(false, 0), includeModifiers));
        Assert.False(comparer.Compare(CliTypeIdentity.GenericParameter(false, 0), CliTypeIdentity.GenericParameter(true, 0), includeModifiers));
        Assert.False(comparer.Compare(CliTypeIdentity.GenericParameter(true, 0), CliTypeIdentity.GenericParameter(true, 1), includeModifiers));
        Assert.False(comparer.Compare(CliTypeIdentity.GenericParameter(false, 0), Int32, includeModifiers));
    }

    [Fact]
    public void NestedShapesCompareElementsArgumentsAndArrayRank()
    {
        var comparer = Comparer();
        Func<CliTypeIdentity, CliTypeIdentity>[] wrappers =
        [
            CliTypeIdentity.SzArray,
            CliTypeIdentity.ManagedByReference,
            CliTypeIdentity.UnmanagedPointer,
            type => CliTypeIdentity.Array(type, 2),
            type => CliTypeIdentity.GenericInstantiation(Named("First"), [type]),
        ];
        foreach (var wrapper in wrappers)
        {
            Assert.True(comparer.Compare(wrapper(Int32), wrapper(Int32), true));
            Assert.False(comparer.Compare(wrapper(Int32), wrapper(Int64), true));
        }

        Assert.False(comparer.Compare(CliTypeIdentity.Array(Int32, 1), CliTypeIdentity.Array(Int32, 2), true));
        Assert.False(comparer.Compare(CliTypeIdentity.Array(Int32, 1), CliTypeIdentity.SzArray(Int32), true));
        Assert.False(comparer.Compare(CliTypeIdentity.GenericInstantiation(Named("First"), [Int32]),
            CliTypeIdentity.GenericInstantiation(Named("First"), [Int32, Int64]), true));
        Assert.False(comparer.Compare(CliTypeIdentity.GenericInstantiation(Named("First"), [Int32]),
            CliTypeIdentity.GenericInstantiation(Named("Other"), [Int32]), true));
    }

    [Fact]
    public void ModifierIgnoringAndExactPassesRemainDistinctAtEveryNestingLevel()
    {
        var comparer = Comparer();
        var required = CliTypeIdentity.Modified(Int32, Named("First"), true);
        var optional = CliTypeIdentity.Modified(Int32, Named("First"), false);
        var other = CliTypeIdentity.Modified(Int32, Named("Other"), true);
        Assert.True(comparer.Compare(required, optional, false));
        Assert.True(comparer.Compare(required, Int32, false));
        Assert.True(comparer.Compare(Int32, CliTypeIdentity.Modified(required, Named("Other"), false), false));
        Assert.True(comparer.Compare(required, required, true));
        Assert.False(comparer.Compare(required, optional, true));
        Assert.False(comparer.Compare(required, other, true));
        Assert.False(comparer.Compare(required, CliTypeIdentity.Modified(Int64, Named("First"), true), true));
        Assert.False(comparer.Compare(required, Int32, true));
        Assert.True(comparer.Compare(CliTypeIdentity.SzArray(required), CliTypeIdentity.SzArray(Int32), false));
        Assert.False(comparer.Compare(CliTypeIdentity.SzArray(required), CliTypeIdentity.SzArray(Int32), true));
    }

    [Fact]
    public void FunctionPointersPreserveCallingConventionAritySentinelAndSignature()
    {
        var comparer = Comparer();
        var signature = new CliFunctionPointerSignature(0, 0, 1, new(Int32, [Int64]));
        var original = CliTypeIdentity.FunctionPointer(signature);
        Assert.True(comparer.Compare(original, original, true));
        foreach (var different in new[]
        {
            signature with { Header = 1 },
            signature with { GenericArity = 1 },
            signature with { RequiredParameterCount = 0 },
            signature with { Signature = new(Int64, [Int64]) },
            signature with { Signature = new(Int32, [Int32]) },
            signature with { Signature = new(Int32, [Int64, Int64]) },
        })
        {
            Assert.False(comparer.Compare(original, CliTypeIdentity.FunctionPointer(different), true));
        }
    }

    [Fact]
    public void MissingDependenciesOrTypesFailImmediately()
    {
        Assert.Throws<ArgumentNullException>(() => new UnsafeAccessorSignatureComparer(null!));
        Assert.Throws<ArgumentNullException>(() => Comparer().Compare(null!, Int32, true));
        Assert.Throws<ArgumentNullException>(() => Comparer().Compare(Int32, null!, true));
    }

    private static IUnsafeAccessorSignatureComparer Comparer() => Assert.IsAssignableFrom<IUnsafeAccessorSignatureComparer>(new UnsafeAccessorSignatureComparer(
        new Definitions(new() { [Named("First")] = Definition("First"), [Named("Other")] = Definition("Other") })));

    private static CliTypeIdentity Named(string assembly) => CliTypeIdentity.Named(new(assembly), "Test", "Type", false);

    private static TypeDefinitionModel Definition(string assembly) => new(new(new(assembly), 0x02000001), "Test", "Type", false, [], []);

    private sealed class Definitions(Dictionary<CliTypeIdentity, TypeDefinitionModel> definitions) : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => definitions[identity];
    }
}
