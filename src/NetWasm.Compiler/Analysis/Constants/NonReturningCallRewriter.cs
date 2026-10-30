using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Constants;

// A callee whose body has no ret can only leave by throwing, as upstream's ThrowHelper methods
// do. The call's fall-through is then dead, yet the control-flow graph joins it to the code after
// the call, so values that code reads stay live (and rooted) across the call and the cold path
// merges back into the hot one. A throw after the call ends the block the way an inline throw
// does; it never runs. UnreachableInstructionRewriter, which runs later, removes what only the
// fall-through reached.
internal sealed class NonReturningCallRewriter(
    ICalledMethodResolver calledMethods,
    IMethodBodyReader methodBodies,
    IRuntimeIntrinsicRegistry intrinsics) : IMethodRewriteRule
{
    private readonly ConcurrentDictionary<string, bool> _returns = [];

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        List<CilInstruction>[]? replacements = null;
        for (var index = 0; index < body.Instructions.Length - 1; index++)
        {
            var call = body.Instructions[index];
            if (call.Operation != CilOperation.Call ||
                body.Instructions[index + 1].Operation is CilOperation.Throw or CilOperation.Rethrow ||
                calledMethods.Resolve(call) is not { } target ||
                Returns(target))
            {
                continue;
            }

            replacements ??= [.. body.Instructions.Select(static instruction => new List<CilInstruction> { instruction })];
            if (target.Signature.ReturnType != CliValueKind.Void)
            {
                replacements[index].Add(Synthetic(CilOperation.Pop));
            }
            replacements[index].Add(Synthetic(CilOperation.LoadNull));
            replacements[index].Add(Synthetic(CilOperation.Throw));
        }

        return replacements is null ? body : RenumberedInstructions.Splice(body, replacements);
    }

    private bool Returns(MethodInstanceModel target)
    {
        var definition = target.Definition;
        if (!definition.HasBody || definition.UnsafeAccessor is not null ||
            intrinsics.TryGetIntrinsic(definition.Key, out _) ||
            target.DeclaringType.ContainsGenericParameters ||
            target.MethodArguments.Any(static type => type.ContainsGenericParameters))
        {
            return true;
        }
        return _returns.GetOrAdd(target.CanonicalName, _ => methodBodies.ReadMethodBody(target).Instructions
            .Any(static instruction => instruction.Operation == CilOperation.Return));
    }

    private static CilInstruction Synthetic(CilOperation operation) => new(0, 1, operation, new CilOperand.None());
}
