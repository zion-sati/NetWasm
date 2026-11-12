using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>What a recorded query asked; its key names the argument stably across compiles.</summary>
internal enum UnitQueryKind
{
    ObjectLayout,
    ObjectLayoutLookup,
    ObjectLayoutOfDefinition,
    StringTypeId,
    TypeTypeId,
    ValueLayout,
    InstanceField,
    InstanceFieldOfDefinition,
    StaticField,
    StaticFieldOfDefinition,
    StringLayout,
    StaticDataEnd,
    StaticRootAddresses,
    DataSegments,
    TypeTable,
    TypeObjectAddress,
    TypeObjectTable,
    MethodDescriptorAddress,
    FieldDescriptorAddress,
    MemberDescriptorOffsets,
    ExceptionObject,
    TypeDescriptors,
    ConstructedTypeDescriptors,
    ValueTypeDescriptors,
    MetadataTypeDescriptors,
    ManagedCallSite,
    ByRefEffects,
    DispatchCallSite,
    TypeTestSite,
    RootMap,
    EnumMetadata,
    StaticInitializerGuard,
    ExceptionMetadata,
    StackTraceDirectMethodId,
    StackTraceConstructedMethodId,
    StackTraceLocationSymbols,
    TargetPlans,
    FunctionAddress,
    CallableMethods,
    Finalizable,
    DelegateTypes,
    // Root-map analysis: whether a callee may allocate.
    AllocatingMethod,
    AllocatingConstructedMethod,
}

/// <summary>
/// One query a unit's emission made, a fingerprint of the answer it got and the answer
/// itself: the value the provider returned, or, for a unit read back from a store, the
/// value as the store encoded it (<see cref="UnitEncodedValue"/>).
/// </summary>
internal readonly record struct UnitDependency(UnitQueryKind Kind, string Key, UnitFingerprint Answer)
{
    public object? Value { get; init; }
}

/// <summary>An answer as a store encoded it.</summary>
internal sealed record UnitEncodedValue(byte[] Bytes);

/// <summary>A keyed lookup's answer: whether the key was found, and what it found.</summary>
internal readonly record struct UnitLookup<T>(bool Found, T? Value);

internal interface IUnitDependencyRecorder
{
    void Record(UnitQueryKind kind, string key, UnitFingerprint answer, object? value = null);
}

/// <summary>
/// The queries one worker's current method makes. Confined to the worker's thread: a
/// method's emission runs start to finish on it, so no synchronization is needed.
/// </summary>
internal sealed class UnitDependencyLog : IUnitDependencyRecorder
{
    // One table, cleared for each method, so its capacity is grown once per worker.
    private readonly Dictionary<(UnitQueryKind Kind, string Key), (UnitFingerprint Answer, object? Value)> _table = [];
    private Dictionary<(UnitQueryKind Kind, string Key), (UnitFingerprint Answer, object? Value)>? _answers;

    internal void Begin()
    {
        _table.Clear();
        _answers = _table;
    }

    internal ImmutableArray<UnitDependency> End()
    {
        var answers = _answers ?? throw new InvalidOperationException("No method is being recorded.");
        _answers = null;
        var dependencies = ImmutableArray.CreateBuilder<UnitDependency>(answers.Count);
        foreach (var ((kind, key), (answer, value)) in answers)
            dependencies.Add(new(kind, key, answer) { Value = value });
        return Answers = dependencies.MoveToImmutable();
    }

    // What the last recorded method asked, kept after End.
    internal ImmutableArray<UnitDependency> Answers { get; private set; } = [];

    public void Record(UnitQueryKind kind, string key, UnitFingerprint answer, object? value = null)
    {
        // Outside a recorded method (planning, generated helpers) queries are not unit inputs.
        if (_answers is null) return;
        if (!_answers.TryAdd((kind, key), (answer, value)) && _answers[(kind, key)].Answer != answer)
            throw new InvalidOperationException(
                $"Query {kind} '{key}' answered differently within one method.");
    }
}
