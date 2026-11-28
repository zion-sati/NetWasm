// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0
using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;
// Replaces the x86 and Arm instruction sets' IsSupported queries (Fma.IsSupported, AdvSimd.Arm64.IsSupported and the
// rest, nested classes included) with false: NetWasm never targets those instruction sets, so the value depends on
// nothing a program can change. The constant-branch rules that follow then remove the paths they guard before
// reachability at every optimization level, as the managed inliner does from O1 up; O0 does not inline, and would
// otherwise reach what those paths call (Math.FusedMultiplyAdd's native fma), imports O1 never reaches.
internal sealed class HardwareIntrinsicSupportCallRewriter(
    ICalledMethodResolver calledMethods,
    ISymbolFormatter symbols) : IMethodRewriteRule
{
    private const string X86Namespace = "System.Runtime.Intrinsics.X86.";
    private const string ArmNamespace = "System.Runtime.Intrinsics.Arm.";

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var instructions = body.Instructions.ToArray();
        var changed = false;
        for (var index = 0; index < instructions.Length; index++)
        {
            var call = instructions[index];
            if (call.Operation != CilOperation.Call ||
                calledMethods.Resolve(call) is not { } target ||
                !target.Definition.IsStatic ||
                target.Definition.Name != "get_IsSupported" ||
                !IsForeignInstructionSet(symbols.Format(target.Definition.DeclaringType)))
            {
                continue;
            }
            instructions[index] = call with
            {
                Operation = CilOperation.LoadInt32,
                Operand = new CilOperand.ConstantI4(0),
            };
            changed = true;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    private static bool IsForeignInstructionSet(string type) =>
        type.StartsWith(X86Namespace, StringComparison.Ordinal) ||
        type.StartsWith(ArmNamespace, StringComparison.Ordinal);
}
