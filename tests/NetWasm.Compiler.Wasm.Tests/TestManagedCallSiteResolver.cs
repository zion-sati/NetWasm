using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission;
using System;
using NetWasm.Compiler.Core;

using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls.ManagedCallSites;

namespace NetWasm.Compiler.Wasm.Tests;

internal sealed class TestManagedCallSiteResolver(
    IMethodRepository methods,
    ICilTypeIdentityResolver identities) : IManagedCallSiteResolver
{
    private MethodInstanceModel ResolveTarget(CilInstruction instruction) =>
        instruction.Operand switch
        {
            CilOperand.MethodInstance method => method.Value,
            CilOperand.Entity method => Resolve(method.Key),
            _ => throw new InvalidOperationException(
                $"instruction {instruction.Operation} has no method operand"),
        };

    private MethodInstanceModel Resolve(EntityKey key)
    {
        var method = methods.GetMethod(key);
        return new MethodInstanceModel(
            method,
            identities.Resolve(method.DeclaringType),
            [],
            method.Signature);
    }

    public ManagedCallSite Resolve(InstructionEmissionRequest request)
    {
        var target = ResolveTarget(request.Instruction);
        var instructionIndex = request.Header.Instructions.IndexOf(request.Instruction);
        var constrainedType = instructionIndex > 0 &&
            request.Header.Instructions[instructionIndex - 1] is
            {
                Operation: CilOperation.Constrained,
                Operand: CilOperand.TypeIdentity constrained,
            }
                ? constrained.Value
                : null;
        return new ManagedCallSite(
            new ManagedCallSiteKey(new ManagedMethodIdentity("test-caller"), request.Instruction.Offset),
            ManagedCallOperation.Direct,
            new ManagedMethodIdentity(target.CanonicalName),
            target,
            constrainedType);
    }
}
