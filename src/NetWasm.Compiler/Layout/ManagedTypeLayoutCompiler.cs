using System;
using System.Collections.Immutable;
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
        return new ManagedTypeLayouts(
            _state.Objects.ToImmutableDictionary(),
            _state.ObjectIdentities.ToImmutableDictionary(),
            _state.ObjectsByName.ToImmutableDictionary(StringComparer.Ordinal),
            _state.ConstructedObjects.ToImmutableDictionary(),
            _state.ValueLayouts,
            _target,
            offsets.Target,
            offsets.MethodId,
            offsets.Left,
            offsets.Right);
    }

    internal static int Align(int value, int alignment) =>
        ManagedObjectLayoutBuilder.Align(value, alignment);
}
