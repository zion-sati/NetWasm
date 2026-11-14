using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

// RuntimeInformation.IsOSPlatform(OSPlatform.Windows), and its Linux, OSX and FreeBSD forms, are false on NetWasm, as
// OperatingSystem.IsWindows() already is, and so is OperatingSystem.IsOSPlatform("windows") for any literal platform
// name but WASI's and the browser's. Libraries built for .NET Standard guard their native calls this way (Serilog's
// console sink enables Windows virtual-terminal processing behind IsOSPlatform(OSPlatform.Windows)); folding the check
// before reachability leaves the guarded code, and its P/Invokes, out of the program. The platform comes from a
// property call or a string literal immediately before the check, which no branch enters between.
internal sealed class KnownPlatformCheckRewriter(ICalledMethodResolver calledMethods) : IMethodRewriteRule
{
    private const string RuntimeInformation = "System.Runtime.InteropServices.RuntimeInformation";
    private const string OSPlatform = "System.Runtime.InteropServices.OSPlatform";

    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ImmutableArray<CilInstruction>.Builder? rewritten = null;
        HashSet<int>? targets = null;
        for (var index = 1; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            if (instruction.Operation != CilOperation.Call ||
                _calledMethods.Resolve(instruction) is not { Definition: { Name: "IsOSPlatform", IsStatic: true } } check)
            {
                continue;
            }
            var platform = body.Instructions[index - 1];
            var never = check.DeclaringType.FullName switch
            {
                RuntimeInformation => platform.Operation == CilOperation.Call &&
                    _calledMethods.Resolve(platform) is
                    {
                        Definition: { Name: "get_Windows" or "get_Linux" or "get_OSX" or "get_FreeBSD", IsStatic: true },
                        DeclaringType.FullName: OSPlatform,
                    },
                "System.OperatingSystem" => platform is { Operation: CilOperation.LoadString, Operand: CilOperand.UserString { Value: var name } } &&
                    !name.Equals("WASI", StringComparison.OrdinalIgnoreCase) && !name.Equals("BROWSER", StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
            if (!never || (targets ??= Targets(body)).Contains(instruction.Offset)) continue;
            rewritten ??= body.Instructions.ToBuilder();
            rewritten[index - 1] = platform with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
            rewritten[index] = instruction with { Operation = CilOperation.LoadInt32, Operand = new CilOperand.ConstantI4(0) };
        }
        return rewritten is null ? body : body with { Instructions = rewritten.ToImmutable() };
    }

    private static HashSet<int> Targets(CilMethodBody body)
    {
        var targets = new HashSet<int>();
        foreach (var instruction in body.Instructions)
        {
            switch (instruction.Operand)
            {
                case CilOperand.BranchTarget branch:
                    targets.Add(branch.Offset);
                    break;
                case CilOperand.SwitchTargets cases:
                    targets.UnionWith(cases.Offsets);
                    break;
            }
        }
        foreach (var region in body.ExceptionRegions)
        {
            targets.Add(region.TryOffset);
            targets.Add(region.HandlerOffset);
            if (region.FilterOffset is { } filter) targets.Add(filter);
        }
        return targets;
    }
}
