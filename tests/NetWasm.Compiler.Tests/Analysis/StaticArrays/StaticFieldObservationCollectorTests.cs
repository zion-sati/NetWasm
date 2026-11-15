// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.StaticArrays;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.StaticArrays;

using static StaticArrayTestSupport;

public sealed class StaticFieldObservationCollectorTests
{
    [Fact]
    public void MethodTokensAreNotMistakenForFieldObservations()
    {
        var initial = Initializer();
        var body = initial.Body with
        {
            Instructions = [I(0, CilOperation.Call, new CilOperand.Entity(Constructor().Definition.Key)), I(1, CilOperation.Return)],
        };

        Assert.Empty(AsCollector(new StaticFieldObservationCollector()).Collect(Program(Managed(body)), ReachabilityRoots.Empty));
    }

    [Theory]
    [InlineData(CilOperation.LoadStaticField)]
    [InlineData(CilOperation.LoadStaticFieldAddress)]
    [InlineData(CilOperation.LoadFieldToken)]
    [InlineData(CilOperation.LoadField)]
    [InlineData(CilOperation.LoadFieldAddress)]
    [InlineData(CilOperation.StoreField)]
    public void ObservesReadAndAddressBoundariesButNotWritesAlone(CilOperation observation)
    {
        var initial = Initializer();
        var collector = AsCollector(new StaticFieldObservationCollector());
        Assert.DoesNotContain(Table.Key, collector.Collect(Program(initial), ReachabilityRoots.Empty));
        var body = initial.Body with { Instructions = initial.Body.Instructions.SetItem(8, I(8, observation, new CilOperand.Entity(Table.Key))) };
        Assert.Contains(Table.Key, collector.Collect(Program(Managed(body)), ReachabilityRoots.Empty));
        Assert.Contains(Table.Key, collector.Collect(Program(initial), new([], [Table.Key], [])));
    }

    [Fact]
    public void PreservesResolvedFieldsStaticDataViewsAndDescriptorRoots()
    {
        var field = new FieldInstanceModel(Table, Holder, Table.SignatureType);
        var initial = Initializer();
        var collector = AsCollector(new StaticFieldObservationCollector());
        var body = initial.Body with
        {
            Instructions = [I(0, CilOperation.LoadStaticField, new CilOperand.FieldInstance(field)), I(1, CilOperation.Return)],
        };
        Assert.Contains(Table.Key, collector.Collect(Program(Managed(body)), ReachabilityRoots.Empty));
        body = body with
        {
            Instructions = [I(0, CilOperation.CreateReadOnlySpanFromRva,
                new CilOperand.ReadOnlySpanData(field, CliTypeIdentity.GenericInstantiation(Holder, [Element]), Element, 1)),
                I(1, CilOperation.Return)],
        };
        Assert.Contains(Table.Key, collector.Collect(Program(Managed(body)), ReachabilityRoots.Empty));
        var program = Program(initial) with
        {
            FieldDescriptors = ImmutableDictionary<string, FieldInstanceModel>.Empty.Add(field.CanonicalName, field),
        };
        Assert.Contains(Table.Key, collector.Collect(program, ReachabilityRoots.Empty));
        Assert.Throws<ArgumentNullException>(() => collector.Collect(null!, ReachabilityRoots.Empty));
        Assert.Throws<ArgumentNullException>(() => collector.Collect(program, null!));
    }

    private static IStaticFieldObservationCollector AsCollector(IStaticFieldObservationCollector collector) => collector;
}
