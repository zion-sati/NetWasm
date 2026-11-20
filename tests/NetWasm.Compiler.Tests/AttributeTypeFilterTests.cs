// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// A closed filter matches through the type relationship; an open generic attribute type matches an attribute type
// that is, or derives from, one of its constructed instances, as .NET's MatchesTypeFilter does.
public sealed class AttributeTypeFilterTests
{
    private static readonly AssemblyIdentity Assembly = new("Filters");
    private static readonly CliTypeIdentity Open = CliTypeIdentity.Named(Assembly, "Example", "TagAttribute`1", false);
    private static readonly CliTypeIdentity Other = CliTypeIdentity.Named(Assembly, "Example", "InfoAttribute`1", false);
    private static readonly CliTypeIdentity Int = CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly CliTypeIdentity Instance = CliTypeIdentity.GenericInstantiation(Open, [Int]);
    private static readonly CliTypeIdentity OtherInstance = CliTypeIdentity.GenericInstantiation(Other, [Int]);
    private static readonly CliTypeIdentity Derived = CliTypeIdentity.Named(Assembly, "Example", "IntTagAttribute", false);
    private static readonly CliTypeIdentity Closed = CliTypeIdentity.Named(Assembly, "Example", "MarkerAttribute", false);

    [Fact]
    public void AnOpenFilterMatchesItsInstancesAndTypesDerivedFromThem()
    {
        var facts = new Facts();
        var filter = new AttributeTypeFilter(facts, facts, facts);

        Assert.True(filter.Matches(Instance, Open));
        Assert.True(filter.Matches(Derived, Open));
        Assert.False(filter.Matches(OtherInstance, Open));
        Assert.False(filter.Matches(Closed, Open));
        Assert.Equal(0, facts.Classifications);
    }

    [Fact]
    public void AClosedFilterMatchesThroughTheTypeRelationship()
    {
        var facts = new Facts();
        var filter = new AttributeTypeFilter(facts, facts, facts);

        Assert.True(filter.Matches(Derived, Closed));
        Assert.False(filter.Matches(Instance, Closed));
        // A constructed filter is closed, so an instance of another type argument is decided by the relationship.
        Assert.False(filter.Matches(CliTypeIdentity.GenericInstantiation(Open, [Closed]), Instance));
        Assert.Equal(3, facts.Classifications);
    }

    [Fact]
    public void RejectsMissingDependenciesAndTypes()
    {
        var facts = new Facts();
        var filter = new AttributeTypeFilter(facts, facts, facts);
        Assert.Throws<ArgumentNullException>(() => filter.Matches(null!, Open));
        Assert.Throws<ArgumentNullException>(() => filter.Matches(Instance, null!));
        Assert.Throws<ArgumentNullException>(() => new AttributeTypeFilter(null!, facts, facts));
        Assert.Throws<ArgumentNullException>(() => new AttributeTypeFilter(facts, null!, facts));
        Assert.Throws<ArgumentNullException>(() => new AttributeTypeFilter(facts, facts, null!));
    }

    private sealed class Facts : ITypeRelationshipClassifier, IBaseTypeResolver, ITypeDefinitionResolver
    {
        internal int Classifications { get; private set; }

        public TypeRelationship Classify(CliTypeIdentity candidate, CliTypeIdentity target)
        {
            Classifications++;
            return new(candidate.Equals(Derived) && target.Equals(Closed)
                ? TypeRelationshipCharacteristics.Hierarchy
                : TypeRelationshipCharacteristics.None);
        }

        // IntTagAttribute derives from TagAttribute<int>.
        public CliTypeIdentity? Resolve(CliTypeIdentity type) => type.Equals(Derived) ? Instance : null;

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            new(new(Assembly, 1), "Example", "Type", false, [], [])
            {
                GenericArity = identity.Equals(Open) || identity.Equals(Other) ? 1 : 0,
            };
    }
}
