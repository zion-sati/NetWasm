// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.CompositeFormats;

internal interface IToStringOnlyTypeClassifier
{
    // Whether string.Format writes every value of the type as its ToString(): a class that is not IFormattable and
    // that no type in the program derived from it makes IFormattable.
    bool IsToStringOnly(CliTypeIdentity type);
}

// The first class that is neither sealed nor a string indexes the program once: every type that is IFormattable and
// each class it derives from. A class is then ToString-only when it is not in that index: linear in the program's
// types, once per compile, and a lookup for each class after.
internal sealed class ToStringOnlyTypeClassifier(
    ImmutableArray<TypeDefinitionModel> program,
    ITypeDefinitionResolver typeDefinitions,
    ITypeIdentityResolver identities,
    IBaseTypeResolver baseTypes,
    IImplementedInterfaceResolver interfaces) : IToStringOnlyTypeClassifier
{
    private readonly ConcurrentDictionary<CliTypeIdentity, bool> _types = new();
    private HashSet<CliTypeIdentity>? _formattableOrBase;

    public bool IsToStringOnly(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _types.GetOrAdd(type, Classify);
    }

    private bool Classify(CliTypeIdentity type)
    {
        if (type.CanonicalName == "primitive:string")
        {
            return true;
        }
        if (type.IsValueType || type.Shape != CliTypeShape.Named || type.ContainsGenericParameters ||
            type.CanonicalName == "primitive:object")
        {
            return false;
        }
        var definition = typeDefinitions.ResolveTypeIdentity(type);
        if (definition.IsInterface || IsFormattable(type))
        {
            return false;
        }
        return definition.IsSealed || !FormattableOrBase().Contains(type);
    }

    // Every IFormattable type of the program, generic definitions included, and every class each derives from.
    private HashSet<CliTypeIdentity> FormattableOrBase()
    {
        if (Volatile.Read(ref _formattableOrBase) is { } built)
        {
            return built;
        }
        var index = new HashSet<CliTypeIdentity>();
        foreach (var candidate in program)
        {
            var identity = identities.GetTypeIdentity(candidate.Key);
            if (!IsFormattable(identity))
            {
                continue;
            }
            for (var current = identity; current is not null && index.Add(current); current = baseTypes.Resolve(current))
            {
            }
        }
        return Interlocked.CompareExchange(ref _formattableOrBase, index, null) ?? index;
    }

    private bool IsFormattable(CliTypeIdentity type) =>
        interfaces.GetInterfaces(type).Any(static implemented => implemented.FullName == "System.IFormattable");
}
