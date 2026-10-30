using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// ReceiverBound, when known, is a static type every receiver at the call
// site is assignable to, so a target outside it never runs there.
internal sealed record DispatchDeclaration(
    string Caller,
    int IlOffset,
    MethodInstanceModel Declaration,
    CilOperation Operation,
    CliTypeIdentity? ReceiverBound = null);
