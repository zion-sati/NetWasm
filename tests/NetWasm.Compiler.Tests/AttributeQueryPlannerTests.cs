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
