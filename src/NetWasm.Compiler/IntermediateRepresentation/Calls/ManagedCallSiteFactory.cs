using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

using System;
using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.IntermediateRepresentation.Calls;

internal sealed class ManagedCallSiteFactory(IManagedMethodIdentityFactory identities)
    : IManagedCallSiteFactory
{
    public ManagedCallSite Create(
        MethodInstanceModel caller,
        CilMethodBody body,
        int instructionIndex,
        MethodInstanceModel target,
        ITypeOperandResolver typeOperands)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfNegative(instructionIndex);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(typeOperands);

        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            instructionIndex,
            body.Instructions.Length);

        var instruction = body.Instructions[instructionIndex];
        var constrainedType = instructionIndex > 0 &&
            body.Instructions[instructionIndex - 1].Operation == CilOperation.Constrained
                ? typeOperands.Resolve(body.Instructions[instructionIndex - 1], caller)
                : null;

        return new ManagedCallSite(
            new ManagedCallSiteKey(identities.Create(caller), instruction.Offset),
            Translate(instruction.Operation),
            identities.Create(target),
            target,
            constrainedType);
    }

    private static ManagedCallOperation Translate(CilOperation operation) => operation switch
    {
        CilOperation.Call => ManagedCallOperation.Direct,
        CilOperation.CallVirtual => ManagedCallOperation.Virtual,
        CilOperation.NewObject => ManagedCallOperation.Construct,
        CilOperation.LoadFunction => ManagedCallOperation.LoadFunction,
        CilOperation.LoadVirtualFunction => ManagedCallOperation.LoadVirtualFunction,
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported managed call operation."),
    };
}
