using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

[Flags]
internal enum RuntimeMemberDescriptorFlags
{
    None = 0,
    Static = 1 << 0,
    Public = 1 << 1,
    InitOnly = 1 << 2,
    Literal = 1 << 3,
    GenericMethod = 1 << 4,
    GenericMethodDefinition = 1 << 5,
    ContainsGenericParameters = 1 << 6,
    ExpressionExecutable = 1 << 7,
}

internal sealed class MemberDescriptorDataBuilder : IMemberDescriptorDataBuilder
{
    private readonly ITypeFinder _typeFinder;
    private readonly ITypeDefinitionResolver _typeDefinitions;
    private readonly IFieldRepository _fieldRepository;
    private readonly ReachableProgram _program;
    private readonly ManagedTypeLayouts _types;
    private readonly WasmTargetLayout _target;
    private readonly ManagedStaticDataBuildState _state;

    internal MemberDescriptorDataBuilder(
        ITypeFinder typeFinder,
        ITypeDefinitionResolver typeDefinitions,
        IFieldRepository fieldRepository,
        ReachableProgram program,
        ManagedTypeLayouts types,
        WasmTargetLayout target,
        ManagedStaticDataBuildState state)
    {
        _typeFinder = typeFinder ?? throw new ArgumentNullException(nameof(typeFinder));
        _typeDefinitions = typeDefinitions ??
            throw new ArgumentNullException(nameof(typeDefinitions));
        _fieldRepository = fieldRepository ??
            throw new ArgumentNullException(nameof(fieldRepository));
        _program = program ?? throw new ArgumentNullException(nameof(program));
        _types = types ?? throw new ArgumentNullException(nameof(types));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public void Build()
    {
        var methods = _program.MethodDescriptors.Values
            .OrderBy(value => value.CanonicalName, StringComparer.Ordinal)
            .Select(method =>
            {
                var typeName = method.Definition.Name is ".ctor" or ".cctor"
                    ? "System.Reflection.RuntimeConstructorInfo"
                    : "System.Reflection.RuntimeMethodInfo";
                return Reserve(
                    method,
                    _typeFinder.FindType(typeName),
                    _state.MethodDescriptors,
                    method.CanonicalName);
            })
            .ToArray();
        var fields = _program.FieldDescriptors.Values
            .OrderBy(value => value.CanonicalName, StringComparer.Ordinal)
            .Select(field => Reserve(
                field,
                _typeFinder.FindType("System.Reflection.RuntimeFieldInfo"),
                _state.FieldDescriptors,
                field.CanonicalName))
            .ToArray();
        var properties = _program.PropertyDescriptors.Values
            .OrderBy(value => value.CanonicalName, StringComparer.Ordinal)
            .Select(property => Reserve(
                property,
                _typeFinder.FindType("System.Reflection.RuntimePropertyInfo"),
                _state.PropertyDescriptors,
                property.CanonicalName))
            .ToArray();
        var propertiesByAccessor = BuildAccessorPropertyIndex(properties);

        var parameterData = new List<DataSegment>();
        var parameterAddresses = methods.ToDictionary(
            reservation => reservation.Value.CanonicalName,
            reservation => ReserveParameterTypes(
                reservation.Value.Signature.ParameterSignatureTypes,
                parameterData),
            StringComparer.Ordinal);

        var segments = new List<DataSegment>(
            methods.Length + fields.Length + properties.Length + parameterData.Count);
        foreach (var reservation in methods)
        {
            segments.Add(new DataSegment(
                reservation.Address,
                [.. BuildMethod(
                    reservation,
                    parameterAddresses,
                    propertiesByAccessor)]));
        }
        foreach (var reservation in fields)
        {
            segments.Add(new DataSegment(
                reservation.Address,
                [.. BuildField(reservation)]));
        }
        foreach (var reservation in properties)
        {
            segments.Add(new DataSegment(
                reservation.Address,
                [.. BuildProperty(reservation)]));
        }
        segments.AddRange(parameterData);
        _state.Segments.AddRange(segments.OrderBy(segment => segment.Address));
    }

    private DescriptorReservation<T> Reserve<T>(
        T value,
        TypeDefinitionModel type,
        Dictionary<string, int> addresses,
        string identity)
    {
        if (!_types.Objects.TryGetValue(type.Key, out var layout))
        {
            throw MissingObjectLayout(type.Key);
        }
        _state.Cursor = ManagedTypeLayoutCompiler.Align(
            _state.Cursor,
            _target.ObjectReferenceAlignment);
        var address = _state.Cursor;
        _state.Cursor += layout.Size;
        addresses.Add(identity, address);
        return new(value, type, layout, address);
    }

    private byte[] BuildMethod(
        DescriptorReservation<MethodInstanceModel> reservation,
        Dictionary<string, int> parameterAddresses,
        Dictionary<string, PropertyInstanceModel> propertiesByAccessor)
    {
        var method = reservation.Value;
        var bytes = CreateObject(reservation.Layout);
        WriteCommon(bytes, reservation.Type, method.DeclaringType);
        WriteInt32(bytes, reservation.Type, "Flags", (int)GetMethodFlags(method));
        WriteAddress(
            bytes,
            reservation.Type,
            "ParameterTypeIds",
            parameterAddresses[method.CanonicalName]);
        WriteInt32(
            bytes,
            reservation.Type,
            "ParameterCount",
            method.Signature.ParameterSignatureTypes.Length);
        WriteName(bytes, reservation.Type, method.CanonicalName, method.Definition.Name);

        if (method.Definition.Name is not (".ctor" or ".cctor"))
        {
            WriteInt32(
                bytes,
                reservation.Type,
                "ReturnTypeId",
                GetTypeLayout(method.Signature.ReturnSignatureType).TypeId);
            if (propertiesByAccessor.TryGetValue(method.CanonicalName, out var property))
            {
                WriteAddress(
                    bytes,
                    reservation.Type,
                    "Property",
                    GetAddress(
                        _state.PropertyDescriptors,
                        "property",
                        property.CanonicalName));
            }
        }
        return bytes;
    }

    private static Dictionary<string, PropertyInstanceModel> BuildAccessorPropertyIndex(
        IEnumerable<DescriptorReservation<PropertyInstanceModel>> properties)
    {
        var result = new Dictionary<string, PropertyInstanceModel>(StringComparer.Ordinal);
        foreach (var reservation in properties)
        {
            AddAccessor(reservation.Value.Getter, reservation.Value, result);
            AddAccessor(reservation.Value.Setter, reservation.Value, result);
        }
        return result;
    }

    private static void AddAccessor(
        MethodInstanceModel? accessor,
        PropertyInstanceModel property,
        Dictionary<string, PropertyInstanceModel> propertiesByAccessor)
    {
        if (accessor is null)
        {
            return;
        }
        if (!propertiesByAccessor.TryAdd(accessor.CanonicalName, property))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"method descriptor '{accessor.CanonicalName}' is associated with multiple property accessors"));
        }
    }

    private byte[] BuildField(DescriptorReservation<FieldInstanceModel> reservation)
    {
        var field = reservation.Value;
        var bytes = CreateObject(reservation.Layout);
        WriteCommon(bytes, reservation.Type, field.DeclaringType);
        var flags = RuntimeMemberDescriptorFlags.None;
        if (field.Definition.IsStatic)
        {
            flags |= RuntimeMemberDescriptorFlags.Static;
        }
        if (field.Definition.IsInitOnly)
        {
            flags |= RuntimeMemberDescriptorFlags.InitOnly;
        }
        if (field.Definition.IsLiteral)
        {
            flags |= RuntimeMemberDescriptorFlags.Literal;
        }
        WriteInt32(bytes, reservation.Type, "Flags", (int)flags);
        WriteInt32(
            bytes,
            reservation.Type,
            "FieldTypeId",
            GetTypeLayout(field.FieldType).TypeId);
        WriteName(bytes, reservation.Type, field.CanonicalName, field.Definition.Name);
        return bytes;
    }

    private byte[] BuildProperty(
        DescriptorReservation<PropertyInstanceModel> reservation)
    {
        var property = reservation.Value;
        var bytes = CreateObject(reservation.Layout);
        WriteInt32(
            bytes,
            reservation.Type,
            "DeclaringTypeId",
            GetTypeLayout(property.DeclaringType).TypeId);
        WriteInt32(
            bytes,
            reservation.Type,
            "PropertyTypeId",
            GetTypeLayout(property.PropertyType).TypeId);
        WriteName(
            bytes,
            reservation.Type,
            property.CanonicalName,
            property.Definition.Name);
        WriteAccessor(bytes, reservation.Type, "Getter", property.Getter);
        WriteAccessor(bytes, reservation.Type, "Setter", property.Setter);
        return bytes;
    }

    private void WriteAccessor(
        byte[] bytes,
        TypeDefinitionModel type,
        string fieldName,
        MethodInstanceModel? accessor)
    {
        if (accessor is null)
        {
            return;
        }
        WriteAddress(
            bytes,
            type,
            fieldName,
            GetAddress(_state.MethodDescriptors, "method", accessor.CanonicalName));
    }

    private void WriteCommon(
        byte[] bytes,
        TypeDefinitionModel type,
        CliTypeIdentity declaringType)
    {
        var declaringTypeIdOffset = GetSharedFieldOffset(
            type,
            "DeclaringTypeId",
            _state.MemberDescriptorDeclaringTypeIdOffset,
            offset => _state.MemberDescriptorDeclaringTypeIdOffset = offset);
        var requiresDeclaringTypeOffset = GetSharedFieldOffset(
            type,
            "RequiresDeclaringType",
            _state.MemberDescriptorRequiresDeclaringTypeOffset,
            offset => _state.MemberDescriptorRequiresDeclaringTypeOffset = offset);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(declaringTypeIdOffset),
            GetTypeLayout(declaringType).TypeId);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(requiresDeclaringTypeOffset),
            _typeDefinitions.ResolveTypeIdentity(declaringType).GenericArity != 0
                ? 1
                : 0);
    }

    private RuntimeMemberDescriptorFlags GetMethodFlags(MethodInstanceModel method)
    {
        var flags = RuntimeMemberDescriptorFlags.None;
        if (method.Definition.IsStatic)
        {
            flags |= RuntimeMemberDescriptorFlags.Static;
        }
        if (method.Definition.IsPublic)
        {
            flags |= RuntimeMemberDescriptorFlags.Public;
        }
        if (method.Definition.GenericArity != 0)
        {
            flags |= RuntimeMemberDescriptorFlags.GenericMethod;
            if (method.MethodArguments.IsEmpty)
            {
                flags |= RuntimeMemberDescriptorFlags.GenericMethodDefinition;
            }
        }
        if (method.DeclaringType.ContainsGenericParameters ||
            method.Signature.ReturnSignatureType.ContainsGenericParameters ||
            method.Signature.ParameterSignatureTypes.Any(type =>
                type.ContainsGenericParameters))
        {
            flags |= RuntimeMemberDescriptorFlags.ContainsGenericParameters;
        }
        if (_program.MemberExecution.Methods.ContainsKey(method.CanonicalName))
        {
            flags |= RuntimeMemberDescriptorFlags.ExpressionExecutable;
        }
        return flags;
    }

    private int ReserveParameterTypes(
        ImmutableArray<CliTypeIdentity> parameters,
        List<DataSegment> segments)
    {
        if (parameters.IsEmpty)
        {
            return 0;
        }
        _state.Cursor = ManagedTypeLayoutCompiler.Align(_state.Cursor, sizeof(int));
        var address = _state.Cursor;
        var bytes = new byte[parameters.Length * sizeof(int)];
        for (var index = 0; index < parameters.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(index * sizeof(int)),
                GetTypeLayout(parameters[index]).TypeId);
        }
        segments.Add(new DataSegment(address, [.. bytes]));
        _state.Cursor += bytes.Length;
        return address;
    }

    private static byte[] CreateObject(ObjectLayout layout)
    {
        var bytes = new byte[layout.Size];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, layout.TypeId);
        return bytes;
    }

    private void WriteName(
        byte[] bytes,
        TypeDefinitionModel type,
        string identity,
        string name)
    {
        if (!_program.NamedMemberDescriptors.Contains(identity))
        {
            return;
        }
        var address = _state.Strings.TryGetValue(name, out var layout)
            ? layout.Address
            : throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"member name '{name}' was not assigned static storage"));
        WriteAddress(bytes, type, "MetadataName", address);
    }

    private void WriteInt32(
        byte[] bytes,
        TypeDefinitionModel type,
        string fieldName,
        int value) => BinaryPrimitives.WriteInt32LittleEndian(
        bytes.AsSpan(GetFieldOffset(type, fieldName)),
        value);

    private void WriteAddress(
        byte[] bytes,
        TypeDefinitionModel type,
        string fieldName,
        int value)
    {
        var offset = GetFieldOffset(type, fieldName);
        if (_target.AddressSize == sizeof(int))
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        }
        else
        {
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset), value);
        }
    }

    private int GetSharedFieldOffset(
        TypeDefinitionModel descriptorType,
        string fieldName,
        int? existing,
        Action<int> publish)
    {
        var offset = GetFieldOffset(descriptorType, fieldName);
        if (existing is int sharedOffset && sharedOffset != offset)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"member descriptor '{fieldName}' fields do not share one layout"));
        }
        publish(offset);
        return offset;
    }

    private int GetFieldOffset(TypeDefinitionModel descriptorType, string fieldName)
    {
        var fields = descriptorType.Fields
            .Select(_fieldRepository.GetField)
            .Where(candidate => candidate.Name == fieldName)
            .Take(2)
            .ToArray();
        if (fields.Length != 1)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"descriptor type '{descriptorType.Key}' must define exactly one " +
                $"'{fieldName}' field"));
        }
        var field = fields[0];
        return _types.ValueLayoutState.Fields.TryGetValue(field.Key, out var layout)
            ? layout.Offset
            : throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"field layout for '{field.Key}' was not generated"));
    }

    private ObjectLayout GetTypeLayout(CliTypeIdentity type) =>
        _types.ConstructedObjects.TryGetValue(type, out var constructed)
            ? constructed
            : _types.ObjectIdentities.TryGetValue(type, out var named)
                ? named
                : throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.RuntimeContract,
                    $"object layout for descriptor type '{type.CanonicalName}' was not generated"));

    private static int GetAddress(
        Dictionary<string, int> addresses,
        string kind,
        string identity) => addresses.TryGetValue(identity, out var address)
        ? address
        : throw new CompilerException(new CompilerDiagnostic(
            DiagnosticCode.RuntimeContract,
            $"{kind} descriptor '{identity}' was not assigned static storage"));

    private static CompilerException MissingObjectLayout(EntityKey type) => new(
        new CompilerDiagnostic(
            DiagnosticCode.RuntimeContract,
            $"object layout for '{type}' was not generated"));

    private readonly record struct DescriptorReservation<T>(
        T Value,
        TypeDefinitionModel Type,
        ObjectLayout Layout,
        int Address);
}
