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

internal interface IReachabilitySessionFactory
{
    // A closure's empty state, with the program's boundaries and the bodies a reduction rewrote.
    ReachabilitySession Create(
        MethodDefinitionModel? entryPoint,
        IReadOnlySet<EntityKey> outwardBoundaries,
        ImmutableDictionary<string, CilMethodBody>? rewrittenBodies);
}
