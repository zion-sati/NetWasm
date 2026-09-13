using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal interface IEntryPointValidator
{
    void Validate(
        MethodDefinitionModel entryPoint,
        ISymbolFormatter symbols,
        CompilerEntryPointKind kind);
}

internal sealed class EntryPointValidator : IEntryPointValidator
{
    private readonly ImmutableDictionary<CompilerEntryPointKind,
        IEntryPointValidationStrategy> _strategies;

    public EntryPointValidator(IEnumerable<IEntryPointValidationStrategy> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        var entries = ImmutableDictionary.CreateBuilder<CompilerEntryPointKind,
            IEntryPointValidationStrategy>();
        foreach (var strategy in strategies)
        {
            if (!entries.TryAdd(strategy.Kind, strategy))
            {
                throw new InvalidOperationException(
                    $"entry-point validator '{strategy.Kind}' is registered more than once");
            }
        }

        foreach (var kind in Enum.GetValues<CompilerEntryPointKind>())
        {
            if (!entries.ContainsKey(kind))
            {
                throw new InvalidOperationException(
                    $"entry-point validator '{kind}' is not registered");
            }
        }

        _strategies = entries.ToImmutable();
    }

    public void Validate(
        MethodDefinitionModel entryPoint,
        ISymbolFormatter symbols,
        CompilerEntryPointKind kind)
    {
        ArgumentNullException.ThrowIfNull(entryPoint);
        ArgumentNullException.ThrowIfNull(symbols);
        _strategies[kind].Validate(entryPoint, symbols);
    }
}
