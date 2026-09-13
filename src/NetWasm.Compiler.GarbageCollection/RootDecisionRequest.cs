using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public sealed record RootDecisionRequest(
    ManagedMethodBody Method,
    int Block,
    CilInstruction Instruction,
    ISet<EntityKey> AllocatingMethods,
    ISet<string> AllocatingConstructedMethods);
