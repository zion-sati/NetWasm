namespace NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;

public sealed record ManagedDelegateValueBinding(
    CliTypeIdentity SourceType,
    CliTypeIdentity TargetType,
    ManagedDelegateAdaptation Adaptation);
