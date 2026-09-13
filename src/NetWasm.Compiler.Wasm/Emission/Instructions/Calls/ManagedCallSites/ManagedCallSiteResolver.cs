using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls.ManagedCallSites;

internal sealed class ManagedCallSiteResolver : IManagedCallSiteResolver
{
    public ManagedCallSite Resolve(InstructionEmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caller = request.Context.CallerIdentity;
        if (string.IsNullOrWhiteSpace(caller.CanonicalName))
        {
            throw new InvalidOperationException(
                "Managed instruction emission requires a canonical caller identity.");
        }

        var key = new ManagedCallSiteKey(caller, request.Instruction.Offset);
        if (!request.Target.ManagedCallSites.TryGetValue(key, out var callSite))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.CompilerInvariant,
                "MISSING_MANAGED_CALL_SITE: managed instruction emission requires a canonical call-site fact.",
                key.Caller.CanonicalName,
                key.InstructionOffset));
        }

        return callSite;
    }
}
