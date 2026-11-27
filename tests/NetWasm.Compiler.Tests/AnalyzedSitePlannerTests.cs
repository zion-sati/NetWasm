// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

// Methods analyzed alone get their dispatch sites' targets as the closure gives them: each slot use (method and
// operation) resolved once per receiver, and each site's targets those its receiver bound admits.
public sealed class AnalyzedSitePlannerTests
{
    private static readonly AssemblyIdentity Assembly = new("Tests");
    private static readonly CliTypeIdentity Contract = Type("IContract");
    private static readonly CliTypeIdentity Bound = Type("IBound");
    private static readonly CliTypeIdentity Admitted = Type("Admitted");
    private static readonly CliTypeIdentity Other = Type("Other");
    private static readonly CliTypeIdentity Abstract = Type("Abstract");

    [Fact]
    public void SitesTakeTheirSlotUsesTargetsThatTheirBoundsAdmit()
    {
        var definition = new MethodDefinitionModel(
            new EntityKey(Assembly, 1), new EntityKey(Assembly, 2), "Run", false,
            MethodSignatureModel.Create(CliValueKind.Void), 0)
        {
            IsVirtual = true,
        };
        var method = new MethodInstanceModel(definition, Contract, [], definition.Signature);
        ReachabilityDispatch Site(string key, CilOperation operation, CliTypeIdentity? bound) =>
            new(key, new DispatchDeclaration("Tests.Caller::Run", key.Length, method, operation, bound));
        var resolver = new CountingResolver();
        var planner = new AnalyzedSitePlanner(
            new DispatchCandidateIndexFactory(new BoundRelationships(), UnboundedTypeRelationshipKeys.Instance),
            resolver,
            new DispatchSiteTargetSelector(new BoundRelationships(), UnboundedTypeRelationshipKeys.Instance),
            new EmptyTypeTestPlanner());

        var sites = planner.Plan(
            [
                Analysis(Site("a", CilOperation.CallVirtual, null), Site("bb", CilOperation.CallVirtual, Bound),
                    Site("ccc", CilOperation.LoadVirtualFunction, null)),
                Analysis(Site("dddd", CilOperation.CallVirtual, Bound)),
            ],
            [Other, Abstract, Admitted]).DispatchCallSites;

        Assert.Equal([Admitted, Other], sites["a"].Targets.Select(target => target.ReceiverType));
        Assert.Equal([Admitted], sites["bb"].Targets.Select(target => target.ReceiverType));
        Assert.Equal([Admitted, Other], sites["ccc"].Targets.Select(target => target.ReceiverType));
        Assert.Equal(sites["bb"].Targets, sites["dddd"].Targets);
        // Each slot use resolves each receiver once: three receivers for each of the two operations.
        Assert.Equal(6, resolver.Calls);
    }

    private static ReachableMethodAnalysis Analysis(params ReachabilityDispatch[] dispatches)
    {
        var body = ValidationTestData.ManagedMethodBody();
        return new(body.Method, body, [], [],
            new ReachabilityInstructionAnalysis([], [], [], [], [], [], [], [], [.. dispatches], [], []))
        {
            ReachableRegions = [],
        };
    }

    private static CliTypeIdentity Type(string name) =>
        CliTypeIdentity.Named(Assembly, "Tests", name, isValueType: false);

    // Every receiver implements the contract; only Admitted is assignable to the bound.
    private sealed class BoundRelationships : ITypeRelationshipClassifier
    {
        public TypeRelationship Classify(CliTypeIdentity candidate, CliTypeIdentity target) =>
            new(target == Contract || target == Bound && candidate == Admitted
                ? TypeRelationshipCharacteristics.Hierarchy
                : TypeRelationshipCharacteristics.None);
    }

    // An abstract receiver has no target.
    private sealed class CountingResolver : IDispatchTargetResolver
    {
        public int Calls { get; private set; }

        public DispatchTargetModel? Resolve(MethodInstanceModel declaration, CliTypeIdentity receiver)
        {
            Calls++;
            return receiver == Abstract ? null : new DispatchTargetModel(receiver, declaration);
        }
    }

    private sealed class EmptyTypeTestPlanner : ITypeTestPlanner
    {
        public ImmutableDictionary<string, TypeTestSiteModel> Build(
            IEnumerable<ManagedMethodBody> methods,
            IEnumerable<CliTypeIdentity> allocatedTypes) =>
            ImmutableDictionary<string, TypeTestSiteModel>.Empty;
    }
}
