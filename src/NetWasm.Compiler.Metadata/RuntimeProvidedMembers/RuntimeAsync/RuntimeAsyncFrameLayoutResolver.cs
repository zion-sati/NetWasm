// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// Instantiates CoreLib's RuntimeAsyncFrame and RuntimeAsyncSlots for a lowered method, resolved once per compilation
// and only when a runtime-async method is lowered. RuntimeAsyncSlots holds seven slots and nests the rest in its Rest.
internal sealed class RuntimeAsyncFrameLayoutResolver(
    ITypeFinderFactory types,
    MetadataCompilationSnapshot snapshot,
    IFieldRepository fields,
    ITypeDefinitionResolver definitions) : IRuntimeAsyncFrameLayoutResolver
{
    private const int SlotsPerStruct = 7;

    private readonly Lazy<ITypeFinder> _types = new(() => types.Create(snapshot));

    public RuntimeAsyncFrameLayout Resolve(ImmutableArray<CliTypeIdentity> slots, ImmutableArray<CliTypeIdentity> parameters,
        CliTypeIdentity returnType)
    {
        var frame = Instantiate("System.Runtime.CompilerServices.RuntimeAsyncFrame", [Slots(slots), .. parameters, returnType]);
        var root = Field(frame, "Slots");
        return new(frame, [.. Enumerable.Range(0, slots.Length).Select(index => Path(root, index, slots.Length))]);
    }

    // RuntimeAsyncSlots over these types: seven to a struct, the rest nested in its Rest.
    private CliTypeIdentity Slots(ImmutableArray<CliTypeIdentity> types) =>
        Instantiate("System.Runtime.CompilerServices.RuntimeAsyncSlots", types.Length <= SlotsPerStruct
            ? types
            : [.. types[..SlotsPerStruct], Slots(types[SlotsPerStruct..])]);

    // The fields from the frame to slot index of count: Slots, a Rest for each seven before it, and its Item.
    private ImmutableArray<FieldInstanceModel> Path(FieldInstanceModel root, int index, int count)
    {
        var path = ImmutableArray.CreateBuilder<FieldInstanceModel>();
        path.Add(root);
        while (count > SlotsPerStruct && index >= SlotsPerStruct)
        {
            path.Add(Field(path[^1].FieldType, "Rest"));
            index -= SlotsPerStruct;
            count -= SlotsPerStruct;
        }
        path.Add(Field(path[^1].FieldType, $"Item{index + 1}"));
        return path.ToImmutable();
    }

    // An instance field of a frame or of the slots type that holds a slot, each an instantiation made here: a frame has
    // parameters and a result, and a slots type that holds a slot has its type.
    private FieldInstanceModel Field(CliTypeIdentity owner, string name)
    {
        var field = definitions.ResolveTypeIdentity(owner).Fields.Select(fields.GetField)
            .Single(field => field.Name == name && !field.IsStatic);
        return new(field, owner, field.SignatureType.Substitute(owner.TypeArguments));
    }

    private CliTypeIdentity Instantiate(string name, ImmutableArray<CliTypeIdentity> typeArguments) =>
        typeArguments.IsDefaultOrEmpty
            ? CliTypeIdentity.FromDefinition(_types.Value.FindType(name))
            : CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.FromDefinition(_types.Value.FindType($"{name}`{typeArguments.Length}")), typeArguments);
}
