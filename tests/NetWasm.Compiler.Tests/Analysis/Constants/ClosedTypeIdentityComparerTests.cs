// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantRewriteTestSupport;

public sealed class ClosedTypeIdentityComparerTests
{
    [Fact]
    public void RejectsMissingDefinitionsAndOperands()
    {
        Assert.Throws<ArgumentNullException>(() => new ClosedTypeIdentityComparer(null!));
        Assert.Throws<ArgumentNullException>(() => Comparer().Compare(null!, Owner));
        Assert.Throws<ArgumentNullException>(() => Comparer().Compare(Owner, null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DeclinesUnprovenIdentitiesOnEitherSideAndInsideGenericArguments(int shape)
    {
        var unknown = shape switch
        {
            0 => CliTypeIdentity.GenericParameter(false, 0),
            1 => CliTypeIdentity.Modified(Owner, Owner, true),
            _ => CliTypeIdentity.FunctionPointer(new(0, 0, 0, MethodSignatureModel.Create(CliValueKind.Void))),
        };
        var comparer = Comparer();

        Assert.Null(comparer.Compare(unknown, Owner));
        Assert.Null(comparer.Compare(Owner, unknown));
        Assert.Null(comparer.Compare(CliTypeIdentity.GenericInstantiation(Owner, [unknown]),
            CliTypeIdentity.GenericInstantiation(Owner, [Owner])));
        Assert.Null(comparer.Compare(CliTypeIdentity.SzArray(unknown), CliTypeIdentity.SzArray(Owner)));
    }

    [Fact]
    public void DistinctGenericDefinitionsCannotBeMadeEqualByEqualArguments()
    {
        var second = CliTypeIdentity.Named(Assembly, "Fixture", "Second", false);
        Assert.False(Comparer().Compare(CliTypeIdentity.GenericInstantiation(Owner, [Owner]),
            CliTypeIdentity.GenericInstantiation(second, [Owner])));
        Assert.False(Comparer().Compare(Owner, CliTypeIdentity.SzArray(Owner)));
    }

    [Fact]
    public void UsesResolvedDefinitionsInsteadOfAssemblySpelling()
    {
        var first = CliTypeIdentity.Named(new("Forwarder"), "Fixture", "First", false);
        var second = CliTypeIdentity.Named(new("Implementation"), "Fixture", "First", false);
        var comparer = Comparer();

        Assert.True(comparer.Compare(first, second));
        Assert.False(comparer.Compare(first, CliTypeIdentity.Named(Assembly, "Fixture", "Second", false)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ComparesElementIdentities(int shape)
    {
        var first = CliTypeIdentity.Named(Assembly, "Fixture", "First", false);
        var second = CliTypeIdentity.Named(Assembly, "Fixture", "Second", false);
        var comparer = new ClosedTypeIdentityComparer(new Definitions());

        Assert.True(comparer.Compare(Wrap(first), Wrap(first)));
        Assert.False(comparer.Compare(Wrap(first), Wrap(second)));
        Assert.False(comparer.Compare(Wrap(first), first));

        CliTypeIdentity Wrap(CliTypeIdentity type) => shape switch
        {
            0 => CliTypeIdentity.SzArray(type),
            1 => CliTypeIdentity.Array(type, 2),
            2 => CliTypeIdentity.ManagedByReference(type),
            _ => CliTypeIdentity.UnmanagedPointer(type),
        };
    }

    [Fact]
    public void KeepsArrayRankAndSzShapeDistinct()
    {
        var element = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var comparer = new ClosedTypeIdentityComparer(new Definitions());

        Assert.False(comparer.Compare(CliTypeIdentity.Array(element, 1), CliTypeIdentity.Array(element, 2)));
        Assert.False(comparer.Compare(CliTypeIdentity.SzArray(element), CliTypeIdentity.Array(element, 1)));
    }

    [Fact]
    public void ComparesGenericArgumentsAndRejectsOpenArguments()
    {
        var comparer = new ClosedTypeIdentityComparer(new Definitions());
        var first = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var second = CliTypeIdentity.Named(Assembly, "Fixture", "Second", false);
        var closed = CliTypeIdentity.GenericInstantiation(Owner, [first]);

        Assert.True(comparer.Compare(closed, closed));
        Assert.False(comparer.Compare(closed, CliTypeIdentity.GenericInstantiation(Owner, [second])));
        Assert.False(comparer.Compare(closed, CliTypeIdentity.GenericInstantiation(Owner, [first, first])));
        Assert.Null(comparer.Compare(closed, CliTypeIdentity.GenericInstantiation(Owner, [CliTypeIdentity.GenericParameter(false, 0)])));
    }

    private sealed class Definitions : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity type) => new(
            new(Assembly, type.FullName == "Fixture.Second" ? 0x02000002 : 0x02000001), "Fixture", "Resolved", false, [], []);
    }

    internal static IClosedTypeIdentityComparer Comparer() => new ClosedTypeIdentityComparer(new Definitions());
}
