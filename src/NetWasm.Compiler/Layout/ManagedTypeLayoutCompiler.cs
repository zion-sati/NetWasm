using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedTypeLayoutCompiler : IManagedTypeLayoutCompiler
{
    private readonly ManagedTypeLayoutBuildState _state;
    private readonly WasmTargetLayout _target;
    private readonly IReachableObjectLayoutBuilder _reachableObjects;
    private readonly IStaticFieldValueLayoutResolver _staticFieldValues;
    private readonly IMethodValueLayoutResolver _methodValues;
    private readonly IImplicitObjectLayoutBuilder _implicitObjects;
    private readonly IValueTypeLayoutResolver _valueTypes;
    private readonly IDelegateFieldOffsetResolver _delegateOffsets;

    internal ManagedTypeLayoutCompiler(
        ManagedTypeLayoutBuildState state,
        WasmTargetLayout target,
        IReachableObjectLayoutBuilder reachableObjects,
        IStaticFieldValueLayoutResolver staticFieldValues,
        IMethodValueLayoutResolver methodValues,
        IImplicitObjectLayoutBuilder implicitObjects,
        IValueTypeLayoutResolver valueTypes,
        IDelegateFieldOffsetResolver delegateOffsets)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _reachableObjects = reachableObjects ??
            throw new ArgumentNullException(nameof(reachableObjects));
        _staticFieldValues = staticFieldValues ??
            throw new ArgumentNullException(nameof(staticFieldValues));
        _methodValues = methodValues ??
            throw new ArgumentNullException(nameof(methodValues));
        _implicitObjects = implicitObjects ??
            throw new ArgumentNullException(nameof(implicitObjects));
        _valueTypes = valueTypes ?? throw new ArgumentNullException(nameof(valueTypes));
        _delegateOffsets = delegateOffsets ??
            throw new ArgumentNullException(nameof(delegateOffsets));
    }

    public ManagedTypeLayouts Compile()
    {
        _reachableObjects.Build();
        _staticFieldValues.Resolve();
        _methodValues.Resolve();
        _implicitObjects.Build();
        _valueTypes.Resolve();
        var offsets = _delegateOffsets.Resolve();
        NumberInPreorder();
        return new ManagedTypeLayouts(
            _state.Objects.ToImmutableDictionary(),
            _state.ObjectIdentities.ToImmutableDictionary(),
            _state.ObjectsByName.ToImmutableDictionary(StringComparer.Ordinal),
            _state.ConstructedObjects.ToImmutableDictionary(),
            _state.ValueLayouts,
            _target,
            offsets.Target,
            offsets.MethodId,
            offsets.InvocationList,
            offsets.InvocationCount);
    }

    // Every layout exists now and nothing has read an id yet, so the ids the
    // builders handed out in discovery order become preorder numbers.
    private void NumberInPreorder()
    {
        var numbers = TypeIdPreorder.Number(_state.Objects.Values
            .Concat(_state.ConstructedObjects.Values)
            .Select(layout => layout.TypeId)
            .Concat(_state.BaseTypeIds.Keys)
            .Distinct()
            .Select(typeId => (typeId, _state.BaseTypeIds.GetValueOrDefault(typeId))));
        ObjectLayout Renumber(ObjectLayout layout) =>
            layout with { TypeId = numbers[layout.TypeId].TypeId, LastSubtypeId = numbers[layout.TypeId].LastSubtypeId };
        RenumberAll(_state.Objects, Renumber);
        RenumberAll(_state.ObjectIdentities, Renumber);
        RenumberAll(_state.ObjectsByName, Renumber);
        RenumberAll(_state.ConstructedObjects, Renumber);
        var bases = _state.BaseTypeIds.ToArray();
        _state.BaseTypeIds.Clear();
        foreach (var (typeId, baseTypeId) in bases)
        {
            _state.BaseTypeIds.Add(numbers[typeId].TypeId, baseTypeId == 0 ? 0 : numbers[baseTypeId].TypeId);
        }
    }

    private static void RenumberAll<TKey>(Dictionary<TKey, ObjectLayout> layouts, Func<ObjectLayout, ObjectLayout> renumber)
        where TKey : notnull
    {
        foreach (var key in layouts.Keys.ToArray())
        {
            layouts[key] = renumber(layouts[key]);
        }
    }

    internal static int Align(int value, int alignment) =>
        ManagedObjectLayoutBuilder.Align(value, alignment);
}
