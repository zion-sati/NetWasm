namespace NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;

public sealed record ObjectArrayDelegateAdapterPlan(
    MethodInstanceModel Factory,
    CliTypeIdentity DelegateType,
    MethodInstanceModel? Invoke,
    MethodInstanceModel Target,
    bool IsSupported);
