using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class ManagedAssembly : IDisposable
{
    private readonly MemoryStream _stream;
    private readonly byte[] _image;
    private readonly PEReader _peReader;
    private readonly Dictionary<int, TypeDefinitionModel> _types;
    private readonly Dictionary<int, FieldDefinitionModel> _fields;
    private readonly Dictionary<int, MethodDefinitionModel> _methods;
    private readonly Dictionary<int, EntityHandle> _baseTypes;
    private readonly Dictionary<int, ImmutableArray<EntityHandle>> _implementedInterfaces;
    private readonly MetadataAssemblySnapshot _metadata;
    private bool _disposed;
    private string? _contentSha256;
    private static readonly Dictionary<string, int> PrimitiveInitializerSizes =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["primitive:boolean"] = 1,
            ["primitive:i1"] = 1,
            ["primitive:u1"] = 1,
            ["primitive:char"] = 2,
            ["primitive:i2"] = 2,
            ["primitive:u2"] = 2,
            ["primitive:i4"] = 4,
            ["primitive:u4"] = 4,
            ["primitive:r4"] = 4,
            ["primitive:i8"] = 8,
            ["primitive:u8"] = 8,
            ["primitive:r8"] = 8,
        };

    private ManagedAssembly(
            byte[] image,
            AssemblyIdentityAliases assemblyIdentityAliases,
            IValueTypeDefinitionStackKindResolver stackKinds)
    {
        _image = image;
        _stream = new MemoryStream(image, writable: false);
        _peReader = new PEReader(_stream);
        Reader = _peReader.GetMetadataReader();
        var definition = Reader.GetAssemblyDefinition();
        Identity = new AssemblyIdentity(Reader.GetString(definition.Name));
        (_types, _fields, _methods) = ReadDefinitions(assemblyIdentityAliases, stackKinds);
        _baseTypes = _types.ToDictionary(
            pair => pair.Key,
            pair => Reader.GetTypeDefinition(
                (TypeDefinitionHandle)MetadataTokens.Handle(pair.Key)).BaseType);
        _implementedInterfaces = _types.ToDictionary(
            pair => pair.Key,
            pair => Reader.GetTypeDefinition(
                    (TypeDefinitionHandle)MetadataTokens.Handle(pair.Key))
                .GetInterfaceImplementations()
                .Select(handle => Reader.GetInterfaceImplementation(handle).Interface)
                .ToImmutableArray());
        _metadata = new(
            Identity,
            Reader,
            _types,
            _fields,
            _methods,
            _baseTypes,
            _implementedInterfaces)
        {
            AssemblyIdentityAliases = assemblyIdentityAliases,
        };
        References = [
            ..Reader.AssemblyReferences
                .Select(handle => Reader.GetString(Reader.GetAssemblyReference(handle).Name))
                .Order(StringComparer.Ordinal)
        ];
    }

    public AssemblyIdentity Identity { get; }
    public MetadataReader Reader { get; }
    public ImmutableArray<string> References { get; }
    public IReadOnlyDictionary<int, TypeDefinitionModel> Types => _types;
    public IReadOnlyDictionary<int, FieldDefinitionModel> Fields => _fields;
    public IReadOnlyDictionary<int, MethodDefinitionModel> Methods => _methods;
    public string ContentSha256 => _contentSha256 ??=
        Convert.ToHexStringLower(SHA256.HashData(_image));
    internal MetadataAssemblySnapshot Metadata => _metadata;
    internal PEReader PortableExecutableReader
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _peReader;
        }
    }

    internal static ManagedAssembly Parse(
            string path,
            byte[] image,
            AssemblyIdentityAliases assemblyIdentityAliases,
            IValueTypeDefinitionStackKindResolver stackKinds)
    {
        ArgumentNullException.ThrowIfNull(stackKinds);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(image);
        try
        {
            return new ManagedAssembly(image, assemblyIdentityAliases, stackKinds);
        }
        catch (BadImageFormatException exception)
        {
            throw new CompilerException(
                new CompilerDiagnostic(
                    DiagnosticCode.UnsupportedMetadata,
                    $"'{path}' is not a valid managed assembly: {exception.Message}"));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _peReader.Dispose();
        _stream.Dispose();
        _disposed = true;
    }

    private (
        Dictionary<int, TypeDefinitionModel> Types,
        Dictionary<int, FieldDefinitionModel> Fields,
        Dictionary<int, MethodDefinitionModel> Methods) ReadDefinitions(
        AssemblyIdentityAliases assemblyIdentityAliases,
        IValueTypeDefinitionStackKindResolver stackKinds)
    {
        var types = new Dictionary<int, TypeDefinitionModel>();
        var fields = new Dictionary<int, FieldDefinitionModel>();
        var methods = new Dictionary<int, MethodDefinitionModel>();
        var constants = Enumerable.Range(1, Reader.GetTableRowCount(TableIndex.Constant))
            .Select(index => Reader.GetConstant(MetadataTokens.ConstantHandle(index)))
            .ToDictionary(constant => MetadataTokens.GetToken(constant.Parent));
        var signatureProvider = new SignatureTypeProvider(
            Identity,
            Reader,
            assemblyIdentityAliases);

        foreach (var typeHandle in Reader.TypeDefinitions)
        {
            var definition = Reader.GetTypeDefinition(typeHandle);
            var typeToken = MetadataTokens.GetToken(typeHandle);
            var typeKey = new EntityKey(Identity, typeToken);
            var fieldKeys = ImmutableArray.CreateBuilder<EntityKey>();
            var methodKeys = ImmutableArray.CreateBuilder<EntityKey>();

            foreach (var fieldHandle in definition.GetFields())
            {
                var field = Reader.GetFieldDefinition(fieldHandle);
                var fieldToken = MetadataTokens.GetToken(fieldHandle);
                var fieldKey = new EntityKey(Identity, fieldToken);
                fieldKeys.Add(fieldKey);
                var fieldType = field.DecodeSignature(
                    signatureProvider, genericContext: null);
                fields.Add(
                    fieldToken,
                    new FieldDefinitionModel(
                        fieldKey,
                        typeKey,
                        Reader.GetString(field.Name),
                        fieldType,
                        (field.Attributes & FieldAttributes.Static) != 0)
                    {
                        LiteralValue = ReadLiteralValue(field, fieldType, fieldKey),
                        ExplicitOffset = field.GetOffset() is var offset && offset >= 0
                            ? offset
                            : null,
                    });
            }

            foreach (var methodHandle in definition.GetMethods())
            {
                var method = Reader.GetMethodDefinition(methodHandle);
                var signature = method.DecodeSignature(
                    signatureProvider, genericContext: null);
                var methodToken = MetadataTokens.GetToken(methodHandle);
                var methodKey = new EntityKey(Identity, methodToken);
                methodKeys.Add(methodKey);
                var (Import, Export, WitImport, WitExport, WitPostReturn) =
                    ReadInteropDeclarations(method, methodKey);
                methods.Add(
                    methodToken,
                    new MethodDefinitionModel(
                        methodKey,
                        typeKey,
                        Reader.GetString(method.Name),
                        (method.Attributes & MethodAttributes.Static) != 0,
                        new MethodSignatureModel(
                            signature.ReturnType,
                            signature.ParameterTypes),
                        method.RelativeVirtualAddress)
                    {
                        JSImport = Import,
                        JSExport = Export,
                        WitImport = WitImport,
                        WitExport = WitExport,
                        WitPostReturn = WitPostReturn,
                        GenericArity = method.GetGenericParameters().Count,
                        IsVirtual = (method.Attributes & MethodAttributes.Virtual) != 0,
                        IsNewSlot = (method.Attributes & MethodAttributes.NewSlot) != 0,
                        IsFinal = (method.Attributes & MethodAttributes.Final) != 0,
                        IsAbstract = (method.Attributes & MethodAttributes.Abstract) != 0,
                    });
            }

            (var typeNamespace, var typeName) = GetTypeName(typeHandle);
            var layout = definition.GetLayout();
            var isEnum = IsEnumBase(definition.BaseType);
            var enumUnderlyingType = isEnum
                ? fields.Values.Single(field =>
                    field.DeclaringType == typeKey && field.Name == "value__").SignatureType
                : CliTypeIdentity.FromStackKind(stackKinds.Resolve(typeName));
            var enumMembers = isEnum
                ? fields.Values
                    .Where(field => field.DeclaringType == typeKey &&
                                    field.Name != "value__" &&
                                    field.IsStatic)
                    .OrderBy(field => field.Key.MetadataToken)
                    .Select(field => new EnumMemberModel(
                        field.Name,
                        NormalizeEnumRaw(
                            field.LiteralValue ?? throw new BadImageFormatException(
                                $"enum member '{field.Name}' has no metadata constant"),
                            enumUnderlyingType)))
                    .ToImmutableArray()
                : ImmutableArray<EnumMemberModel>.Empty;
            types.Add(
                typeToken,
                new TypeDefinitionModel(
                    typeKey,
                    typeNamespace,
                    typeName,
                    IsValueTypeDefinition(typeName, isEnum, definition.BaseType),
                    fieldKeys.ToImmutable(),
                    methodKeys.ToImmutable())
                {
                    LayoutKind = (definition.Attributes & TypeAttributes.LayoutMask) switch
                    {
                        TypeAttributes.SequentialLayout => CliTypeLayoutKind.Sequential,
                        TypeAttributes.ExplicitLayout => CliTypeLayoutKind.Explicit,
                        _ => CliTypeLayoutKind.Auto,
                    },
                    PackingSize = layout.PackingSize,
                    DeclaredSize = layout.Size,
                    InlineArrayLength = ReadInlineArrayLength(definition, typeKey),
                    GenericArity = definition.GetGenericParameters().Count,
                    GenericParameterVariances = [.. definition.GetGenericParameters()
                        .Select(handle => Reader.GetGenericParameter(handle).Attributes &
                            GenericParameterAttributes.VarianceMask)
                        .Select(attributes => attributes switch
                        {
                            GenericParameterAttributes.Covariant =>
                                CliGenericVariance.Covariant,
                            GenericParameterAttributes.Contravariant =>
                                CliGenericVariance.Contravariant,
                            _ => CliGenericVariance.Invariant,
                        })],
                    IsInterface = (definition.Attributes & TypeAttributes.Interface) != 0,
                    IsAbstract = (definition.Attributes & TypeAttributes.Abstract) != 0,
                    IsSealed = (definition.Attributes & TypeAttributes.Sealed) != 0,
                    IsBeforeFieldInit = (definition.Attributes & TypeAttributes.BeforeFieldInit) != 0,
                    IsEnum = isEnum,
                    EnumUnderlyingType = enumUnderlyingType,
                    EnumMembers = enumMembers,
                    IsFlagsEnum = isEnum && HasFlagsAttribute(definition),
                });
        }

        foreach (var fieldToken in fields.Keys.ToArray())
        {
            var handle = (FieldDefinitionHandle)MetadataTokens.Handle(fieldToken);
            var rva = Reader.GetFieldDefinition(handle).GetRelativeVirtualAddress();
            if (rva == 0)
            {
                continue;
            }
            var field = fields[fieldToken];
            var size = GetInitializerSize(field.SignatureType);
            fields[fieldToken] = field with
            {
                InitialData = _peReader.GetSectionData(rva).GetContent(0, size),
            };
        }

        return (types, fields, methods);

        int GetInitializerSize(CliTypeIdentity signature)
        {
            if (PrimitiveInitializerSizes.TryGetValue(
                    signature.CanonicalName,
                    out var primitiveSize))
            {
                return primitiveSize;
            }
            var fullName = signature.FullName!;
            return types.Values.Single(candidate => candidate.FullName == fullName).DeclaredSize;
        }

        ulong? ReadLiteralValue(
            FieldDefinition field,
            CliTypeIdentity signature,
            EntityKey fieldKey)
        {
            if (!constants.TryGetValue(fieldKey.MetadataToken, out var constant))
            {
                return null;
            }

            var reader = Reader.GetBlobReader(constant.Value);
            ulong? raw;
            try
            {
                raw = constant.TypeCode switch
                {
                    ConstantTypeCode.Boolean or ConstantTypeCode.Byte =>
                        (ulong)reader.ReadByte(),
                    ConstantTypeCode.Char or ConstantTypeCode.UInt16 =>
                        (ulong)reader.ReadUInt16(),
                    ConstantTypeCode.SByte => (ulong)reader.ReadByte(),
                    ConstantTypeCode.Int16 => (ulong)reader.ReadUInt16(),
                    ConstantTypeCode.Int32 => (ulong)reader.ReadUInt32(),
                    ConstantTypeCode.UInt32 => (ulong)reader.ReadUInt32(),
                    ConstantTypeCode.Int64 or ConstantTypeCode.UInt64 =>
                        reader.ReadUInt64(),
                    _ => null,
                };
            }
            catch (BadImageFormatException exception)
            {
                throw new BadImageFormatException(
                    $"field '{fieldKey}' has a truncated metadata constant", exception);
            }
            if (raw is null)
                return null;
            if (reader.RemainingBytes != 0)
            {
                throw new BadImageFormatException(
                    $"field '{fieldKey}' has trailing literal bytes");
            }
            return raw.Value;
        }


        static ulong NormalizeEnumRaw(ulong raw, CliTypeIdentity underlyingType) =>
            underlyingType.CanonicalName switch
            {
                "primitive:i1" or "primitive:u1" => raw & byte.MaxValue,
                "primitive:i2" or "primitive:u2" => raw & ushort.MaxValue,
                "primitive:i4" or "primitive:u4" => raw & uint.MaxValue,
                "primitive:i8" or "primitive:u8" => raw,
                "primitive:char" => raw & ushort.MaxValue,
                _ => throw new BadImageFormatException(
                    $"enum has unsupported underlying type '{underlyingType.CanonicalName}'"),
            };
    }

    private bool HasFlagsAttribute(TypeDefinition definition) =>
        definition.GetCustomAttributes()
            .Select(handle => Reader.GetCustomAttribute(handle))
            .Any(attribute => GetAttributeTypeName(attribute.Constructor) == "System.FlagsAttribute");

    private int ReadInlineArrayLength(
        TypeDefinition definition,
        EntityKey typeKey)
    {
        var length = 0;
        foreach (var attributeHandle in definition.GetCustomAttributes())
        {
            var attribute = Reader.GetCustomAttribute(attributeHandle);
            if (GetAttributeTypeName(attribute.Constructor) !=
                "System.Runtime.CompilerServices.InlineArrayAttribute")
            {
                continue;
            }
            if (length != 0)
            {
                throw new BadImageFormatException(
                    $"type '{typeKey}' has multiple InlineArray attributes");
            }
            var reader = Reader.GetBlobReader(attribute.Value);
            if (reader.ReadUInt16() != 1)
            {
                throw new BadImageFormatException(
                    $"type '{typeKey}' has an invalid InlineArray attribute prolog");
            }
            length = reader.ReadInt32();
            if (length <= 0 || reader.ReadUInt16() != 0 || reader.RemainingBytes != 0)
            {
                throw new BadImageFormatException(
                    $"type '{typeKey}' has an invalid InlineArray length or named arguments");
            }
        }
        return length;
    }

    private (
        InteropImportDeclaration? Import,
        InteropExportDeclaration? Export,
        WitImportDeclaration? WitImport,
        WitExportDeclaration? WitExport,
        WitPostReturnDeclaration? WitPostReturn)
        ReadInteropDeclarations(MethodDefinition method, EntityKey methodKey)
    {
        var import = (InteropImportDeclaration?)null;
        var export = (InteropExportDeclaration?)null;
        var witImport = (WitImportDeclaration?)null;
        var witExport = (WitExportDeclaration?)null;
        var witPostReturn = (WitPostReturnDeclaration?)null;
        foreach (var attributeHandle in method.GetCustomAttributes())
        {
            var attribute = Reader.GetCustomAttribute(attributeHandle);
            var attributeType = GetAttributeTypeName(attribute.Constructor);
            if (attributeType is "System.Runtime.InteropServices.JavaScript.JSImportAttribute" or
                "System.Runtime.InteropServices.JavaScript.JSImportPromiseAttribute")
            {
                if (import is not null)
                {
                    throw InvalidInteropDeclaration(methodKey, "multiple JSImport attributes are not supported");
                }
                import = ReadJSImport(attribute, methodKey) with
                {
                    IsPromise = attributeType.EndsWith(
                        "JSImportPromiseAttribute", StringComparison.Ordinal),
                };
            }
            else if (attributeType == "System.Runtime.InteropServices.JavaScript.JSExportAttribute")
            {
                if (export is not null)
                {
                    throw InvalidInteropDeclaration(methodKey, "multiple JSExport attributes are not supported");
                }
                export = ReadJSExport(attribute, methodKey);
            }
            else if (attributeType == "System.Runtime.InteropServices.WebAssembly.WitImportAttribute")
            {
                if (witImport is not null)
                {
                    throw InvalidInteropDeclaration(methodKey,
                        "multiple WitImport attributes are not supported");
                }
                witImport = ReadWitImport(attribute, methodKey);
            }
            else if (attributeType == "System.Runtime.InteropServices.WebAssembly.WitExportAttribute")
            {
                if (witExport is not null)
                {
                    throw InvalidInteropDeclaration(methodKey,
                        "multiple WitExport attributes are not supported");
                }
                witExport = ReadWitExport(attribute, methodKey);
            }
            else if (attributeType ==
                "System.Runtime.InteropServices.WebAssembly.WitPostReturnAttribute")
            {
                if (witPostReturn is not null)
                {
                    throw InvalidInteropDeclaration(methodKey,
                        "multiple WitPostReturn attributes are not supported");
                }
                var (interfaceName, functionName) = ReadWitNames(
                    attribute, methodKey, "WitPostReturn");
                witPostReturn = new(interfaceName, functionName);
            }
        }
        var declarationCount = (import is null ? 0 : 1) +
            (export is null ? 0 : 1) +
            (witImport is null ? 0 : 1) +
            (witExport is null ? 0 : 1) +
            (witPostReturn is null ? 0 : 1);
        if (declarationCount > 1)
        {
            throw InvalidInteropDeclaration(methodKey,
                "a method cannot declare more than one host/component interop boundary");
        }
        return (import, export, witImport, witExport, witPostReturn);
    }

    private WitImportDeclaration ReadWitImport(
        CustomAttribute attribute,
        EntityKey methodKey)
    {
        var (interfaceName, functionName) = ReadWitNames(
            attribute, methodKey, "WitImport");
        return new WitImportDeclaration(interfaceName, functionName);
    }

    private WitExportDeclaration ReadWitExport(
        CustomAttribute attribute,
        EntityKey methodKey)
    {
        var (interfaceName, functionName) = ReadWitNames(
            attribute, methodKey, "WitExport");
        return new WitExportDeclaration(interfaceName, functionName);
    }

    private (string InterfaceName, string FunctionName) ReadWitNames(
        CustomAttribute attribute,
        EntityKey methodKey,
        string declaration)
    {
        var reader = Reader.GetBlobReader(attribute.Value);
        if (reader.ReadUInt16() != 1)
        {
            throw InvalidInteropDeclaration(methodKey,
                $"{declaration} has an invalid custom-attribute prolog");
        }
        var interfaceName = reader.ReadSerializedString();
        var functionName = reader.ReadSerializedString();
        if (interfaceName is null || string.IsNullOrWhiteSpace(functionName) ||
            reader.ReadUInt16() != 0 || reader.RemainingBytes != 0)
        {
            throw InvalidInteropDeclaration(methodKey,
                $"{declaration} must specify an interface and non-empty function name with no named arguments");
        }
        return (interfaceName, functionName);
    }

    private InteropImportDeclaration ReadJSImport(
        CustomAttribute attribute,
        EntityKey methodKey)
    {
        var reader = Reader.GetBlobReader(attribute.Value);
        if (reader.ReadUInt16() != 1)
        {
            throw InvalidInteropDeclaration(methodKey, "JSImport has an invalid custom-attribute prolog");
        }
        var functionName = reader.ReadSerializedString();
        var moduleName = reader.RemainingBytes == sizeof(ushort)
            ? null
            : reader.ReadSerializedString();
        if (string.IsNullOrWhiteSpace(functionName) || reader.ReadUInt16() != 0 ||
            reader.RemainingBytes != 0)
        {
            throw InvalidInteropDeclaration(methodKey, "JSImport must specify one non-empty function name, an optional module name, and no named arguments");
        }
        return new InteropImportDeclaration(functionName, moduleName);
    }

    private InteropExportDeclaration ReadJSExport(
        CustomAttribute attribute,
        EntityKey methodKey)
    {
        var reader = Reader.GetBlobReader(attribute.Value);
        if (reader.ReadUInt16() != 1)
        {
            throw InvalidInteropDeclaration(methodKey, "JSExport has an invalid custom-attribute prolog");
        }
        var exportName = reader.RemainingBytes == sizeof(ushort)
            ? null
            : reader.ReadSerializedString();
        if (reader.ReadUInt16() != 0 || reader.RemainingBytes != 0 ||
            exportName is not null && string.IsNullOrWhiteSpace(exportName))
        {
            throw InvalidInteropDeclaration(methodKey, "JSExport may specify one non-empty export name and no named arguments");
        }
        return new InteropExportDeclaration(exportName);
    }

    private string? GetAttributeTypeName(EntityHandle constructor)
    {
        if (constructor.Kind == HandleKind.MemberReference)
        {
            return GetAttributeTypeName(
                Reader.GetMemberReference((MemberReferenceHandle)constructor).Parent);
        }
        if (constructor.Kind == HandleKind.TypeReference)
        {
            return GetTypeReferenceName((TypeReferenceHandle)constructor);
        }
        var method = Reader.GetMethodDefinition((MethodDefinitionHandle)constructor);
        return GetTypeDefinitionFullName(method.GetDeclaringType());
    }

    private string GetTypeDefinitionFullName(TypeDefinitionHandle handle)
    {
        var (Namespace, Name) = GetTypeName(handle);
        return string.IsNullOrEmpty(Namespace)
            ? Name
            : $"{Namespace}.{Name}";
    }

    private string GetTypeReferenceName(TypeReferenceHandle handle)
    {
        var reference = Reader.GetTypeReference(handle);
        var @namespace = Reader.GetString(reference.Namespace);
        var name = Reader.GetString(reference.Name);
        return string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
    }

    private static CompilerException InvalidInteropDeclaration(EntityKey methodKey, string message) =>
        new(new CompilerDiagnostic(
            DiagnosticCode.UnsupportedMetadata,
            message,
            methodKey.ToString()));

    private (string Namespace, string Name) GetTypeName(TypeDefinitionHandle handle)
    {
        var definition = Reader.GetTypeDefinition(handle);
        var name = Reader.GetString(definition.Name);
        var declaringType = definition.GetDeclaringType();
        if (declaringType.IsNil)
        {
            return (Reader.GetString(definition.Namespace), name);
        }
        (var typeNamespace, var declaringName) = GetTypeName(declaringType);
        return (typeNamespace, declaringName + "+" + name);
    }

    private bool IsValueTypeBase(EntityHandle baseType)
    {
        if (baseType.IsNil)
        {
            return false;
        }

        (var typeNamespace, var typeName) = baseType.Kind switch
        {
            HandleKind.TypeReference => GetReferencedTypeName((TypeReferenceHandle)baseType),
            HandleKind.TypeDefinition => GetDefinedTypeName((TypeDefinitionHandle)baseType),
            _ => (string.Empty, string.Empty),
        };
        return typeNamespace == "System" &&
               (typeName == "ValueType" || typeName == "Enum");
    }

    private bool IsValueTypeDefinition(
        string typeName,
        bool isEnum,
        EntityHandle baseType) =>
        isEnum ||
        (typeName is not ("ValueType" or "Enum") &&
         IsValueTypeBase(baseType));

    private bool IsEnumBase(EntityHandle baseType)
    {
        if (baseType.IsNil)
        {
            return false;
        }
        (var typeNamespace, var typeName) = baseType.Kind switch
        {
            HandleKind.TypeReference => GetReferencedTypeName((TypeReferenceHandle)baseType),
            HandleKind.TypeDefinition => GetDefinedTypeName((TypeDefinitionHandle)baseType),
            _ => (string.Empty, string.Empty),
        };
        return typeNamespace == "System" && typeName == "Enum";
    }

    private (string Namespace, string Name) GetReferencedTypeName(TypeReferenceHandle handle)
    {
        var reference = Reader.GetTypeReference(handle);
        return (Reader.GetString(reference.Namespace), Reader.GetString(reference.Name));
    }

    private (string Namespace, string Name) GetDefinedTypeName(TypeDefinitionHandle handle)
    {
        var definition = Reader.GetTypeDefinition(handle);
        return (Reader.GetString(definition.Namespace), Reader.GetString(definition.Name));
    }
}
