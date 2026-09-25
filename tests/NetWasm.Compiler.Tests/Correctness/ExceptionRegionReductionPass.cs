using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class ExceptionRegionReductionPass : IGeneratedCilReductionPass
{
    public string Name => "exception-regions";

    public IEnumerable<GeneratedCilReductionCase> Generate(
        GeneratedCilReductionCase candidate)
    {
        foreach (var region in candidate.Program.ExceptionRegions)
        {
            var removedTryBlocks = candidate.Program.Blocks
                .Where(block => block.ExceptionRegionPath.Any(membership =>
                    membership.RegionId == region.Id &&
                    membership.Part == GeneratedExceptionRegionPart.Try))
                .Select(block => block.Id)
                .ToHashSet();
            var removedBlocks = candidate.Program.Blocks
                .Where(block => block.ExceptionRegionPath.Any(membership =>
                    membership.RegionId == region.Id &&
                    membership.Part is GeneratedExceptionRegionPart.Filter or
                        GeneratedExceptionRegionPart.Handler))
                .Select(block => block.Id)
                .ToHashSet();
            var retained = candidate.Program.Blocks
                .Where(block => !removedBlocks.Contains(block.Id))
                .Select(block => block with
                {
                    ExceptionRegionPath = [.. block.ExceptionRegionPath.Where(
                        membership => membership.RegionId != region.Id)],
                })
                .ToArray();
            if (retained.Length == 0)
            {
                continue;
            }
            var paths = retained.ToDictionary(
                block => block.Id,
                block => block.ExceptionRegionPath);
            var blocks = retained.Select(block => block with
            {
                Instructions = RewriteInstructions(
                    block,
                    region,
                    paths,
                    removedTryBlocks.Contains(block.Id)),
            });
            yield return candidate with
            {
                Program = candidate.Program with
                {
                    Blocks = [.. blocks],
                    ExceptionRegions = candidate.Program.ExceptionRegions.Remove(region),
                },
            };
        }
    }

    private static System.Collections.Immutable.ImmutableArray<GeneratedCilInstruction>
        RewriteInstructions(
            GeneratedCilBlock block,
            GeneratedCilExceptionRegion removed,
            Dictionary<int,
                System.Collections.Immutable.ImmutableArray<GeneratedExceptionRegionMembership>>
                paths,
            bool wasInRemovedTry)
    {
        var instructions = block.Instructions;
        var terminal = instructions[^1];
        if (terminal.Operation == CilOperation.Throw &&
            removed.Kind is CilExceptionRegionKind.Catch or
                CilExceptionRegionKind.Filter &&
            wasInRemovedTry &&
            paths.ContainsKey(removed.HandlerEndBlock))
        {
            return instructions
                .RemoveAt(instructions.Length - 1)
                .Add(new(CilOperation.Pop, new GeneratedCilOperand.None()))
                .Add(new(
                    CilOperation.Branch,
                    new GeneratedCilOperand.BlockTarget(removed.HandlerEndBlock)));
        }
        if (terminal.Operation == CilOperation.Leave &&
            terminal.Operand is GeneratedCilOperand.BlockTarget target &&
            paths.TryGetValue(target.BlockId, out var targetPath) &&
            block.ExceptionRegionPath.AsSpan().SequenceEqual(targetPath.AsSpan()))
        {
            return instructions.SetItem(
                instructions.Length - 1,
                terminal with { Operation = CilOperation.Branch });
        }
        return instructions;
    }
}
