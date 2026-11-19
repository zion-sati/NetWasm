// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeMatchSelectorTests
{
    private static readonly AssemblyIdentity Assembly = new("Selection");
    private static readonly CliTypeIdentity Target = Type("Target");
    private static readonly CliTypeIdentity Parent = Type("Parent");
    private static readonly CliTypeIdentity Filter = Type("BaseAttribute");
    private static readonly CliTypeIdentity First = Type("FirstAttribute");
    private static readonly CliTypeIdentity Second = Type("SecondAttribute");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonmultipleDuplicatesSurviveWithinOneDeclarationLevel(bool inherited)
    {
        var fixture = new Fixture();
        var first = Attribute(1, First);
        var second = Attribute(2, First);
        fixture.Attributes[inherited ? Parent : Target] = [first, second];

        Assert.Collection(fixture.Select(inherit: true),
            item => Assert.Same(first, item), item => Assert.Same(second, item));
        Assert.Equal(inherited ? 2 : 0, fixture.UsageReads.Count);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    public void MultiplicityControlsSuppressionAcrossDeclarationLevels(bool multiple, int count)
    {
        var fixture = new Fixture();
        fixture.Attributes[Target] = [Attribute(1, First)];
        fixture.Attributes[Parent] = [Attribute(2, First), Attribute(3, First)];
        fixture.Usage[First] = new(true, multiple);

        Assert.Equal(Enumerable.Range(1, count), fixture.Select(inherit: true).Select(item => item.AttributeToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstantFalseInheritanceDoesNotReadTheBaseOrUsage(bool existence)
    {
        var fixture = new Fixture();
        fixture.Attributes[Parent] = [Attribute(1, First)];

        Assert.Empty(fixture.Select(inherit: false, existence));
        Assert.Equal([Target], fixture.DeclarationReads);
        Assert.Empty(fixture.UsageReads);
        Assert.Empty(fixture.BaseReads);
    }

    [Fact]
    public void DirectExistenceStopsBeforeFurtherMatchingUsageOrBaseAccess()
    {
        var fixture = new Fixture();
        var first = Attribute(1, First);
        fixture.Attributes[Target] = [first, Attribute(2, Second)];

        Assert.Same(first, Assert.Single(fixture.Select(inherit: true, existence: true)));
        Assert.Equal([(First, Filter)], fixture.RelationshipReads);
        Assert.Empty(fixture.UsageReads);
        Assert.Empty(fixture.BaseReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BaseFiltersUseEachConcreteAttributeUsage(bool existence)
    {
        var fixture = new Fixture();
        var inherited = Attribute(2, Second);
        fixture.Attributes[Parent] = [Attribute(1, First), inherited];
        fixture.Usage[First] = new(false, true);

        Assert.Same(inherited, Assert.Single(fixture.Select(inherit: true, existence)));
        Assert.Equal([First, Second], fixture.UsageReads);
    }

    [Fact]
    public void UnrelatedAttributesDoNotReadUsageOrBecomeResults()
    {
        var fixture = new Fixture();
        fixture.Attributes[Parent] = [Attribute(1, Type("Unrelated"))];

        Assert.Empty(fixture.Select(inherit: true));
        Assert.Empty(fixture.UsageReads);
    }

    private static CliTypeIdentity Type(string name) => CliTypeIdentity.Named(Assembly, "Example", name, false);
    private static CustomAttributeDescriptor Attribute(int token, CliTypeIdentity type) =>
        new(Assembly, token, token + 100, type, CliGenericContext.Empty);

    private sealed class Fixture : ICustomAttributeDescriptorReader, IAttributeUsageReader,
        ITypeRelationshipClassifier, IBaseTypeResolver, IAttributeTypeFilter
    {
        internal Dictionary<CliTypeIdentity, ImmutableArray<CustomAttributeDescriptor>> Attributes { get; } = [];
        internal Dictionary<CliTypeIdentity, AttributeUsage> Usage { get; } = [];
        internal List<CliTypeIdentity> DeclarationReads { get; } = [];
        internal List<CliTypeIdentity> UsageReads { get; } = [];
        internal List<CliTypeIdentity> BaseReads { get; } = [];
        internal List<(CliTypeIdentity Candidate, CliTypeIdentity Target)> RelationshipReads { get; } = [];

        internal ImmutableArray<CustomAttributeDescriptor> Select(bool inherit, bool existence = false) =>
            ((IAttributeMatchSelector)new AttributeMatchSelector(this, this, this, this))
                .Select(Target, Filter, inherit, existence);

        ImmutableArray<CustomAttributeDescriptor> ICustomAttributeDescriptorReader.Read(CliTypeIdentity type)
        {
            DeclarationReads.Add(type);
            return Attributes.GetValueOrDefault(type, []);
        }

        AttributeUsage IAttributeUsageReader.Read(CliTypeIdentity type)
        {
            UsageReads.Add(type);
            return Usage.GetValueOrDefault(type, new(true, false));
        }

        public bool Matches(CliTypeIdentity attributeType, CliTypeIdentity filter) =>
            Classify(attributeType, filter).IsAssignmentCompatible;

        public TypeRelationship Classify(CliTypeIdentity candidate, CliTypeIdentity target)
        {
            RelationshipReads.Add((candidate, target));
            return new(candidate.Equals(First) || candidate.Equals(Second)
                ? TypeRelationshipCharacteristics.Hierarchy : TypeRelationshipCharacteristics.None);
        }

        public CliTypeIdentity? Resolve(CliTypeIdentity type)
        {
            BaseReads.Add(type);
            return type.Equals(Target) ? Parent : null;
        }
    }
}
