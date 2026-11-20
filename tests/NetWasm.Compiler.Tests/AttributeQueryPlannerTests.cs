// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeQueryPlannerTests
{
    private static readonly CliTypeIdentity Target = CliTypeIdentity.Named(new("Test"), "Test", "Target", false);
    private static readonly CliTypeIdentity Filter = CliTypeIdentity.Named(new("Test"), "Test", "MarkerAttribute", false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(null)]
    public void ExistencePlansNeverRequestMaterialization(bool? inherit)
    {
        var fixture = new Fixture();
        var result = ((IAttributeQueryPlanner)new AttributeQueryPlanner(fixture, fixture))
            .Plan(Query(AttributeQueryOperation.IsDefined, inherit));

        Assert.Equal(Target, result.TargetType);
        Assert.Equal(Filter, result.FilterType);
        Assert.False(Assert.IsType<AttributeExistenceResult>(result.Direct).IsDefined);
        Assert.Equal(inherit != false, Assert.IsType<AttributeExistenceResult>(result.Inherited).IsDefined);
        Assert.Equal(inherit is null ? [false, true] : [inherit.Value], fixture.InheritanceRequests);
        Assert.All(fixture.ExistenceRequests, Assert.True);
        Assert.Empty(fixture.ConstructionRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(null)]
    public void RetrievalPlansMaterializeOnlySelectedBranches(bool? inherit)
    {
        var fixture = new Fixture();
        var result = ((IAttributeQueryPlanner)new AttributeQueryPlanner(fixture, fixture))
            .Plan(Query(AttributeQueryOperation.GetMany, inherit));

        Assert.Empty(Assert.IsType<AttributeRetrievalResult>(result.Direct).Attributes);
        Assert.Equal(inherit == false ? 0 : 1, Assert.IsType<AttributeRetrievalResult>(result.Inherited).Attributes.Length);
        Assert.Equal(inherit is null ? [false, true] : [inherit.Value], fixture.InheritanceRequests);
        Assert.All(fixture.ExistenceRequests, value => Assert.False(value));
        Assert.Equal(inherit == false ? 0 : 1, fixture.ConstructionRequests.Count);
    }

    [Fact]
    public void NullQueryFailsBeforeSelection()
    {
        var fixture = new Fixture();
        Assert.Throws<ArgumentNullException>(() =>
            ((IAttributeQueryPlanner)new AttributeQueryPlanner(fixture, fixture)).Plan(null!));
        Assert.Empty(fixture.InheritanceRequests);
    }

    // An assembly has no base: both results are its own attributes of the filter type, read by name.
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void AssemblyQueriesReadTheAssemblysOwnAttributes(bool existence, int constructions)
    {
        var operation = existence ? AttributeQueryOperation.IsDefined : AttributeQueryOperation.GetMany;
        var fixture = new Fixture();
        var attributes = new AssemblyAttributes();
        var query = new ResolvedAttributeQuery(new(operation, 2, null, Filter, 1), null, Filter, null) { TargetAssembly = new("Test") };

        var result = ((IAttributeQueryPlanner)new AttributeQueryPlanner(fixture, fixture, attributes)).Plan(query);

        Assert.Null(result.TargetType);
        Assert.Equal(new AssemblyIdentity("Test"), result.TargetAssembly);
        if (existence)
        {
            Assert.True(Assert.IsType<AttributeExistenceResult>(result.Direct).IsDefined);
            Assert.True(Assert.IsType<AttributeExistenceResult>(result.Inherited).IsDefined);
        }
        else
        {
            Assert.Single(Assert.IsType<AttributeRetrievalResult>(result.Direct).Attributes);
            Assert.Single(Assert.IsType<AttributeRetrievalResult>(result.Inherited).Attributes);
        }
        Assert.Equal([(new AssemblyIdentity("Test"), "Test.MarkerAttribute"), (new AssemblyIdentity("Test"), "Test.MarkerAttribute")], attributes.Requests);
        Assert.Empty(fixture.InheritanceRequests);
        Assert.Equal(constructions * 2, fixture.ConstructionRequests.Count);
        var withoutReader = new AttributeQueryPlanner(fixture, fixture);
        Assert.Throws<InvalidOperationException>(() => ((IAttributeQueryPlanner)withoutReader).Plan(query));
    }

    // The reader returns a generic attribute whatever its type (its constructor belongs to a type specification), so the
    // planner keeps only the attributes the filter matches.
    [Fact]
    public void AssemblyAttributesAreCheckedAgainstTheFilter()
    {
        var fixture = new Fixture();
        var other = CliTypeIdentity.Named(new("Test"), "Test", "OtherAttribute", false);
        var attributes = new AssemblyAttributes(other);
        var query = new ResolvedAttributeQuery(new(AttributeQueryOperation.GetMany, 2, null, Filter, 1), null, Filter, null)
        {
            TargetAssembly = new("Test"),
        };

        var result = ((IAttributeQueryPlanner)new AttributeQueryPlanner(fixture, fixture, attributes, new Types())).Plan(query);

        Assert.Single(Assert.IsType<AttributeRetrievalResult>(result.Direct).Attributes);
        Assert.Equal([Filter, Filter], fixture.ConstructionRequests.Select(request => request.AttributeType));
    }

    private sealed class Types : IAttributeTypeFilter
    {
        public bool Matches(CliTypeIdentity attributeType, CliTypeIdentity filter) => attributeType.Equals(filter);
    }

    private sealed class AssemblyAttributes(CliTypeIdentity? alsoFound = null) : IAssemblyAttributeDescriptorReader
    {
        internal List<(AssemblyIdentity Assembly, string Name)> Requests { get; } = [];

        public ImmutableArray<CustomAttributeDescriptor> Read(AssemblyIdentity assembly, string attributeFullName)
        {
            Requests.Add((assembly, attributeFullName));
            return alsoFound is null
                ? [new(assembly, 1, 2, Filter, CliGenericContext.Empty)]
                : [new(assembly, 1, 2, Filter, CliGenericContext.Empty), new(assembly, 3, 4, alsoFound, CliGenericContext.Empty)];
        }
    }

    private static ResolvedAttributeQuery Query(AttributeQueryOperation operation, bool? inherit) =>
        new(new(operation, 2, null, Filter, 1), Target, Filter, inherit);

    private sealed class Fixture : IAttributeMatchSelector, IAttributeConstructionPlanner
    {
        internal List<bool> InheritanceRequests { get; } = [];
        internal List<bool> ExistenceRequests { get; } = [];
        internal List<CustomAttributeDescriptor> ConstructionRequests { get; } = [];

        public ImmutableArray<CustomAttributeDescriptor> Select(CliTypeIdentity target,
            CliTypeIdentity filter, bool inherit, bool existenceOnly)
        {
            Assert.Equal(Target, target);
            Assert.Equal(Filter, filter);
            InheritanceRequests.Add(inherit);
            ExistenceRequests.Add(existenceOnly);
            return inherit ? [new(new("Test"), 1, 2, Filter, CliGenericContext.Empty)] : [];
        }

        public AttributeConstructionPlan Plan(CustomAttributeDescriptor attribute)
        {
            ConstructionRequests.Add(attribute);
            var signature = MethodSignatureModel.Create(CliValueKind.Void);
            return new(Filter, new(new(new(new("Test"), 2), new(new("Test"), 3),
                ".ctor", false, signature, 1), Filter, [], signature), [], []);
        }
    }
}
