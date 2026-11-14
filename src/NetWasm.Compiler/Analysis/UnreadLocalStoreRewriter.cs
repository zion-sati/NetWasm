using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// Drops a store to a local nothing reads, with the load without effects that pushed its value. After a folded check
// cut off a cached source's last use (ArrayPoolEventSource log = ArrayPoolEventSource.Log; if (log.IsEnabled())
// ...), the static field is no longer read, so its type is not initialized for it. A pinned local keeps its stores:
// they pin what they hold.
internal sealed class UnreadLocalStoreRewriter(
    IControlFlowGraphBuilder graphs,
    IEffectFreeLoadFinder loads) : IMethodRewriteRule
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly IEffectFreeLoadFinder _loads = loads ?? throw new ArgumentNullException(nameof(loads));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var read = body.Instructions
            .Where(instruction => instruction.Operation is CilOperation.LoadLocal or CilOperation.LoadLocalAddress)
            .Select(instruction => ((CilOperand.Index)instruction.Operand).Value)
            .ToHashSet();
        if (!body.Instructions.Any(instruction => IsUnreadStore(instruction, read, body)))
        {
            return body;
        }

        var rewritten = body.Instructions.ToDictionary(instruction => instruction.Offset);
        var changed = false;
        foreach (var block in _graphs.Build(body).Blocks)
        {
            var operations = block.Instructions.Where(instruction => instruction.Operation != CilOperation.Nop).ToArray();
            for (var index = 0; index < operations.Length; index++)
            {
                if (!IsUnreadStore(operations[index], read, body) || _loads.FindStart(operations, index) is var start && start < 0)
                {
                    continue;
                }
                for (var dropped = start; dropped <= index; dropped++)
                {
                    rewritten[operations[dropped].Offset] = operations[dropped] with
                    {
                        Operation = CilOperation.Nop,
                        Operand = new CilOperand.None(),
                    };
                }
                changed = true;
            }
        }
        return changed ? body with { Instructions = [.. body.Instructions.Select(instruction => rewritten[instruction.Offset])] } : body;
    }

    private static bool IsUnreadStore(CilInstruction instruction, HashSet<int> read, CilMethodBody body) =>
        instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index local } &&
        !read.Contains(local.Value) && !body.PinnedLocals.Contains(local.Value);
}
