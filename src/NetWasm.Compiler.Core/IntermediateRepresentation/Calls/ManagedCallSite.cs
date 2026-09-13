using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

public sealed record ManagedCallSite(
    ManagedCallSiteKey Key,
    ManagedCallOperation Operation,
    ManagedMethodIdentity TargetIdentity,
    MethodInstanceModel Target,
    CliTypeIdentity? ConstrainedType);
