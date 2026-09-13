using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal enum InstructionFamily
{
    ConstantsStackLocalsArguments,
    Numeric,
    ValueObjectBlockMemory,
    ArraysFieldsStatics,
    AllocationBoxingTypes,
    CallsAndCallableLoading,
    Delegates,
    StructuredControlFlow,
    ExceptionsAndRoots,
}

internal sealed record InstructionEmissionRequest(
    StructuredMethodHeader Header,
    CilInstruction Instruction,
    List<CliValueKind> Stack,
    MethodEmissionContext Context,
    InstructionModuleTarget Target);

internal sealed class InstructionCommand : IInstructionCommand
{
    private readonly Action<
        InstructionEmissionRequest,
        IWasmInstructionWriter,
        IFunctionIndexResolver> _emit;

    public InstructionCommand(
        CilOperation operation,
        InstructionFamily family,
        Action<InstructionEmissionRequest, IWasmInstructionWriter> emit) :
        this(operation, family, (request, code, _) => emit(request, code))
    {
    }

    public InstructionCommand(
        CilOperation operation,
        InstructionFamily family,
        Action<
            InstructionEmissionRequest,
            IWasmInstructionWriter,
            IFunctionIndexResolver> emit)
    {
        Operation = operation;
        Family = family;
        _emit = emit;
    }

    public CilOperation Operation { get; }
    public InstructionFamily Family { get; }

    public void Emit(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices) => _emit(request, code, functionIndices);
}

internal interface IInstructionCommandProvider
{
    ImmutableArray<InstructionCommand> Commands { get; }
}

internal abstract class InstructionCommandProvider : IInstructionCommandProvider
{
    public abstract ImmutableArray<InstructionCommand> Commands { get; }

    public ImmutableArray<CilOperation> Operations =>
        [.. Commands.Select(command => command.Operation)];
}

internal sealed class InstructionCommandRegistry : IInstructionCommandResolver
{
    private readonly ImmutableDictionary<CilOperation, IInstructionCommand> _commands;

    public InstructionCommandRegistry(
        IEnumerable<IInstructionCommand> commands,
        IEnumerable<CilOperation>? requiredOperations = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var registrations =
            ImmutableDictionary.CreateBuilder<CilOperation, IInstructionCommand>();
        foreach (var command in commands)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (!registrations.TryAdd(command.Operation, command))
            {
                throw new InvalidOperationException(
                    $"CIL operation '{command.Operation}' has more than one instruction command.");
            }
        }

        var required = (requiredOperations ?? SupportedCil.Operations)
            .Distinct()
            .ToImmutableArray();
        var missing = required.Where(operation => !registrations.ContainsKey(operation))
            .Order()
            .ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidOperationException(
                $"CIL operation '{missing[0]}' has no instruction command.");
        }
        _commands = registrations.ToImmutable();
    }

    public IInstructionCommand Resolve(CilOperation operation) =>
        _commands.TryGetValue(operation, out var command)
            ? command
            : throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedCil,
                $"CIL operation '{operation}' is not supported"));

}
