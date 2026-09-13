using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed record ReachabilityInstructionRequest(
    MethodInstanceModel Method,
    CilMethodBody Body);
