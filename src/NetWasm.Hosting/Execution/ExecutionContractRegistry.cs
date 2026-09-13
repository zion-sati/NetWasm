using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Hosting.Execution;

/// <summary>Resolves one exact immutable execution contract without aliases or export probing.</summary>
internal sealed class ExecutionContractRegistry : IExecutionContractRegistry
{
    private static readonly SearchValues<char> ContractNameCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789-./:");
    private static readonly SearchValues<char> VersionCharacters = SearchValues.Create("0123456789.");
    private readonly Dictionary<string, IExecutionContractDefinition> _contracts;

    internal ExecutionContractRegistry(IEnumerable<IExecutionContractDefinition> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        var indexed = new Dictionary<string, IExecutionContractDefinition>(StringComparer.Ordinal);
        foreach (var contract in contracts)
        {
            ArgumentNullException.ThrowIfNull(contract);
            ValidateKey(contract.Key);
            if (!indexed.TryAdd(contract.Key, contract))
            {
                throw new ArgumentException("Execution contract keys must be unique.", nameof(contracts));
            }
        }

        if (indexed.Count == 0)
        {
            throw new ArgumentException("At least one execution contract must be registered.", nameof(contracts));
        }

        _contracts = indexed;
    }

    public IExecutionContractDefinition Resolve(string key)
    {
        ValidateKey(key);
        return _contracts.TryGetValue(key, out var contract)
            ? contract
            : throw new NotSupportedException($"Execution contract '{key}' is not registered.");
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var separator = key.LastIndexOf('@');
        if (separator <= 0 || separator == key.Length - 1 || key.AsSpan(0, separator).Contains('@'))
        {
            throw new ArgumentException("Execution contract keys require one exact version.", nameof(key));
        }

        if (!(char.IsAsciiLetterLower(key[0]) || char.IsAsciiDigit(key[0]))
            || key.AsSpan().Trim().Length != key.Length
            || key.AsSpan(0, separator).ContainsAnyExcept(ContractNameCharacters)
            || key.AsSpan(separator + 1).ContainsAnyExcept(VersionCharacters))
        {
            throw new ArgumentException("Execution contract keys must use canonical lowercase syntax.", nameof(key));
        }

        var versionParts = key[(separator + 1)..].Split('.');
        if (versionParts.Length != 3 || versionParts.Any(part => part.Length == 0))
        {
            throw new ArgumentException("Execution contract keys require an exact numeric version.", nameof(key));
        }
    }
}
