using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Symbols;

public interface IRuntimeTypeSymbolTableBuilder
{
    RuntimeTypeSymbolTable Build(
        ITypeDescriptorSource descriptors,
        ITypeIdentityResolver identities,
        ITypeDefinitionResolver definitions,
        ImmutableArray<AssemblyIdentity> assemblies,
        IAssemblyIdentityFormatter assemblyNames,
        IRuntimeTypeAssemblyResolver assemblyOf);
}

// Names every type the program gives a runtime type ID: class, constructed, value and metadata (generic
// parameter) descriptors; and every assembly Type.Assembly can report, by its type table index, in the entry
// whose ID is the largest type ID + 1 + that index, with its display name. An exception type is one whose base
// types reach System.Exception.
public sealed class RuntimeTypeSymbolTableBuilder : IRuntimeTypeSymbolTableBuilder
{
    public RuntimeTypeSymbolTable Build(
        ITypeDescriptorSource descriptors,
        ITypeIdentityResolver identities,
        ITypeDefinitionResolver definitions,
        ImmutableArray<AssemblyIdentity> assemblies,
        IAssemblyIdentityFormatter assemblyNames,
        IRuntimeTypeAssemblyResolver assemblyOf)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(assemblyNames);
        ArgumentNullException.ThrowIfNull(assemblyOf);
        if (assemblies.IsDefault)
            throw new ArgumentException("The runtime assemblies must be initialized.", nameof(assemblies));

        var types = new SortedDictionary<int, CliTypeIdentity>();
        var baseTypeIds = new Dictionary<int, int>();
        void Add(int typeId, CliTypeIdentity identity)
        {
            if (typeId <= 0)
                throw Invariant($"type '{identity}' has runtime type ID {typeId}");
            if (types.TryGetValue(typeId, out var existing) && !existing.Equals(identity))
                throw Invariant($"runtime type ID {typeId} names both '{existing}' and '{identity}'");
            types[typeId] = identity;
        }

        var exceptionTypeId = 0;
        foreach (var descriptor in descriptors.TypeDescriptors)
        {
            var identity = identities.GetTypeIdentity(descriptor.Type);
            Add(descriptor.TypeId, identity);
            baseTypeIds[descriptor.TypeId] = descriptor.BaseTypeId;
            if (identity.FullName == "System.Exception")
                exceptionTypeId = descriptor.TypeId;
        }
        foreach (var descriptor in descriptors.ConstructedTypeDescriptors)
        {
            Add(descriptor.TypeId, descriptor.Type);
            baseTypeIds[descriptor.TypeId] = descriptor.BaseTypeId;
        }
        foreach (var descriptor in descriptors.ValueTypeDescriptors)
            Add(descriptor.TypeId, descriptor.Type);
        var genericParameterNames = new Dictionary<CliTypeIdentity, string>();
        foreach (var descriptor in descriptors.MetadataTypeDescriptors)
        {
            Add(descriptor.TypeId, descriptor.Type);
            if (descriptor.GenericParameterName is { } name)
                genericParameterNames[descriptor.Type] = name;
        }

        var capacity = types.Count == 0 ? 1 : types.Keys.Max() + 1;
        var symbols = types
            .Select(pair => RuntimeTypeNameFormatter.Format(pair.Key, pair.Value, definitions, genericParameterNames,
                argument => assemblyNames.Format(assemblyOf.Resolve(argument))))
            .Concat(assemblies.Select((assembly, index) =>
            {
                var name = assemblyNames.Format(assembly);
                return new RuntimeTypeSymbol(capacity + index + 1, name, null, null, name);
            }))
            .ToImmutableArray();
        var exceptionTypeIds = exceptionTypeId == 0
            ? ImmutableHashSet<int>.Empty
            : [.. baseTypeIds.Keys.Where(typeId => DerivesFrom(typeId, exceptionTypeId, baseTypeIds))];
        return new(symbols, exceptionTypeIds, Digest(symbols));
    }

    private static bool DerivesFrom(int typeId, int ancestorId, Dictionary<int, int> baseTypeIds)
    {
        var visited = new HashSet<int>();
        while (typeId != 0 && visited.Add(typeId))
        {
            if (typeId == ancestorId)
                return true;
            if (!baseTypeIds.TryGetValue(typeId, out typeId))
                return false;
        }
        return false;
    }

    // SHA-256 over every entry: its ID, then each name's UTF-16 length (-1 for none) and text.
    private static ImmutableArray<byte> Digest(ImmutableArray<RuntimeTypeSymbol> symbols)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var number = new byte[sizeof(int)];
        void Int(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(number, value);
            hash.AppendData(number);
        }
        void Text(string? value)
        {
            Int(value?.Length ?? -1);
            if (value is not null)
                hash.AppendData(Encoding.Unicode.GetBytes(value));
        }
        foreach (var symbol in symbols)
        {
            Int(symbol.TypeId);
            Text(symbol.Name);
            Text(symbol.Namespace);
            Text(symbol.FullName);
            Text(symbol.DisplayName);
        }
        return [.. hash.GetHashAndReset()];
    }

    private static CompilerException Invariant(string message) =>
        new(new CompilerDiagnostic(DiagnosticCode.RuntimeContract, message));
}
