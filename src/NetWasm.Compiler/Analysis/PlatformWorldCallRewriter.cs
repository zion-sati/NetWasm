using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// Replaces calls to CoreLib's PlatformWorld switches with the selected world's values, so the
// constant-branch rules that follow remove the composition a world cannot bind before
// reachability, whatever the optimization settings.
internal sealed class PlatformWorldCallRewriter(
    ICalledMethodResolver calledMethods,
    ISymbolFormatter symbols,
    PlatformWorldCapabilities world) : IMethodRewriteRule
{
    private const string PlatformWorldType = "System.Runtime.InteropServices.PlatformWorld";

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
                symbols.Format(target.Definition.DeclaringType) != PlatformWorldType)
            {
                continue;
            }
            var value = target.Definition.Name switch
            {
                "ImportsEventLoop" => world.EventLoop,
                "ImportsMonotonicClock" => world.MonotonicClock,
                "ImportsProcessExit" => world.ProcessExit,
                "HasSynchronousExports" => world.SynchronousExports,
                "HasHostEventLoop" => world.HostEventLoop,
                "ProvidesWasi" => world.Wasi,
                _ => (bool?)null,
            };
            if (value is not { } known)
            {
                continue;
            }
            instructions[index] = call with
            {
                Operation = CilOperation.LoadInt32,
                Operand = new CilOperand.ConstantI4(known ? 1 : 0),
            };
            changed = true;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }
}
