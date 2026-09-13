using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed record DispatchDeclaration(
    string Caller,
    int IlOffset,
    MethodInstanceModel Declaration,
    CilOperation Operation);
