using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public sealed record RootMapAnalysisRequest(
    ManagedMethodBody Method,
    ISet<EntityKey> AllocatingMethods,
    ISet<string> AllocatingConstructedMethods);
