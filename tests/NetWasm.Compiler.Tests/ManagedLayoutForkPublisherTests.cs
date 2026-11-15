// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedLayoutForkPublisherTests
{
    [Fact]
    public void PublishesEquivalentAdditionsAndSynchronizesLaterBatch()
    {
        var original = Baseline();
        var first = new ManagedValueLayoutState(original);
        var second = new ManagedValueLayoutState(original);
        var newType = Type("new");
        first.Values.Add(newType, Layout(newType, 8));
        second.Values.Add(newType, Layout(newType, 8));
        first.CompletedMetadataLayouts.Add(newType);
        second.CompletedMetadataLayouts.Add(newType);
        var publisher = new ManagedLayoutForkPublisher(original, [first, second]);

        publisher.Publish();

        Assert.Equal(8, original.Values[newType].Size);
        Assert.Contains(newType, first.CompletedMetadataLayouts);
        Assert.Contains(newType, second.CompletedMetadataLayouts);
        var laterType = Type("later");
        second.Values.Add(laterType, Layout(laterType, 12));
        second.ConstructedFields.Add("later-field", new FieldLayout(4));
        publisher.Publish();
        Assert.Equal(12, original.Values[laterType].Size);
        Assert.Equal(12, first.Values[laterType].Size);
        Assert.Equal(new FieldLayout(4), first.ConstructedFields["later-field"]);
    }

    [Fact]
    public void RejectsConflictingOrIncompleteAdditionsWithoutPublication()
    {
        var newType = Type("new");
        var original = Baseline();
        var first = new ManagedValueLayoutState(original);
        var second = new ManagedValueLayoutState(original);
        first.Values.Add(newType, Layout(newType, 8));
        second.Values.Add(newType, Layout(newType, 12));
        var publisher = new ManagedLayoutForkPublisher(original, [first, second]);

        Assert.Throws<InvalidOperationException>(publisher.Publish);
        Assert.DoesNotContain(newType, original.Values.Keys);

        second.Values[newType] = Layout(newType, 8);
        first.CompletedMetadataLayouts.Add(Type("missing"));
        Assert.Throws<InvalidOperationException>(publisher.Publish);
        Assert.DoesNotContain(newType, original.Values.Keys);
    }

    [Fact]
    public void RejectsRemovedOrChangedBaselineEntries()
    {
        var original = Baseline();
        var baselineType = Type("base");
        var worker = new ManagedValueLayoutState(original);
        var publisher = new ManagedLayoutForkPublisher(original, [worker]);

        worker.Values.Remove(baselineType);
        Assert.Throws<InvalidOperationException>(publisher.Publish);
        Assert.Contains(baselineType, original.Values.Keys);

        worker.Values.Add(baselineType, Layout(baselineType, 12));
        Assert.Throws<InvalidOperationException>(publisher.Publish);

        worker.Values[baselineType] = Layout(baselineType, 4);
        worker.CompletedMetadataLayouts.Remove(baselineType);
        Assert.Throws<InvalidOperationException>(publisher.Publish);
        Assert.Contains(baselineType, original.CompletedMetadataLayouts);
    }

    [Fact]
    public void RejectsConcurrentMutationOfOriginalBeforePublishing()
    {
        var original = Baseline();
        var worker = new ManagedValueLayoutState(original);
        var publisher = new ManagedLayoutForkPublisher(original, [worker]);
        var extra = Type("other");
        original.Values.Add(extra, Layout(extra, 8));

        Assert.Throws<InvalidOperationException>(publisher.Publish);
        Assert.DoesNotContain(extra, worker.Values.Keys);
    }

    private static ManagedValueLayoutState Baseline()
    {
        var state = new ManagedValueLayoutState();
        var type = Type("base");
        state.Values.Add(type, Layout(type, 4));
        state.CompletedMetadataLayouts.Add(type);
        return state;
    }

    private static CliTypeIdentity Type(string name) =>
        CliTypeIdentity.Primitive(name, CliValueKind.I4);

    private static ValueLayout Layout(CliTypeIdentity type, int size) =>
        new(type, size, 4, ImmutableArray<int>.Empty);
}
