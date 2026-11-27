// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class DispatchSiteTargetSelectorTests
{
    private static readonly AssemblyIdentity Assembly = new("Tests");

    // An unbounded site takes every target of its slot use, the same array; a bounded one those whose receivers are
    // assignable to its bound, classifying only the receivers whose relationship keys can reach the bound's.
    [Fact]
    public void ABoundSelectsTheAssignableReceiversAndClassifiesOnlyThoseItsKeysAdmit()
    {
        var bound = Type("IBound");
        var filed = Type("Filed");
        var unfiled = Type("Unfiled");
        var open = Type("Open");
        var rejected = Type("Rejected");
        var keys = new KeyTable(new()
        {
            [bound] = new("bound", new HashSet<string> { "bound" }, false),
            [filed] = new("filed", new HashSet<string> { "filed", "bound" }, false),
            [unfiled] = new("unfiled", new HashSet<string> { "unfiled" }, false),
            [open] = new("open", new HashSet<string>(), true),
            [rejected] = new("rejected", new HashSet<string> { "rejected", "bound" }, false),
        });
        var relationships = new RecordingRelationships(rejected);
        var selector = new DispatchSiteTargetSelector(relationships, keys);
        var method = MethodInstance();
        System.Collections.Immutable.ImmutableArray<DispatchTargetModel> targets =
            [new(filed, method), new(open, method), new(rejected, method), new(unfiled, method)];

        Assert.True(selector.Select(null, targets) == targets);
        Assert.Empty(relationships.Classified);
        Assert.Equal([filed, open], selector.Select(bound, targets).Select(target => target.ReceiverType));
        Assert.Equal([filed, open, rejected], relationships.Classified);
    }

    [Fact]
    public void AnUnboundedBoundClassifiesEveryReceiver()
    {
        var bound = Type("IOpenBound");
        var receiver = Type("Receiver");
        var keys = new KeyTable(new()
        {
            [bound] = new("open", new HashSet<string>(), true),
        });
        var relationships = new RecordingRelationships(rejected: null);

        var selected = new DispatchSiteTargetSelector(relationships, keys)
            .Select(bound, [new(receiver, MethodInstance())]);

        Assert.Equal([receiver], selected.Select(target => target.ReceiverType));
        Assert.Equal([receiver], relationships.Classified);
    }

    private static MethodInstanceModel MethodInstance()
    {
        var definition = new MethodDefinitionModel(
            new EntityKey(Assembly, 1), new EntityKey(Assembly, 2), "Run", false,
            MethodSignatureModel.Create(CliValueKind.Void), 0);
        return new(definition, Type("IContract"), [], definition.Signature);
    }

    private static CliTypeIdentity Type(string name) =>
        CliTypeIdentity.Named(Assembly, "Tests", name, isValueType: false);

    private sealed class KeyTable(Dictionary<CliTypeIdentity, TypeRelationshipKeys> keys) : ITypeRelationshipKeyResolver
    {
        public TypeRelationshipKeys Resolve(CliTypeIdentity type) => keys[type];
    }

    // Every receiver it classifies is assignable but the rejected one.
    private sealed class RecordingRelationships(CliTypeIdentity? rejected) : ITypeRelationshipClassifier
    {
        public List<CliTypeIdentity> Classified { get; } = [];

        public TypeRelationship Classify(CliTypeIdentity candidate, CliTypeIdentity target)
        {
            Classified.Add(candidate);
            return new(candidate == rejected
                ? TypeRelationshipCharacteristics.None
                : TypeRelationshipCharacteristics.Hierarchy);
        }
    }
}
