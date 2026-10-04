using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedStaticDataBuilder : IManagedStaticDataBuilder
{
    private readonly ManagedTypeLayouts _types;
    private readonly ManagedStaticDataBuildState _state;
    private readonly IStaticFieldStorageBuilder _staticFields;
    private readonly ITypeDescriptorBuilder _typeDescriptors;
    private readonly IConstructedTypeDescriptorBuilder _constructedTypeDescriptors;
    private readonly IValueTypeDescriptorBuilder _valueTypeDescriptors;
    private readonly IRuntimeGenericArgumentMetadataBuilder _genericArguments;
    private readonly IMemberDescriptorDataBuilder _memberDescriptors;
    private readonly IStringDataBuilder _strings;
    private readonly IExceptionObjectBuilder _exceptionObjects;
    private readonly IEnumMetadataCollector _enumMetadataCollector;
    private readonly IEnumMetadataBuilder _enumMetadata;
    private readonly WasmTargetLayout _target;

    internal ManagedStaticDataBuilder(
        ManagedTypeLayouts types,
        ManagedStaticDataBuildState state,
        IStaticFieldStorageBuilder staticFields,
        ITypeDescriptorBuilder typeDescriptors,
        IConstructedTypeDescriptorBuilder constructedTypeDescriptors,
        IValueTypeDescriptorBuilder valueTypeDescriptors,
        IMemberDescriptorDataBuilder memberDescriptors,
        IStringDataBuilder strings,
        IExceptionObjectBuilder exceptionObjects,
        IEnumMetadataCollector enumMetadataCollector,
        IEnumMetadataBuilder enumMetadata) :
        this(
            types,
            state,
            staticFields,
            typeDescriptors,
            constructedTypeDescriptors,
            valueTypeDescriptors,
            EmptyRuntimeGenericArgumentMetadataBuilder.Instance,
            memberDescriptors,
            strings,
            exceptionObjects,
            enumMetadataCollector,
            enumMetadata)
    {
    }

    internal ManagedStaticDataBuilder(
        ManagedTypeLayouts types,
        ManagedStaticDataBuildState state,
        IStaticFieldStorageBuilder staticFields,
        ITypeDescriptorBuilder typeDescriptors,
        IConstructedTypeDescriptorBuilder constructedTypeDescriptors,
        IValueTypeDescriptorBuilder valueTypeDescriptors,
        IRuntimeGenericArgumentMetadataBuilder genericArguments,
        IMemberDescriptorDataBuilder memberDescriptors,
        IStringDataBuilder strings,
        IExceptionObjectBuilder exceptionObjects,
        IEnumMetadataCollector enumMetadataCollector,
        IEnumMetadataBuilder enumMetadata)
    {
        _types = types ?? throw new ArgumentNullException(nameof(types));
        _target = types.Target ?? throw new ArgumentNullException(nameof(types));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _staticFields = staticFields ?? throw new ArgumentNullException(nameof(staticFields));
        _typeDescriptors = typeDescriptors ??
            throw new ArgumentNullException(nameof(typeDescriptors));
        _constructedTypeDescriptors = constructedTypeDescriptors ??
            throw new ArgumentNullException(nameof(constructedTypeDescriptors));
        _valueTypeDescriptors = valueTypeDescriptors ??
            throw new ArgumentNullException(nameof(valueTypeDescriptors));
        _genericArguments = genericArguments ??
            throw new ArgumentNullException(nameof(genericArguments));
        _memberDescriptors = memberDescriptors ??
            throw new ArgumentNullException(nameof(memberDescriptors));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        _exceptionObjects = exceptionObjects ??
            throw new ArgumentNullException(nameof(exceptionObjects));
        _enumMetadataCollector = enumMetadataCollector ??
            throw new ArgumentNullException(nameof(enumMetadataCollector));
        _enumMetadata = enumMetadata ?? throw new ArgumentNullException(nameof(enumMetadata));
    }

    public ManagedStaticData Build()
    {
        _staticFields.Build();
        _typeDescriptors.Build();
        _constructedTypeDescriptors.Build();
        _valueTypeDescriptors.Build();
        _genericArguments.Build();
        _enumMetadataCollector.Collect();
        _strings.Build();
        _memberDescriptors.Build();
        BuildTypeFacts();
        _enumMetadata.Build();
        _exceptionObjects.Build();

        return new ManagedStaticData(
            ManagedTypeLayoutCompiler.Align(
                _state.Cursor,
                _target.ObjectReferenceAlignment),
            _state.StaticFields.ToImmutableDictionary(),
            _state.ConstructedStaticFields.ToImmutableDictionary(StringComparer.Ordinal),
            _state.Strings.ToImmutableDictionary(StringComparer.Ordinal),
            _state.ExceptionObjects.ToImmutableDictionary(),
            _state.Segments.ToImmutable(),
            _state.TypeDescriptors.ToImmutable(),
            _state.ConstructedTypeDescriptors.ToImmutable(),
            _state.ValueTypeDescriptors.ToImmutable(),
            [.. _state.StaticRoots.Order()])
        {
            MetadataTypeDescriptors = _state.MetadataTypeDescriptors.ToImmutable(),
            EnumMetadata = _state.EnumMetadata.ToImmutable(),
            MethodDescriptors = _state.MethodDescriptors.ToImmutableDictionary(
                StringComparer.Ordinal),
            FieldDescriptors = _state.FieldDescriptors.ToImmutableDictionary(
                StringComparer.Ordinal),
            PropertyDescriptors = _state.PropertyDescriptors.ToImmutableDictionary(
                StringComparer.Ordinal),
            MemberDescriptorDeclaringTypeIdOffset =
                _state.MemberDescriptorDeclaringTypeIdOffset ?? 0,
            MemberDescriptorRequiresDeclaringTypeOffset =
                _state.MemberDescriptorRequiresDeclaringTypeOffset ?? 0,
            TypeFactsTableAddress = _state.TypeFactsTableAddress,
            TypeFactsTableCount = _state.TypeFactsTableCount,
        };
    }

    private void BuildTypeFacts()
    {
        foreach (var facts in _state.PendingTypeFacts.OrderBy(facts => facts.TypeId))
        {
            var names = facts.Names is null
                ? 0
                : RuntimeTypeFactsEncoding.AddNames(_state, _target, facts.Names);
            var genericArgumentTypeIds = AddGenericArgumentTypeIds(
                facts.GenericArgumentTypeIds);
            RuntimeTypeFactsEncoding.Add(
                _state,
                _target,
                facts.Identity,
                facts.Definition,
                facts.TypeId,
                facts.BaseTypeId,
                facts.AssignableTypeIdsAddress,
                facts.AssignableTypeIdCount,
                GetDelegateInvokeAddress(facts),
                names,
                genericArgumentTypeIds,
                facts.GenericArgumentTypeIds.Length);
        }
        BuildTypeFactsTable();
    }

    private int AddGenericArgumentTypeIds(ImmutableArray<int> typeIds)
    {
        if (typeIds.IsEmpty)
        {
            return 0;
        }

        _state.Cursor = ManagedTypeLayoutCompiler.Align(_state.Cursor, sizeof(int));
        var address = _state.Cursor;
        var bytes = new byte[checked(typeIds.Length * sizeof(int))];
        for (int index = 0; index < typeIds.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(index * sizeof(int)),
                typeIds[index]);
        }
        _state.Segments.Add(new DataSegment(address, [.. bytes]));
        _state.Cursor += bytes.Length;
        return address;
    }

    private int GetDelegateInvokeAddress(PendingRuntimeTypeFacts facts)
    {
        if (facts.DelegateInvokeDescriptor is null)
        {
            return 0;
        }
        return _state.MethodDescriptors.TryGetValue(
            facts.DelegateInvokeDescriptor,
            out var address)
                ? address
                : throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.RuntimeContract,
                    $"delegate Invoke descriptor '{facts.DelegateInvokeDescriptor}' " +
                    "was not assigned static storage"));
    }

    private void BuildTypeFactsTable()
    {
        if (_state.TypeFacts.Count == 0)
        {
            return;
        }
        var descriptorTypeIds = _state.TypeDescriptors
            .Select(descriptor => descriptor.TypeId)
            .Concat(_state.ConstructedTypeDescriptors.Select(descriptor =>
                descriptor.TypeId))
            .Concat(_state.MetadataTypeDescriptors.Select(descriptor =>
                descriptor.TypeId))
            .ToHashSet();
        if (!descriptorTypeIds.SetEquals(_state.TypeFacts.Keys))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                "type-facts records do not cover every reachable semantic type"));
        }
        var count = checked(_state.TypeFacts.Keys.Max() + 1);
        _state.Cursor = ManagedTypeLayoutCompiler.Align(
            _state.Cursor,
            _target.AddressSize);
        var address = _state.Cursor;
        var bytes = new byte[checked(count * _target.AddressSize)];
        foreach (var (typeId, factsAddress) in _state.TypeFacts)
        {
            var destination = bytes.AsSpan(typeId * _target.AddressSize);
            if (_target.AddressSize == sizeof(int))
            {
                BinaryPrimitives.WriteInt32LittleEndian(destination, factsAddress);
            }
            else
            {
                BinaryPrimitives.WriteInt64LittleEndian(destination, factsAddress);
            }
        }
        _state.Segments.Add(new DataSegment(address, [.. bytes]));
        _state.Cursor += bytes.Length;
        _state.TypeFactsTableAddress = address;
        _state.TypeFactsTableCount = count;
    }
}
