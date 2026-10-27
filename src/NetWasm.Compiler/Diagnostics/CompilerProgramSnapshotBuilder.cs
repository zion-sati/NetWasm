using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerProgramSnapshotBuilder(
    ICompilerCilOperandFormatter operandFormatter) : ICompilerProgramSnapshotBuilder
{
    private readonly ICompilerCilOperandFormatter _operandFormatter =
        operandFormatter ?? throw new ArgumentNullException(nameof(operandFormatter));

    public CompilerProgramArtifact BuildProgram(ISymbolFormatter symbols, ReachableProgram program)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(program);
        var methods = program.Methods
            .OrderBy(pair => pair.Key.Assembly.Name, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.MetadataToken)
            .Select(pair => BuildMethod(
                symbols.Format(pair.Value.Method.Definition),
                pair.Value))
            .Concat(program.ConstructedMethods
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => BuildMethod(pair.Key, pair.Value)))
            .ToArray();
        return new CompilerProgramArtifact(
            program.EntryPoint is { } entryPoint ? symbols.Format(entryPoint) : null,
            [.. program.Methods.Keys.Select(key => key.ToString()).Order(StringComparer.Ordinal)],
            [.. program.ConstructedMethods.Keys.Order(StringComparer.Ordinal)],
            [.. program.Types.Select(type => type.ToString()).Order(StringComparer.Ordinal)],
            [.. program.ConstructedTypes.Select(type => type.CanonicalName).Order(StringComparer.Ordinal)],
            [.. program.TypeTestSites
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new CompilerTypeTestSiteArtifact(
                    pair.Key,
                    pair.Value.TargetType.CanonicalName,
                    [.. pair.Value.MatchingTypes
                        .Select(type => type.CanonicalName)
                        .Order(StringComparer.Ordinal)]))],
            [.. program.Fields.Select(field => field.ToString()).Order(StringComparer.Ordinal)],
            [.. program.Exports.OrderBy(export => export.Name, StringComparer.Ordinal)],
            [.. program.AllocatingMethods.Select(method => method.ToString()).Order(StringComparer.Ordinal)],
            program.ConstructedAllocatingMethods,
            [.. program.DispatchCallSites.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new CompilerDispatchSiteArtifact(
                    pair.Key,
                    pair.Value.Declaration.CanonicalName,
                    [.. pair.Value.Targets.Select(target => new CompilerDispatchTargetArtifact(
                        target.ReceiverType.CanonicalName,
                        target.Method.CanonicalName))]))],
            [.. program.DelegateBindings.Select(binding => new CompilerDelegateBindingArtifact(
                binding.InvokeIdentity.CanonicalName,
                binding.TargetIdentity.CanonicalName,
                [.. binding.ParameterBindings.Select(parameter => new CompilerDelegateValueArtifact(
                    parameter.SourceType.CanonicalName,
                    parameter.TargetType.CanonicalName,
                    parameter.Adaptation))],
                new CompilerDelegateValueArtifact(
                    binding.ReturnBinding.SourceType.CanonicalName,
                    binding.ReturnBinding.TargetType.CanonicalName,
                    binding.ReturnBinding.Adaptation)))],
            methods);
    }

    private CompilerMethodArtifact BuildMethod(string identity, ManagedMethodBody method)
    {
        var body = method.Body;
        var graph = method.ControlFlow.Graph;
        return new CompilerMethodArtifact(
            identity,
            [.. body.Instructions.Select(BuildInstruction)],
            [.. body.ExceptionRegions.Select(region => new CompilerExceptionRegionArtifact(
                region.Kind,
                region.TryOffset,
                region.TryLength,
                region.HandlerOffset,
                region.HandlerLength,
                region.CatchType,
                region.CatchTypeIdentity?.CanonicalName,
                region.FilterOffset))],
            [.. graph.Blocks.Select(block => new CompilerBlockArtifact(
                block.Index,
                block.StartOffset,
                graph.Successors[block.Index],
                graph.ExceptionalSuccessors[block.Index],
                method.ControlFlow.EntryStacks.TryGetValue(block.Index, out var entryStack)
                    ? [.. entryStack.Select(value => value.ToString())]
                    : []))],
            [.. method.ControlFlow.InstructionEntryStacks.OrderBy(pair => pair.Key)
                .Select(pair => new CompilerInstructionStackArtifact(
                    pair.Key,
                    [.. pair.Value.Select(value => value.ToString())]))]);
    }

    private CompilerCilInstructionArtifact BuildInstruction(CilInstruction instruction) => new(
        instruction.Offset,
        instruction.NextOffset,
        instruction.Operation.ToString(),
        instruction.Operand is CilOperand.None
            ? null
            : _operandFormatter.FormatOperand(instruction.Operand));
}
