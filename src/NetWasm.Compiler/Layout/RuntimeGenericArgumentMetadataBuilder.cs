using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class RuntimeGenericArgumentMetadataBuilder(
    ReachableProgram program,
    ManagedStaticDataBuildState state) : IRuntimeGenericArgumentMetadataBuilder
{
    private readonly ReachableProgram _program = program ??
        throw new ArgumentNullException(nameof(program));
    private readonly ManagedStaticDataBuildState _state = state ??
        throw new ArgumentNullException(nameof(state));

    public void Build()
    {
        if (!_program.RequiresGenericArguments)
        {
            return;
        }

        var typeIds = _state.PendingTypeFacts.ToDictionary(
            facts => facts.Identity,
            facts => facts.TypeId);
        var nextTypeId = typeIds.Values.DefaultIfEmpty(0).Max() + 1;
        var existingFactsCount = _state.PendingTypeFacts.Count;
        for (int index = 0; index < existingFactsCount; index++)
        {
            var facts = _state.PendingTypeFacts[index];
            var arguments = GetArguments(facts);
            if (arguments.IsDefaultOrEmpty)
            {
                continue;
            }

            var argumentTypeIds = ImmutableArray.CreateBuilder<int>(arguments.Length);
            foreach (var argument in arguments)
            {
                if (!typeIds.TryGetValue(argument.Identity, out var typeId))
                {
                    if (argument.Identity.Shape != CliTypeShape.GenericTypeParameter)
                    {
                        throw new CompilerException(new CompilerDiagnostic(
                            DiagnosticCode.RuntimeContract,
                            $"runtime generic argument '{argument.Identity}' has no type descriptor"));
                    }

                    typeId = nextTypeId++;
                    typeIds.Add(argument.Identity, typeId);
                    _state.MetadataTypeDescriptors.Add(new(argument.Identity, typeId));
                    _state.PendingTypeFacts.Add(new PendingRuntimeTypeFacts(
                        argument.Identity,
                        Definition: null,
                        typeId,
                        BaseTypeId: 0,
                        AssignableTypeIdsAddress: 0,
                        AssignableTypeIdCount: 0,
                        DelegateInvokeDescriptor: null,
                        Names: GetNames(argument.Name)));
                }
                argumentTypeIds.Add(typeId);
            }

            _state.PendingTypeFacts[index] = facts with
            {
                GenericArgumentTypeIds = argumentTypeIds.ToImmutable(),
            };
        }
    }

    private static ImmutableArray<RuntimeGenericArgument> GetArguments(
        PendingRuntimeTypeFacts facts)
    {
        if (facts.Identity.Shape == CliTypeShape.GenericInstantiation)
        {
            return [.. facts.Identity.TypeArguments.Select(argument =>
                GetConstructedArgument(facts, argument))];
        }
        if (facts.Identity.Shape != CliTypeShape.Named ||
            facts.Definition is not { GenericArity: > 0 } definition)
        {
            return [];
        }
        if (definition.GenericParameterNames.Length != definition.GenericArity)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"type definition '{definition.Key}' has inconsistent generic parameter names"));
        }

        return [.. Enumerable.Range(0, definition.GenericArity).Select(index =>
            new RuntimeGenericArgument(
                CliTypeIdentity.ScopedGenericParameter(
                    facts.Identity.CanonicalName,
                    method: false,
                    index),
                definition.GenericParameterNames[index]))];
    }

    private static RuntimeGenericArgument GetConstructedArgument(
        PendingRuntimeTypeFacts facts,
        CliTypeIdentity argument)
    {
        if (argument.Shape != CliTypeShape.GenericTypeParameter)
        {
            return new(argument, Name: null);
        }
        var definition = facts.Definition ?? throw new CompilerException(
            new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"constructed type '{facts.Identity}' has no type definition"));
        if ((uint)argument.GenericParameterIndex >=
            (uint)definition.GenericParameterNames.Length)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"constructed type '{facts.Identity}' has an invalid generic parameter index"));
        }
        return new(
            CliTypeIdentity.ScopedGenericParameter(
                facts.Identity.ElementType!.CanonicalName,
                method: false,
                argument.GenericParameterIndex),
            definition.GenericParameterNames[argument.GenericParameterIndex]);
    }

    private RuntimeTypeNames? GetNames(string? name)
    {
        if (_program.TypeNamePayload == RuntimeTypeNamePayload.None || name is null)
        {
            return null;
        }
        return new(
            _program.TypeNamePayload,
            (_program.TypeNamePayload & RuntimeTypeNamePayload.Name) != 0 ? name : null,
            Namespace: null,
            FullName: null,
            (_program.TypeNamePayload & RuntimeTypeNamePayload.DisplayName) != 0
                ? name
                : null);
    }

    private readonly record struct RuntimeGenericArgument(
        CliTypeIdentity Identity,
        string? Name);
}
