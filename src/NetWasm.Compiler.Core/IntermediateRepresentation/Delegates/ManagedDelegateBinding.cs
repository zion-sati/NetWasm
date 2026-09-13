using System.Collections.Immutable;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;

public sealed record ManagedDelegateBinding(
    ManagedMethodIdentity InvokeIdentity,
    ManagedMethodIdentity TargetIdentity,
    MethodInstanceModel Invoke,
    MethodInstanceModel Target,
    ImmutableArray<ManagedDelegateValueBinding> ParameterBindings,
    ManagedDelegateValueBinding ReturnBinding);
