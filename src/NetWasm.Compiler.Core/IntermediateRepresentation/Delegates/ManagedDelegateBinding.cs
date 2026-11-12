using System.Collections.Immutable;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;

public sealed record ManagedDelegateBinding(
    ManagedMethodIdentity InvokeIdentity,
    ManagedMethodIdentity TargetIdentity,
    MethodInstanceModel Invoke,
    MethodInstanceModel Target,
    ImmutableArray<ManagedDelegateValueBinding> ParameterBindings,
    ManagedDelegateValueBinding ReturnBinding)
{
    // A static target closed over its first argument (ECMA-335 II.14.6.1), such as an
    // extension method's group: the delegate's target is that argument, and the parameter
    // bindings pair the Invoke's parameters with the target's remaining ones.
    public bool ClosedOverFirstArgument { get; init; }

    // The delegate's target is the target method's first argument: its this, or the first
    // parameter a static target is closed over.
    public bool PassesTarget => !Target.Definition.IsStatic || ClosedOverFirstArgument;
}
