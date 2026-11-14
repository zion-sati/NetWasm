using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Analysis.ManagedCallSites;
using NetWasm.Compiler.Analysis.Delegates;

namespace NetWasm.Compiler.Analysis.Reachability;

// The runtime's roots first, then the members, then the type facts the others may have demanded.
internal sealed class ReachabilityDerivedRootExpander(
    IRuntimeReachabilityExpander runtimeRoots,
    IMemberReachabilityExpander memberRoots,
    ITypeFactsReachabilityExpander typeFactsRoots) : IReachabilityDerivedRootExpander
{
    private readonly IRuntimeReachabilityExpander _runtimeRoots = runtimeRoots ??
        throw new ArgumentNullException(nameof(runtimeRoots));
    private readonly IMemberReachabilityExpander _memberRoots = memberRoots ??
        throw new ArgumentNullException(nameof(memberRoots));
    private readonly ITypeFactsReachabilityExpander _typeFactsRoots = typeFactsRoots ??
        throw new ArgumentNullException(nameof(typeFactsRoots));

    public void Expand(ReachabilitySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _runtimeRoots.Expand(session);
        _memberRoots.Expand(session);
        _typeFactsRoots.Expand(session);
    }
}
