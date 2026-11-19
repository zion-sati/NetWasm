// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// How instructions move values on the evaluation stack, for rules that follow
// a value from the instruction that pushes it to the one that takes it before
// the body's stacks are validated.
internal interface IStackEffectResolver
{
    // How many values an instruction pops and pushes, or null for one this
    // does not follow.
    (int Pops, int Pushes)? Resolve(CilInstruction instruction);
}

internal sealed class StackEffectResolver(ICalledMethodResolver calledMethods) : IStackEffectResolver
{
    private readonly ICalledMethodResolver _calledMethods = calledMethods ??
        throw new ArgumentNullException(nameof(calledMethods));

    public (int Pops, int Pushes)? Resolve(CilInstruction instruction) => instruction.Operation switch
    {
        CilOperation.Call or CilOperation.CallVirtual => _calledMethods.Resolve(instruction) is { } callee
            ? (callee.Signature.ParameterTypes.Length + (callee.Definition.IsStatic ? 0 : 1),
                callee.Signature.ReturnType == CliValueKind.Void ? 0 : 1)
            : null,
        CilOperation.NewObject => _calledMethods.Resolve(instruction) is { } constructor
            ? (constructor.Signature.ParameterTypes.Length, 1)
            : null,
        _ => CilStackEffects.Of(instruction),
    };
}
