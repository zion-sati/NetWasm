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

internal interface IMemberReachabilityExpander
{
    // Adds the member descriptors and the members bounded execution runs, with what they reach.
    void Expand(ReachabilitySession session);
}
