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

    public object BuildProgram(ISymbolFormatter symbols, ReachableProgram program)
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
        return new
        {
            EntryPoint = symbols.Format(program.EntryPoint),
            ReachableMethods = program.Methods.Keys.Select(key => key.ToString())
                .Order(StringComparer.Ordinal).ToArray(),
            ConstructedMethods = program.ConstructedMethods.Keys
                .Order(StringComparer.Ordinal).ToArray(),
            ReachableTypes = program.Types.Select(type => type.ToString())
                .Order(StringComparer.Ordinal).ToArray(),
            ConstructedTypes = program.ConstructedTypes
                .Select(type => type.CanonicalName)
                .Order(StringComparer.Ordinal).ToArray(),
            TypeTestSites = program.TypeTestSites
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new
                {
                    pair.Key,
                    TargetType = pair.Value.TargetType.CanonicalName,
                    MatchingTypes = pair.Value.MatchingTypes
                        .Select(type => type.CanonicalName)
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                })
                .ToArray(),
            ReachableFields = program.Fields.Select(field => field.ToString())
                .Order(StringComparer.Ordinal).ToArray(),
            Exports = program.Exports.OrderBy(export => export.Name, StringComparer.Ordinal),
            AllocatingMethods = program.AllocatingMethods.Select(method => method.ToString())
                .Order(StringComparer.Ordinal).ToArray(),
            program.ConstructedAllocatingMethods,
            DispatchSites = program.DispatchCallSites.OrderBy(
                pair => pair.Key,
                StringComparer.Ordinal).Select(pair => new
                {
                    pair.Key,
                    Declaration = pair.Value.Declaration.CanonicalName,
                    Targets = pair.Value.Targets.Select(target => new
                    {
                        Receiver = target.ReceiverType.CanonicalName,
                        Method = target.Method.CanonicalName,
                    }),
                }),
            DelegateBindings = program.DelegateBindings.Select(binding => new
            {
                Invoke = binding.InvokeIdentity.CanonicalName,
                Target = binding.TargetIdentity.CanonicalName,
                Parameters = binding.ParameterBindings.Select(parameter => new
                {
                    Source = parameter.SourceType.CanonicalName,
                    Target = parameter.TargetType.CanonicalName,
                    parameter.Adaptation,
                }),
                Return = new
                {
                    Source = binding.ReturnBinding.SourceType.CanonicalName,
                    Target = binding.ReturnBinding.TargetType.CanonicalName,
                    binding.ReturnBinding.Adaptation,
                },
            }),
            Methods = methods,
        };
    }

    private object BuildMethod(string identity, ManagedMethodBody method)
    {
        var body = method.Body;
        var graph = method.ControlFlow.Graph;
        return new
        {
            Identity = identity,
            Instructions = body.Instructions.Select(BuildInstruction),
            ExceptionRegions = body.ExceptionRegions.Select(region => new
            {
                region.Kind,
                region.TryOffset,
                region.TryLength,
                region.HandlerOffset,
                region.HandlerLength,
                region.CatchType,
                region.FilterOffset,
            }),
            Blocks = graph.Blocks.Select(block => new
            {
                block.Index,
                block.StartOffset,
                Successors = graph.Successors[block.Index],
                ExceptionalSuccessors = graph.ExceptionalSuccessors[block.Index],
                EntryStack = method.ControlFlow.EntryStacks.TryGetValue(
                    block.Index,
                    out var entryStack)
                    ? entryStack.Select(value => value.ToString()).ToArray()
                    : [],
            }),
            InstructionStacks = method.ControlFlow.InstructionEntryStacks.OrderBy(pair => pair.Key)
                .Select(pair => new
                {
                    Offset = pair.Key,
                    Stack = pair.Value.Select(value => value.ToString()),
                }),
        };
    }

    private object BuildInstruction(CilInstruction instruction) => new
    {
        instruction.Offset,
        instruction.NextOffset,
        Operation = instruction.Operation.ToString(),
        Operand = instruction.Operand is CilOperand.None
            ? null
            : _operandFormatter.FormatOperand(instruction.Operand),
    };
}
