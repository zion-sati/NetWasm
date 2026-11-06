using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public sealed record RootMapAnalysisRequest(
    ManagedMethodBody Method,
    IMembership<EntityKey> AllocatingMethods,
    IMembership<string> AllocatingConstructedMethods);
