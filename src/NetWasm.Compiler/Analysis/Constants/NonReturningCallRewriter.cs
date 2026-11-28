// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
// does; it never runs, so it lowers to Wasm's unreachable (CilOperand.NeverRuns) rather than to a
// throw of null: each cold path then ends in the call and unreachable alone, which Binaryen folds
// into one tail outside the loop that branches to it, where a shared null-reference throw would
// leave the call inside the loop. UnreachableInstructionRewriter, which runs later, removes what
// only the fall-through reached.
internal sealed class NonReturningCallRewriter(
    ICalledMethodResolver calledMethods,
    ICalleeMethodBodyReader methodBodies,
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
            replacements[index].Add(new(0, 1, CilOperation.Throw, new CilOperand.NeverRuns()));
        }

        return replacements is null ? body : RenumberedInstructions.Splice(body, replacements);
    }

    private bool Returns(MethodInstanceModel target)
    {
        var definition = target.Definition;
        // An attribute query the lowering may yet replace with its answer (after this rewrite runs) returns, whatever
        // its fallback body does: dropping the code after it would leave the answer nowhere to go.
        if (Attributes.AttributeQueryApis.IsQuery(target) ||
            !definition.HasBody || definition.UnsafeAccessor is not null ||
            intrinsics.TryGetIntrinsic(definition.Key, out _) ||
            target.DeclaringType.ContainsGenericParameters ||
            target.MethodArguments.Any(static type => type.ContainsGenericParameters))
        {
            return true;
        }
        // A body that cannot be decoded may return, as far as this rewrite knows.
        return _returns.GetOrAdd(target.CanonicalName, _ => !methodBodies.TryReadMethodBody(target, out var body) ||
            body.Instructions.Any(static instruction => instruction.Operation == CilOperation.Return));
    }

    private static CilInstruction Synthetic(CilOperation operation) => new(0, 1, operation, new CilOperand.None());
}
