// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

// A virtual or interface call with many targets calls one generated function per declaration that carries the dispatch,
// instead of expanding a case per receiver type at every call site: a site of IObserver<object>.OnNext with hundreds of
// implementations grew each caller, and each generic instantiation of it, by kilobytes. The shared function's targets
// are every sharing site's, so a site whose receivers are bounded dispatches through a superset of them. A site with a
// target that is a runtime intrinsic expands it in place, as it must read the caller's frame.
internal static class SharedVirtualDispatch
{
    public const int MinimumTargets = SharedDispatchPolicy.MinimumTargets;

    public static bool Shares(DispatchCallSiteModel site, IRuntimeIntrinsicRegistry intrinsics) =>
        !site.SharesObjectDispatch && !site.Targets.IsDefault && site.Targets.Length >= MinimumTargets &&
        site.Targets.All(target => !intrinsics.TryGetIntrinsic(target.Method.Definition.Key, out _));

    // Each declaration that a sharing site calls, with the union of those sites' targets, one per receiver type, in
    // declaration order.
    public static ImmutableArray<SharedVirtualDispatchPlan> Plan(
        IEnumerable<DispatchCallSiteModel> sites, IRuntimeIntrinsicRegistry intrinsics) =>
        [.. sites
            .Where(site => Shares(site, intrinsics))
            .GroupBy(site => site.Declaration.CanonicalName, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new SharedVirtualDispatchPlan(
                group.First().Declaration,
                [.. group.SelectMany(site => site.Targets)
                    .DistinctBy(target => target.ReceiverType.CanonicalName, StringComparer.Ordinal)
                    .OrderBy(target => target.ReceiverType.CanonicalName, StringComparer.Ordinal)]))];

    public static string Symbol(string declaration) => "virtual-dispatch:" + declaration;
}

// A shared dispatch function: the declaration it implements, and every receiver type's target.
internal sealed record SharedVirtualDispatchPlan(MethodInstanceModel Declaration, ImmutableArray<DispatchTargetModel> Targets);
