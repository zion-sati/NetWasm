// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.PlatformServices;

// Applies the program's declared platform to CoreLib before reachability: a call to
// PlatformServiceBindings.CreatePlatform becomes the platform's constructor, so the default it
// replaces is not reached, and a call to DescribeDeclaredPlatform becomes the platform's
// description.
internal sealed class PlatformServiceBindingRewriter(
    ICalledMethodResolver calledMethods,
    ISymbolFormatter symbols,
    PlatformWorldCapabilities world) : IMethodRewriteRule
{
    private const string BindingsType = "System.Runtime.InteropServices.PlatformServiceBindings";

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (world.DeclaredPlatform is not { } platform)
        {
            return body;
        }

        var instructions = body.Instructions.ToArray();
        var changed = false;
        for (var index = 0; index < instructions.Length; index++)
        {
            var call = instructions[index];
            if (call.Operation != CilOperation.Call ||
                calledMethods.Resolve(call) is not { } target ||
                !target.Definition.IsStatic ||
                symbols.Format(target.Definition.DeclaringType) != BindingsType)
            {
                continue;
            }

            CilInstruction? replacement = target.Definition.Name switch
            {
                "CreatePlatform" => call with
                {
                    Operation = CilOperation.NewObject,
                    Operand = new CilOperand.MethodInstance(platform.Constructor),
                },
                "DescribeDeclaredPlatform" => call with
                {
                    Operation = CilOperation.LoadString,
                    Operand = new CilOperand.UserString(platform.Description),
                },
                _ => null,
            };
            if (replacement is not null)
            {
                instructions[index] = replacement;
                changed = true;
            }
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }
}
