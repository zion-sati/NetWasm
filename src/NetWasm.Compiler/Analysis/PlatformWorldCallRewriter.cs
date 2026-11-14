using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// Replaces calls to CoreLib's PlatformWorld and NetWasmHost switches with the selected world's
// and host's values, so the constant-branch rules that follow remove the composition a world
// cannot bind, and the other host's code, before reachability, whatever the optimization settings.
internal sealed class PlatformWorldCallRewriter(
    ICalledMethodResolver calledMethods,
    ISymbolFormatter symbols,
    PlatformWorldCapabilities world) : IMethodRewriteRule
{
    private const string PlatformWorldType = "System.Runtime.InteropServices.PlatformWorld";
    private const string NetWasmHostType = "System.Runtime.InteropServices.NetWasmHost";

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
                !target.Definition.IsStatic)
            {
                continue;
            }
            var value = (symbols.Format(target.Definition.DeclaringType), target.Definition.Name) switch
            {
                (PlatformWorldType, "ImportsEventLoop") => world.EventLoop,
                (PlatformWorldType, "ImportsMonotonicClock") => world.MonotonicClock,
                (PlatformWorldType, "ImportsProcessExit") => world.ProcessExit,
                (PlatformWorldType, "HasSynchronousExports") => world.SynchronousExports,
                (PlatformWorldType, "HasHostEventLoop") => world.HostEventLoop,
                (PlatformWorldType, "ProvidesWasi") => world.Wasi,
                (NetWasmHostType, "get_IsJavaScriptHost") => world.JavaScriptHost,
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
