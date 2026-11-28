// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.ReceiverFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class ReceiverFlowGraphTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Animal = Type("Animal");
    private static readonly CliTypeIdentity Cat = Type("Cat");
    private static readonly CliTypeIdentity Dog = Type("Dog");
    private static readonly CliTypeIdentity Stone = Type("Stone");

    [Fact]
    public void AValueTravelsAlongEveryEdgeOnce()
    {
        var graph = Graph();
        var (a, b, c) = (graph.Id(Key("a")), graph.Id(Key("b")), graph.Id(Key("c")));
        graph.AddEdge(a, b, []);
        graph.AddEdge(b, c, []);
        graph.AddEdge(c, a, []);
        graph.AddObject(a, new FlowObject.Exact(Cat));

        graph.Propagate();

        Assert.Equal(["Cat"], Names(graph.Value(Key("c"))));
        Assert.True(graph.Value(Key("a")).IsPrecise);
        Assert.False(graph.HasPending);
    }

    [Fact]
    public void ACastDropsOnlyTypesThatCanNeverPassAndBoundsTopValues()
    {
        var graph = Graph();
        var (source, target) = (graph.Id(Key("source")), graph.Id(Key("target")));
        graph.AddEdge(source, target, [Animal]);
        graph.AddObject(source, new FlowObject.Exact(Cat));
        graph.AddObject(source, new FlowObject.Exact(Stone));
        graph.AddTop(source, new FlowBound([], "unknown"));

        graph.Propagate();

        var value = graph.Value(Key("target"));
        Assert.Equal(["Cat"], Names(value));
        var bound = Assert.Single(value.Tops);
        Assert.Equal([Animal], bound.Types);
        Assert.Equal("unknown", bound.Cause);
    }

    [Fact]
    public void AnEdgeAddedLaterCarriesWhatItsSourceAlreadyHolds()
    {
        var graph = Graph();
        var (source, target) = (graph.Id(Key("source")), graph.Id(Key("target")));
        graph.AddObject(source, new FlowObject.Exact(Dog));
        graph.Propagate();

        graph.AddEdge(source, target, []);
        graph.Propagate();

        Assert.Equal(["Dog"], Names(graph.Value(Key("target"))));
    }

    [Fact]
    public void AWatcherSeesWhatTheNodeHoldsAndWhatItGains()
    {
        var graph = Graph();
        var node = graph.Id(Key("node"));
        graph.AddObject(node, new FlowObject.Exact(Cat));
        graph.Propagate();
        var seen = new List<string>();

        graph.Watch(node, (item, bound) => seen.Add(item?.Type.FullName ?? bound!.Cause));
        graph.AddObject(node, new FlowObject.Exact(Dog));
        graph.AddTop(node, new FlowBound([Animal], "boundary"));
        graph.Propagate();

        Assert.Equal(["Cat", "Dog", "boundary"], seen);
    }

    [Fact]
    public void MoreTypesThanTheCapMakeTheNodeTopBoundedByItsDeclaredType()
    {
        var graph = Graph();
        var node = graph.Id(Key("node"), Animal);
        for (var index = 0; index <= ReceiverFlowGraph.MaximumObjects; index++)
            graph.AddObject(node, new FlowObject.Exact(Type($"Cat{index}")));

        graph.Propagate();

        var bound = Assert.Single(graph.Value(Key("node")).Tops);
        Assert.Equal([Animal], bound.Types);
    }

    [Fact]
    public void MoreBoundsThanTheCapMakeTheNodeTopWithoutABound()
    {
        var graph = Graph();
        var node = graph.Id(Key("node"));
        for (var index = 0; index <= ReceiverFlowGraph.MaximumBounds; index++)
            graph.AddTop(node, new FlowBound([Type($"Bound{index}")], "boundary"));

        Assert.Contains(graph.Value(Key("node")).Tops, static bound => bound.Types.IsEmpty);
    }

    [Fact]
    public void ATypeIsWithinABoundWhenItPassesEveryTypeOfIt()
    {
        var graph = Graph();

        Assert.True(graph.Within(Cat, new FlowBound([Animal], "boundary")));
        Assert.False(graph.Within(Stone, new FlowBound([Animal], "boundary")));
        Assert.True(graph.Within(Stone, new FlowBound([], "unknown")));
    }

    private static ReceiverFlowGraph Graph() => new(new Hierarchy());

    private static FlowNodeKey Key(string name) => new(FlowNodeKind.Local, name);

    private static CliTypeIdentity Type(string name) => CliTypeIdentity.Named(Assembly, "", name, isValueType: false);

    private static string[] Names(FlowValue value) => [.. value.Objects.Select(static item => item.Type.FullName!).Order()];

    // Cats and dogs are animals; nothing else is related.
    private sealed class Hierarchy : ITypeRelationshipClassifier
    {
        public TypeRelationship Classify(CliTypeIdentity candidate, CliTypeIdentity target) =>
            new(candidate.Equals(target) || target.Equals(Animal) && candidate.FullName!.StartsWith("Cat", System.StringComparison.Ordinal) ||
                target.Equals(Animal) && candidate.Equals(Dog)
                ? TypeRelationshipCharacteristics.Hierarchy
                : TypeRelationshipCharacteristics.None);
    }
}
