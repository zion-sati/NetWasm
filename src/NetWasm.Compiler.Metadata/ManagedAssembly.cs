// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Metadata.UnsafeAccessors;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeImports;

namespace NetWasm.Compiler.Metadata;

public sealed class ManagedAssembly : IDisposable
{
    // MethodImplAttributes.Async, which .NET 11's System.Reflection.Metadata names and .NET 10's does not.
    private const MethodImplAttributes RuntimeAsyncImplementation = (MethodImplAttributes)0x2000;

    private readonly PEReader _peReader;
    private readonly byte[] _image;
    private readonly Dictionary<int, TypeDefinitionModel> _types;
    private readonly Dictionary<int, FieldDefinitionModel> _fields;
    private readonly Dictionary<int, MethodDefinitionModel> _methods;
    private readonly Dictionary<int, PropertyDefinitionModel> _properties;
    private readonly Dictionary<int, EntityHandle> _baseTypes;
    private readonly Dictionary<int, ImmutableArray<EntityHandle>> _implementedInterfaces;
    private readonly MetadataAssemblySnapshot _metadata;
    private readonly GeneratedJavaScriptBinding _javaScriptBinding;
    // This assembly's value types, as runtime marshalling lays them out natively; and, when it keeps runtime marshalling,
    // the P/Invokes and unmanaged calli signatures whose struct positions name a value type of another assembly at any
    // depth, which are laid out once the compilation has read every assembly (LayCrossAssemblyStructStubs).
    private readonly AssemblyStructSource _structs;
    private readonly List<int> _crossAssemblyStructMethods = [];
    private readonly List<int> _crossAssemblyCalliSignatures = [];
    private bool _disposed;
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
            string path,
            byte[] image,
            AssemblyIdentityAliases assemblyIdentityAliases,
            IValueTypeDefinitionStackKindResolver stackKinds,
            INativeImportDeclarationReader nativeImports,
            INativeCallbackDeclarationReader nativeCallbacks,
            IUnsafeAccessorDeclarationReader unsafeAccessors,
            INativeImportBinder nativeBinding,
            INativeVariadicCallPlanner nativeVariadics,
            IFeatureSwitchSubstitutionReader substitutions,
            GeneratedJavaScriptBinding javaScriptBinding)
    {
        Path = path;
        _javaScriptBinding = javaScriptBinding;
        _image = image;
        // Over the image itself, so concurrent method body reads take no lock.
        _peReader = new PEReader(ImmutableCollectionsMarshal.AsImmutableArray(image));
        Reader = _peReader.GetMetadataReader();
        var definition = Reader.GetAssemblyDefinition();
        Identity = new AssemblyIdentity(Reader.GetString(definition.Name));
        Culture = Reader.GetString(definition.Culture);
        (_types, _fields, _methods, _properties, _structs) = ReadDefinitions(
            assemblyIdentityAliases,
            stackKinds,
            nativeImports,
            nativeCallbacks,
            unsafeAccessors,
            nativeBinding, nativeVariadics);
        foreach (var (token, stubs) in substitutions.Read(_peReader, Reader))
        {
            if (_methods.TryGetValue(token, out var method))
                _methods[token] = method with { FeatureSwitchStubs = stubs };
        }
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
            Properties = _properties,
        };
        References = [
            ..Reader.AssemblyReferences
                .Select(handle => Reader.GetString(Reader.GetAssemblyReference(handle).Name))
                .Order(StringComparer.Ordinal)
        ];
    }

    public AssemblyIdentity Identity { get; }

    // The assembly's culture: empty for a neutral assembly; a satellite resource assembly names the culture its
    // resources are for (cs-CZ/App.resources.dll).
    public string Culture { get; }
    public string Path { get; }
    public MetadataReader Reader { get; }
    public ImmutableArray<string> References { get; }
    public IReadOnlyDictionary<int, TypeDefinitionModel> Types => _types;
    public IReadOnlyDictionary<int, FieldDefinitionModel> Fields => _fields;
    public IReadOnlyDictionary<int, MethodDefinitionModel> Methods => _methods;
    public IReadOnlyDictionary<int, PropertyDefinitionModel> Properties => _properties;
    internal MetadataAssemblySnapshot Metadata => _metadata;
    // The assembly file's bytes, as loaded.
    internal ReadOnlySpan<byte> Image => _image;
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
            IValueTypeDefinitionStackKindResolver stackKinds,
            INativeImportDeclarationReader nativeImports) =>
        Parse(
            path,
            image,
            assemblyIdentityAliases,
            stackKinds,
            nativeImports,
            new NativeCallbackDeclarationReader());

    internal static ManagedAssembly Parse(
            string path,
            byte[] image,
            AssemblyIdentityAliases assemblyIdentityAliases,
            IValueTypeDefinitionStackKindResolver stackKinds,
            INativeImportDeclarationReader nativeImports,
            INativeCallbackDeclarationReader nativeCallbacks,
            INativeImportBinder? nativeBinding = null,
        INativeVariadicCallPlanner? nativeVariadics = null,
        GeneratedJavaScriptBinding? javaScriptBinding = null)
    {
        ArgumentNullException.ThrowIfNull(stackKinds);
        ArgumentNullException.ThrowIfNull(nativeImports);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(image);
        try
        {
            return new ManagedAssembly(
                path,
                image,
                assemblyIdentityAliases,
                stackKinds,
                nativeImports,
                nativeCallbacks,
                new UnsafeAccessorDeclarationReader(new CustomAttributeTypeNameProvider()),
                nativeBinding ?? NativeLibraryBinding.All,
                nativeVariadics ?? NoNativeVariadicCalls.Instance,
                SubstitutionReader(),
                javaScriptBinding ?? GeneratedJavaScriptBinding.OrdinaryCode);
        }
        catch (BadImageFormatException exception)
        {
            throw new CompilerException(
                new CompilerDiagnostic(
                    DiagnosticCode.UnsupportedMetadata,
                    $"'{path}' is not a valid managed assembly: {exception.Message}"));
        }
    }

    // The substitution reader, its type and signature names given by one naming Strategy.
    private static FeatureSwitchSubstitutionReader SubstitutionReader()
    {
        var names = new SubstitutionSignatureTypeNameProvider();
        return new(new EmbeddedResourceReader(), new TypeDefinitionNameFormatter(names), new MethodSignatureNameFormatter(names));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _peReader.Dispose();
        _disposed = true;
    }

    private (
        Dictionary<int, TypeDefinitionModel> Types,
        Dictionary<int, FieldDefinitionModel> Fields,
        Dictionary<int, MethodDefinitionModel> Methods,
        Dictionary<int, PropertyDefinitionModel> Properties,
        AssemblyStructSource Structs) ReadDefinitions(
        AssemblyIdentityAliases assemblyIdentityAliases,
        IValueTypeDefinitionStackKindResolver stackKinds,
        INativeImportDeclarationReader nativeImports,
        INativeCallbackDeclarationReader nativeCallbacks,
        IUnsafeAccessorDeclarationReader unsafeAccessors,
        INativeImportBinder nativeBinding,
            INativeVariadicCallPlanner nativeVariadics)
    {
        // Sized from the metadata tables, so CoreLib's definitions never rehash.
        var types = new Dictionary<int, TypeDefinitionModel>(Reader.TypeDefinitions.Count);
        var fields = new Dictionary<int, FieldDefinitionModel>(Reader.FieldDefinitions.Count);
        var methods = new Dictionary<int, MethodDefinitionModel>(Reader.MethodDefinitions.Count);
        var properties = new Dictionary<int, PropertyDefinitionModel>(Reader.PropertyDefinitions.Count);
        var disablesRuntimeMarshalling = DisablesRuntimeMarshalling();
        ILookup<string, TypeDefinitionModel>? typesByName = null;
        var constants = Enumerable.Range(1, Reader.GetTableRowCount(TableIndex.Constant))
            .Select(index => Reader.GetConstant(MetadataTokens.ConstantHandle(index)))
            .ToDictionary(constant => MetadataTokens.GetToken(constant.Parent));
        var signatureProvider = new SignatureTypeProvider(
            Identity,
            Reader,
            assemblyIdentityAliases);
        var (pinvokes, variadicCalls) =
            NativeVariadicDeclarations.Resolve(Reader, nativeImports, nativeBinding, nativeVariadics, signatureProvider);
        var stateMachines = AsyncStateMachineKickoffs.Find(Reader);
        var iterators = AsyncStateMachineKickoffs.FindIteratorMembers(Reader);

        foreach (var typeHandle in Reader.TypeDefinitions)
        {
            var definition = Reader.GetTypeDefinition(typeHandle);
            var typeToken = MetadataTokens.GetToken(typeHandle);
            var typeKey = new EntityKey(Identity, typeToken);
            var fieldHandles = definition.GetFields();
            var methodHandles = definition.GetMethods();
            var fieldKeys = ImmutableArray.CreateBuilder<EntityKey>(fieldHandles.Count);
            var methodKeys = ImmutableArray.CreateBuilder<EntityKey>(methodHandles.Count);

            foreach (var fieldHandle in fieldHandles)
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
                        IsInitOnly = (field.Attributes & FieldAttributes.InitOnly) != 0,
                        IsLiteral = (field.Attributes & FieldAttributes.Literal) != 0,
                        MarshalAs = FieldMarshalAs(field, out var complexMarshalAs, out var sizeConst, out var arraySubType),
                        HasComplexMarshalAs = complexMarshalAs,
                        MarshalAsSizeConst = sizeConst,
                        MarshalAsArraySubType = arraySubType,
                        IsPrivate = (field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Private,
                    });
            }

            var typeStackTraceHidden = HasStackTraceHiddenAttribute(definition.GetCustomAttributes());
            var declaresValueType = IsValueTypeBase(definition.BaseType);
            foreach (var methodHandle in methodHandles)
            {
                var method = Reader.GetMethodDefinition(methodHandle);
                var signature = method.DecodeSignature(
                    signatureProvider, genericContext: null);
                var methodToken = MetadataTokens.GetToken(methodHandle);
                var methodKey = new EntityKey(Identity, methodToken);
                methodKeys.Add(methodKey);
                var methodName = Reader.GetString(method.Name);
                var methodSignature = new MethodSignatureModel(signature.ReturnType, signature.ParameterTypes);
                var (Import, Export, WitImport, WitExport, WitPostReturn, declinedBinding, generatedImportBody) =
                    ReadInteropDeclarations(method, methodKey, methodName, methodSignature);
                var nativeImport = pinvokes.GetValueOrDefault(methodToken);
                // A method Roslyn compiled to a state machine over one of the task builders compiles as runtime async:
                // its body is its MoveNext rewritten (StateMachineRewriter). A struct's method keeps the machine, which
                // works on a copy of the struct.
                EntityKey? stateMachineMoveNext =
                    (method.ImplAttributes & RuntimeAsyncImplementation) == 0 && !declaresValueType &&
                    stateMachines.TryGetValue(methodHandle, out var stateMachine) &&
                    TakesTaskBuilder(stateMachine.Machine, signatureProvider)
                        ? new EntityKey(Identity, MetadataTokens.GetToken(stateMachine.MoveNext))
                        : null;
                // An async iterator's machine (a class over the iterator builder): its MoveNextAsync and DisposeAsync
                // compile as runtime async, MoveNextAsync's body MoveNext's rewritten, DisposeAsync's awaiting it in
                // dispose mode (AsyncIteratorBodyBuilder). Both or neither, as the machine's members.
                var iteratorMember = (method.ImplAttributes & RuntimeAsyncImplementation) == 0 &&
                    iterators.TryGetValue(methodHandle, out var iterator) &&
                    TakesBuilder(iterator.Machine, signatureProvider, AsyncStateMachineKickoffs.TakesIteratorBuilder)
                        ? iterator
                        : (AsyncIteratorMember?)null;
                var runtimeAsync = (method.ImplAttributes & RuntimeAsyncImplementation) != 0 || stateMachineMoveNext is not null ||
                    iteratorMember is not null;
                // A method written as runtime async can run its lowered body in a method beside it (RuntimeAsyncBodies),
                // the method itself becoming the entry, where the compilation separates bodies
                // (RuntimeAsyncBodyPlacement). Its own protected blocks stay in the body; only the lowering's catch
                // moves to the entry, which a direct await skips.
                EntityKey? runtimeAsyncBody = (method.ImplAttributes & RuntimeAsyncImplementation) != 0 &&
                    method.RelativeVirtualAddress != 0
                        ? new EntityKey(Identity, RuntimeAsyncBodies.Token(methodToken))
                        : null;
                var stackTraceHidden = typeStackTraceHidden || HasStackTraceHiddenAttribute(method.GetCustomAttributes());
                var binds = nativeImport is null || nativeBinding.Binds(nativeImport);
                // A bound P/Invoke that takes runtime marshalling, or one of the fixed-signature declarations of a C
                // variadic function, compiles to a stub over an inner native method. One the stub cannot express
                // (CLI varargs, HRESULT translation, [MarshalAs], generics) stays an import, which the native
                // declaration validator rejects.
                NativeMarshallingStub? marshallingStub = null;
                var variadic = variadicCalls.GetValueOrDefault(methodToken);
                if (nativeImport is not null && binds &&
                    !nativeImport.IsVarArg && !nativeImport.TranslatesHResult && !nativeImport.HasMarshalling &&
                    method.GetGenericParameters().Count == 0 && definition.GetGenericParameters().Count == 0 &&
                    StubSignature(methodSignature, nativeImport, variadic, disablesRuntimeMarshalling) is { } nativeSignature)
                {
                    var nativeToken = NativeMarshallingStubSignature.NativeMethodToken(methodToken);
                    var nativeKey = new EntityKey(Identity, nativeToken);
                    methods.Add(nativeToken, new MethodDefinitionModel(
                        nativeKey, typeKey, methodName, IsStatic: true, nativeSignature, RelativeVirtualAddress: 0)
                    {
                        NativeImport = nativeImport,
                        IsStackTraceHidden = true,
                    });
                    marshallingStub = new(nativeKey) { Variadic = variadic };
                }
                // A raw import taking or returning a value type by value gets a forwarder with the managed convention,
                // which delegates and function pointers bound to the import bind instead (NativeImportForwarders).
                EntityKey? importForwarder = null;
                if (nativeImport is not null && binds && marshallingStub is null && NativeImportForwarders.Needs(methodSignature))
                {
                    var forwarderToken = NativeImportForwarders.Token(methodToken);
                    importForwarder = new EntityKey(Identity, forwarderToken);
                    methods.Add(forwarderToken, new MethodDefinitionModel(
                        importForwarder.Value, typeKey, methodName, IsStatic: true, methodSignature, RelativeVirtualAddress: 0)
                    {
                        ForwardsNativeImport = methodKey,
                        IsStackTraceHidden = true,
                    });
                }
                methods.Add(
                    methodToken,
                    new MethodDefinitionModel(
                        methodKey,
                        typeKey,
                        methodName,
                        (method.Attributes & MethodAttributes.Static) != 0,
                        methodSignature,
                        // A generated JSImport bound to the host is bodyless, as NetWasm's own import is.
                        generatedImportBody ? 0 : method.RelativeVirtualAddress)
                    {
                        JSImport = Import,
                        JSExport = Export,
                        DeclinedJavaScriptBinding = declinedBinding,
                        WitImport = WitImport,
                        WitExport = WitExport,
                        WitPostReturn = WitPostReturn,
                        NativeImport = binds && marshallingStub is null ? nativeImport : null,
                        UnboundNativeImport = binds ? null : nativeImport,
                        NativeMarshallingStub = marshallingStub,
                        NativeImportForwarder = importForwarder,
                        NativeCallback = nativeCallbacks.Read(Reader, method),
                        DisablesRuntimeMarshalling = disablesRuntimeMarshalling,
                        UnsafeAccessor = unsafeAccessors.Read(Reader, method),
                        GenericArity = method.GetGenericParameters().Count,
                        IsPublic = (method.Attributes & MethodAttributes.MemberAccessMask) ==
                            MethodAttributes.Public,
                        IsVirtual = (method.Attributes & MethodAttributes.Virtual) != 0,
                        IsNewSlot = (method.Attributes & MethodAttributes.NewSlot) != 0,
                        IsFinal = (method.Attributes & MethodAttributes.Final) != 0,
                        IsAbstract = (method.Attributes & MethodAttributes.Abstract) != 0,
                        PreservesBaseOverrides = HasPreserveBaseOverridesAttribute(method),
                        IsAggressiveInlining = (method.ImplAttributes & MethodImplAttributes.AggressiveInlining) != 0,
                        IsNoInlining = (method.ImplAttributes & MethodImplAttributes.NoInlining) != 0,
                        IsStackTraceHidden = stackTraceHidden,
                        // The JIT honors the attribute only in the core library, whose internal type it is.
                        IsIntrinsic = Identity.Name == CoreLibraryName && HasIntrinsicAttribute(method),
                        IsDelegateAsynchronousCall =
                            (method.ImplAttributes & MethodImplAttributes.CodeTypeMask) == MethodImplAttributes.Runtime &&
                            methodName is "BeginInvoke" or "EndInvoke",
                        IsRuntimeAsync = runtimeAsync,
                        RuntimeAsyncBody = runtimeAsyncBody,
                        StateMachineMoveNext = stateMachineMoveNext,
                        AsyncIteratorMoveNext = iteratorMember is { Dispose: false } moveNextAsync
                            ? new EntityKey(Identity, MetadataTokens.GetToken(moveNextAsync.MoveNext))
                            : null,
                        AsyncIteratorDisposeOf = iteratorMember is { Dispose: true } disposeAsync
                            ? new EntityKey(Identity, MetadataTokens.GetToken(disposeAsync.MoveNextAsync))
                            : null,
                    });
                if (runtimeAsyncBody is { } bodyKey)
                {
                    methods.Add(bodyKey.MetadataToken, new MethodDefinitionModel(
                        bodyKey,
                        typeKey,
                        methodName,
                        (method.Attributes & MethodAttributes.Static) != 0,
                        RuntimeAsyncBodies.Signature(methodSignature),
                        RelativeVirtualAddress: 0)
                    {
                        RuntimeAsyncBodyOf = methodKey,
                        GenericArity = method.GetGenericParameters().Count,
                        IsAggressiveInlining = (method.ImplAttributes & MethodImplAttributes.AggressiveInlining) != 0,
                        IsNoInlining = (method.ImplAttributes & MethodImplAttributes.NoInlining) != 0,
                        IsStackTraceHidden = stackTraceHidden,
                    });
                }
                if (runtimeAsync && RuntimeAsyncResumers.Needs(methodSignature))
                {
                    var resumerToken = RuntimeAsyncResumers.Token(methodToken);
                    methods.Add(resumerToken, new MethodDefinitionModel(
                        new EntityKey(Identity, resumerToken),
                        typeKey,
                        $"<resume>{methodName}",
                        (method.Attributes & MethodAttributes.Static) != 0,
                        new MethodSignatureModel(methodSignature.ReturnSignatureType, []),
                        RelativeVirtualAddress: 0)
                    {
                        RuntimeAsyncResumer = methodKey,
                        GenericArity = method.GetGenericParameters().Count,
                        IsStackTraceHidden = true,
                    });
                }
            }

            foreach (var propertyHandle in definition.GetProperties())
            {
                var property = Reader.GetPropertyDefinition(propertyHandle);
                var signature = property.DecodeSignature(
                    signatureProvider,
                    genericContext: null);
                var accessors = property.GetAccessors();
                var propertyToken = MetadataTokens.GetToken(propertyHandle);
                if (!accessors.Getter.IsNil && FeatureSwitchName(property) is { } featureSwitch)
                {
                    var getterToken = MetadataTokens.GetToken(accessors.Getter);
                    methods[getterToken] = methods[getterToken] with { FeatureSwitch = featureSwitch };
                }
                properties.Add(
                    propertyToken,
                    new PropertyDefinitionModel(
                        new EntityKey(Identity, propertyToken),
                        typeKey,
                        Reader.GetString(property.Name),
                        signature.ReturnType,
                        signature.ParameterTypes,
                        ToEntityKey(accessors.Getter),
                        ToEntityKey(accessors.Setter)));
            }

            (var typeNamespace, var typeName) = GetTypeName(typeHandle);
            var layout = definition.GetLayout();
            var genericParameters = definition.GetGenericParameters();
            var isEnum = IsEnumBase(definition.BaseType);
            // The fields this type declares are the ones just read for it.
            var typeFields = fieldKeys.MoveToImmutable();
            var enumUnderlyingType = isEnum
                ? typeFields.Select(key => fields[key.MetadataToken])
                    .Single(field => field.Name == "value__").SignatureType
                : CliTypeIdentity.FromStackKind(stackKinds.Resolve(typeName));
            var enumMembers = isEnum
                ? typeFields.Select(key => fields[key.MetadataToken])
                    .Where(field => field.Name != "value__" && field.IsStatic)
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
                    typeFields,
                    methodKeys.MoveToImmutable())
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
                    GenericArity = genericParameters.Count,
                    GenericParameterNames = [.. genericParameters.Select(handle =>
                        Reader.GetString(Reader.GetGenericParameter(handle).Name))],
                    GenericParameterVariances = [.. genericParameters
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
                    GenericParameterAttributes = [.. genericParameters
                        .Select(handle => (int)Reader.GetGenericParameter(handle).Attributes & 0x3F)],
                    Attributes = (int)definition.Attributes,
                    IsByRefLike = HasByRefLikeAttribute(definition),
                    IsInterface = (definition.Attributes & TypeAttributes.Interface) != 0,
                    IsAbstract = (definition.Attributes & TypeAttributes.Abstract) != 0,
                    IsSealed = (definition.Attributes & TypeAttributes.Sealed) != 0,
                    IsBeforeFieldInit = (definition.Attributes & TypeAttributes.BeforeFieldInit) != 0,
                    IsEnum = isEnum,
                    EnumUnderlyingType = enumUnderlyingType,
                    EnumMembers = enumMembers,
                    IsFlagsEnum = isEnum && HasFlagsAttribute(definition),
                    DeclaringType = definition.GetDeclaringType().IsNil
                        ? null
                        : new EntityKey(typeKey.Assembly, MetadataTokens.GetToken(definition.GetDeclaringType())),
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

        // A struct's native layout is its own, whatever assembly declares it, so another assembly may ask for it.
        var source = new AssemblyStructSource(types, fields, Reader, signatureProvider);
        if (!disablesRuntimeMarshalling)
        {
            Func<CliTypeIdentity, NativeStructLayout?> structs = type => NativeStructLayouts.Plan(type, source, pointerSize: 4);
            // A P/Invoke may name a struct declared after it, so stubs are laid out once every type is read.
            foreach (var token in methods.Keys.ToArray())
            {
                source.Missed = false;
                LayStructStub(token, types, methods, structs);
                if (source.Missed)
                    _crossAssemblyStructMethods.Add(token);
            }
            for (var row = 1; row <= Reader.GetTableRowCount(TableIndex.StandAloneSig); row++)
            {
                source.Missed = false;
                LayFunctionPointerStub(row, methods, signatureProvider, structs);
                if (source.Missed)
                    _crossAssemblyCalliSignatures.Add(row);
            }
        }
        return (types, fields, methods, properties, source);

        EntityKey? ToEntityKey(MethodDefinitionHandle handle) => handle.IsNil
            ? null
            : new EntityKey(Identity, MetadataTokens.GetToken(handle));

        int GetInitializerSize(CliTypeIdentity signature)
        {
            if (PrimitiveInitializerSizes.TryGetValue(
                    signature.CanonicalName,
                    out var primitiveSize))
            {
                return primitiveSize;
            }
            var fullName = signature.FullName!;
            typesByName ??= types.Values.ToLookup(candidate => candidate.FullName, StringComparer.Ordinal);
            return typesByName[fullName].Single().DeclaredSize;
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

    private bool HasIntrinsicAttribute(MethodDefinition method) =>
        method.GetCustomAttributes()
            .Select(handle => Reader.GetCustomAttribute(handle))
            .Any(attribute => GetAttributeTypeName(attribute.Constructor) ==
                "System.Runtime.CompilerServices.IntrinsicAttribute");

    private bool HasPreserveBaseOverridesAttribute(MethodDefinition method) =>
        method.GetCustomAttributes()
            .Select(handle => Reader.GetCustomAttribute(handle))
            .Any(attribute => GetAttributeTypeName(attribute.Constructor) ==
                "System.Runtime.CompilerServices.PreserveBaseOverridesAttribute");

    // [FeatureSwitchDefinition(featureName)]: its one constructor argument, a string.
    private string? FeatureSwitchName(PropertyDefinition property)
    {
        foreach (var handle in property.GetCustomAttributes())
        {
            var attribute = Reader.GetCustomAttribute(handle);
            if (GetAttributeTypeName(attribute.Constructor) !=
                "System.Diagnostics.CodeAnalysis.FeatureSwitchDefinitionAttribute")
                continue;
            var value = Reader.GetBlobReader(attribute.Value);
            return value.ReadUInt16() == 1 ? value.ReadSerializedString() : null;
        }
        return null;
    }

    // The resources embedded in the image's resources directory: each a little-endian length and its bytes at its
    // offset. A linked resource, or one in another file, is not embedded. Read only when a program needs them.
    public ImmutableArray<ManifestResourceData> ManifestResources
    {
        get
        {
            if (_manifestResources.IsDefault)
            {
                _manifestResources = ReadManifestResources();
            }
            return _manifestResources;
        }
    }

    private ImmutableArray<ManifestResourceData> _manifestResources;

    private ImmutableArray<ManifestResourceData> ReadManifestResources()
    {
        var resources = ImmutableArray.CreateBuilder<ManifestResourceData>();
        // A managed assembly has a CLI header: its metadata was read through it.
        var directory = _peReader.PEHeaders.CorHeader!.ResourcesDirectory;
        foreach (var handle in Reader.ManifestResources)
        {
            var resource = Reader.GetManifestResource(handle);
            if (!resource.Implementation.IsNil || directory is not { Size: > 0 } section)
            {
                continue;
            }
            var data = _peReader.GetSectionData(section.RelativeVirtualAddress);
            var reader = data.GetReader((int)resource.Offset, section.Size - (int)resource.Offset);
            var length = reader.ReadInt32();
            resources.Add(new(Reader.GetString(resource.Name), reader.ReadBytes(length)));
        }
        return resources.ToImmutable();
    }

    // The native signature a P/Invoke's stub calls, or null when it compiles to a plain import: a variadic call's fixed
    // parameters and buffer, or the runtime-marshalled signature, which disabled runtime marshalling leaves out (a
    // variadic call under it passes only blittable values).
    private static MethodSignatureModel? StubSignature(MethodSignatureModel signature, NativeImportDeclaration import,
        NativeVariadicCall? variadic, bool disablesRuntimeMarshalling) => variadic switch
        {
            not null when !disablesRuntimeMarshalling || NativeMarshallingStubSignature.Lower(signature) is null =>
                NativeVariadicDeclarations.NativeSignature(signature, variadic),
            null when !disablesRuntimeMarshalling => NativeMarshallingStubSignature.Lower(signature, import),
            _ => null,
        };

    // A P/Invoke whose struct positions take runtime marshalling: a struct that holds a bool or char at any depth passes
    // as its native copy (NativeStructLayouts, NativeMarshallingStubSignature). A plain import becomes a stub, and a
    // stub's native method takes the struct's native form too.
    private void LayStructStub(int token, Dictionary<int, TypeDefinitionModel> types,
        Dictionary<int, MethodDefinitionModel> methods, Func<CliTypeIdentity, NativeStructLayout?> structs)
    {
        // A stub's own native method, whose token has the high bit set, is already in its native form.
        if (token < 0)
            return;
        var method = methods[token];
        var stub = method.NativeMarshallingStub;
        var import = stub is { Variadic: null } ? methods[stub.NativeMethod.MetadataToken].NativeImport : method.NativeImport;
        if (import is not { IsVarArg: false, TranslatesHResult: false, HasMarshalling: false } || stub is { Variadic: not null } ||
            method.GenericArity != 0 || types[method.DeclaringType.MetadataToken].GenericArity != 0 ||
            NativeMarshallingStubSignature.Lower(method.Signature, import, structs) is not { } native)
        {
            return;
        }
        if (stub is not null)
        {
            var inner = methods[stub.NativeMethod.MetadataToken];
            methods[stub.NativeMethod.MetadataToken] = inner with { Signature = native };
            return;
        }
        var nativeToken = NativeMarshallingStubSignature.NativeMethodToken(token);
        var nativeKey = new EntityKey(Identity, nativeToken);
        methods[nativeToken] = new MethodDefinitionModel(
            nativeKey, method.DeclaringType, method.Name, IsStatic: true, native, RelativeVirtualAddress: 0)
        {
            NativeImport = import,
            IsStackTraceHidden = true,
        };
        methods[token] = method with { NativeImport = null, NativeMarshallingStub = new(nativeKey) };
    }

    // Lays out the P/Invokes and unmanaged calli signatures whose structs reach into other assemblies, as desktop .NET
    // lays out a struct of any assembly, once the compilation has read every assembly. Before the compilation's
    // snapshot is taken; an assembly whose signatures name only its own structs has nothing to do.
    internal void LayCrossAssemblyStructStubs(INativeStructSource compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        Func<CliTypeIdentity, NativeStructLayout?> structs = type => NativeStructLayouts.Plan(type, compilation, pointerSize: 4);
        foreach (var token in _crossAssemblyStructMethods)
            LayStructStub(token, _types, _methods, structs);
        foreach (var row in _crossAssemblyCalliSignatures)
            LayFunctionPointerStub(row, _methods, _structs.Signatures, structs);
    }

    // One of this assembly's value types and its instance fields, for a compilation that lays out another assembly's
    // struct; null when this assembly does not declare it.
    internal (TypeDefinitionModel Definition, ImmutableArray<FieldDefinitionModel> Fields)? DescribeValueType(CliTypeIdentity type) =>
        _structs.Describe(type);

    // The declaration an unmanaged function pointer stands for: no DllImport attributes, so ANSI chars and strings,
    // BOOL bools, and no [In], [Out] or [MarshalAs], which a function pointer's signature cannot carry.
    private static readonly NativeImportDeclaration FunctionPointerDeclaration =
        new(string.Empty, string.Empty, MethodImportAttributes.None, IsVarArg: false, HasMarshalling: false,
            UnsupportedCallingConvention: null);

    // An unmanaged calli signature that takes runtime marshalling, as desktop .NET's IL stub for an unmanaged calli
    // marshals bool, char, string and structs that hold them. It becomes a stub that takes the call's arguments and
    // then the pointer, which the body reader calls in the calli's place (NativeFunctionPointerCall); its token is the
    // signature's with the high bit set. A signature that names generic parameters, or conventions the C ABI cannot
    // honor, keeps its calli and the diagnostics that go with it.
    private void LayFunctionPointerStub(int row, Dictionary<int, MethodDefinitionModel> methods,
        SignatureTypeProvider signatureProvider, Func<CliTypeIdentity, NativeStructLayout?> structs)
    {
        var handle = MetadataTokens.StandaloneSignatureHandle(row);
        var standalone = Reader.GetStandaloneSignature(handle);
        // Besides calli and locals signatures the table holds field signatures, which compilers leave for the
        // constants a Windows PDB describes and StandaloneSignature.GetKind refuses; the header alone says a method's.
        var blob = Reader.GetBlobReader(standalone.Signature);
        if (blob.ReadSignatureHeader().Kind != SignatureKind.Method)
            return;
        var decoded = standalone.DecodeMethodSignature(signatureProvider, genericContext: null);
        if (!UnmanagedCallingConventions.IsUnmanagedHeader((byte)decoded.Header.CallingConvention))
            return;
        var returnType = decoded.ReturnType;
        var conventions = new List<string?>();
        while (returnType.Shape == CliTypeShape.Modified)
        {
            if (!returnType.IsRequiredModifier)
                conventions.Add(returnType.CustomModifier!.FullName);
            returnType = returnType.ElementType!;
        }
        var signature = new MethodSignatureModel(returnType, decoded.ParameterTypes);
        if (decoded.Header.CallingConvention == SignatureCallingConvention.Unmanaged &&
            UnmanagedCallingConventions.Unsupported(conventions) is not null ||
            returnType.ContainsGenericParameters || decoded.ParameterTypes.Any(type => type.ContainsGenericParameters) ||
            NativeMarshallingStubSignature.Lower(signature, FunctionPointerDeclaration, structs) is not { } native)
        {
            return;
        }
        var token = NativeMarshallingStubSignature.NativeMethodToken(MetadataTokens.GetToken(handle));
        var key = new EntityKey(Identity, token);
        var module = new EntityKey(Identity, MetadataTokens.GetToken(MetadataTokens.TypeDefinitionHandle(1)));
        methods[token] = new MethodDefinitionModel(key, module, $"<calli>{row:x}", IsStatic: true,
            new MethodSignatureModel(returnType, [.. decoded.ParameterTypes, CliTypeIdentity.FromStackKind(CliValueKind.NativeInt)]),
            RelativeVirtualAddress: 0)
        {
            NativeMarshallingStub = new(key) { FunctionPointer = new(native, FunctionPointerDeclaration) },
            IsStackTraceHidden = true,
        };
    }

    // The stub an unmanaged calli of this standalone signature calls instead, when its signature takes runtime
    // marshalling (LayFunctionPointerStubs).
    internal MethodDefinitionModel? FunctionPointerStub(int signatureToken) =>
        _methods.TryGetValue(NativeMarshallingStubSignature.NativeMethodToken(signatureToken), out var stub) &&
        stub.NativeMarshallingStub is { FunctionPointer: not null } ? stub : null;

    // This assembly's value types by identity, as a P/Invoke's signature names them. Missed: a value type was asked
    // for that another assembly declares.
    private sealed class AssemblyStructSource(
        Dictionary<int, TypeDefinitionModel> types,
        Dictionary<int, FieldDefinitionModel> fields,
        MetadataReader reader,
        SignatureTypeProvider signatureProvider) : INativeStructSource
    {
        public bool Missed { get; set; }

        public SignatureTypeProvider Signatures => signatureProvider;

        private readonly Lazy<Dictionary<string, int>> _tokens = new(() => types
            .Where(pair => pair.Value.IsValueType)
            .ToDictionary(
                pair => signatureProvider.GetTypeFromDefinition(reader,
                    (TypeDefinitionHandle)MetadataTokens.Handle(pair.Key), (byte)SignatureTypeKind.ValueType).CanonicalName,
                pair => pair.Key,
                StringComparer.Ordinal));

        public (TypeDefinitionModel Definition, ImmutableArray<FieldDefinitionModel> Fields)? Describe(CliTypeIdentity type)
        {
            if (!_tokens.Value.TryGetValue(type.CanonicalName, out var token))
            {
                Missed = true;
                return null;
            }
            var definition = types[token];
            return (definition, [.. definition.Fields.Select(key => fields[key.MetadataToken]).Where(field => !field.IsStatic)]);
        }
    }

    // A field's [MarshalAs] native type when its descriptor is just that, and whether it says more. An inline string or
    // array (ByValTStr or ByValArray) also gives its SizeConst and, for an array, its ArraySubType: the descriptor is
    // the native type, the compressed size, then the optional element type.
    private UnmanagedType? FieldMarshalAs(FieldDefinition field, out bool complex, out int? sizeConst,
        out UnmanagedType? arraySubType)
    {
        complex = false;
        sizeConst = null;
        arraySubType = null;
        var descriptor = field.GetMarshallingDescriptor();
        if (descriptor.IsNil)
            return null;
        var blob = Reader.GetBlobReader(descriptor);
        if (blob.Length == 1)
            return (UnmanagedType)blob.ReadByte();
        complex = true;
        var native = (UnmanagedType)blob.ReadByte();
        if (native is not (UnmanagedType.ByValTStr or UnmanagedType.ByValArray) ||
            !blob.TryReadCompressedInteger(out var size))
        {
            return null;
        }
        sizeConst = size;
        if (native == UnmanagedType.ByValArray && blob.RemainingBytes > 0)
            arraySubType = (UnmanagedType)blob.ReadByte();
        return native;
    }

    private bool DisablesRuntimeMarshalling() =>
        Reader.GetAssemblyDefinition().GetCustomAttributes()
            .Select(handle => Reader.GetCustomAttribute(handle))
            .Any(attribute => GetAttributeTypeName(attribute.Constructor) ==
                "System.Runtime.CompilerServices.DisableRuntimeMarshallingAttribute");

    private bool HasStackTraceHiddenAttribute(CustomAttributeHandleCollection attributes) =>
        attributes
            .Select(handle => Reader.GetCustomAttribute(handle))
            .Any(attribute => GetAttributeTypeName(attribute.Constructor) ==
                "System.Diagnostics.StackTraceHiddenAttribute");

    private bool HasByRefLikeAttribute(TypeDefinition definition) =>
        definition.GetCustomAttributes()
            .Select(handle => Reader.GetCustomAttribute(handle))
            .Any(attribute => GetAttributeTypeName(attribute.Constructor) ==
                "System.Runtime.CompilerServices.IsByRefLikeAttribute");

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
        WitPostReturnDeclaration? WitPostReturn,
        DeclinedJavaScriptBinding? Declined,
        bool SetsGeneratedBodyAside)
        ReadInteropDeclarations(MethodDefinition method, EntityKey methodKey, string methodName,
            MethodSignatureModel signature)
    {
        var parameterCount = signature.ParameterTypes.Length;
        var import = (InteropImportDeclaration?)null;
        var export = (InteropExportDeclaration?)null;
        var witImport = (WitImportDeclaration?)null;
        var witExport = (WitExportDeclaration?)null;
        var witPostReturn = (WitPostReturnDeclaration?)null;
        var generatedBinding = false;
        foreach (var attributeHandle in method.GetCustomAttributes())
        {
            var attribute = Reader.GetCustomAttribute(attributeHandle);
            var attributeType = GetAttributeTypeName(attribute.Constructor);
            generatedBinding |= attributeType == "System.Diagnostics.DebuggerNonUserCodeAttribute";
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
                    MarshalAs = ReadMarshalAs(method, parameterCount),
                };
            }
            else if (attributeType == "System.Runtime.InteropServices.JavaScript.JSExportAttribute")
            {
                if (export is not null)
                {
                    throw InvalidInteropDeclaration(methodKey, "multiple JSExport attributes are not supported");
                }
                var (nonNullableParameters, nonNullableResult) = ReadNonNullable(method, parameterCount);
                export = ReadJSExport(attribute, methodKey) with
                {
                    ParameterNames = ReadParameterNames(method, parameterCount),
                    NonNullableParameters = nonNullableParameters,
                    NonNullableResult = nonNullableResult,
                    MarshalAs = ReadMarshalAs(method, parameterCount),
                };
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
            else if (attributeType == "System.Runtime.InteropServices.WebAssembly.ComponentBuiltInAttribute")
            {
                if (witImport is not null)
                {
                    throw InvalidInteropDeclaration(methodKey,
                        "multiple WitImport or ComponentBuiltIn attributes are not supported");
                }
                var (moduleName, functionName) = ReadWitNames(
                    attribute, methodKey, "ComponentBuiltIn");
                witImport = new WitImportDeclaration(moduleName, functionName)
                {
                    IsCanonicalBuiltIn = true,
                };
            }
            else if (attributeType == "System.Runtime.InteropServices.WebAssembly.ComponentExportAttribute")
            {
                if (witExport is not null)
                {
                    throw InvalidInteropDeclaration(methodKey,
                        "multiple WitExport or ComponentExport attributes are not supported");
                }
                var (_, name) = ReadWitNames(attribute, methodKey, "ComponentExport");
                witExport = new WitExportDeclaration(string.Empty, name) { IsComponentLift = true };
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
        // .NET's JSImport generator gives the method a body that binds the import through JSFunctionBinding and marks
        // it [DebuggerNonUserCode]. With a JavaScript host the method is NetWasm's own import, its body set aside, when
        // NetWasm's interop takes its shape; otherwise it is ordinary managed code, whose binding throws
        // PlatformNotSupportedException, as on .NET's platforms without a browser. A body on a JSImport of NetWasm's
        // own, which has none, stays an error.
        DeclinedJavaScriptBinding? declined = null;
        var setsGeneratedBodyAside = false;
        if (import is not null && generatedBinding && method.RelativeVirtualAddress != 0)
        {
            if (!_javaScriptBinding.BindsToHost)
            {
                import = null;
            }
            else if (JavaScriptBoundaryShapes.UnsupportedImport(signature, import.MarshalAs, import.IsPromise,
                         JavaScriptBoundaryShapes.StandardDelegateInvoke) is { } reason)
            {
                import = null;
                declined = new("JSImport", reason);
            }
            else
            {
                setsGeneratedBodyAside = true;
            }
        }
        // .NET's JSExport generator leaves the method as written and adds a [DebuggerNonUserCode] wrapper beside it,
        // which .NET's browser runtime registers. NetWasm exports the method itself; one whose shape NetWasm's
        // interop does not take stays an ordinary method JavaScript cannot call, where NetWasm's own export is an
        // error.
        if (export is not null && HasGeneratedExportWrapper(method, methodName) &&
            JavaScriptBoundaryShapes.UnsupportedExport(signature, export.MarshalAs) is { } exportReason)
        {
            export = null;
            declined = new("JSExport", exportReason);
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
        return (import, export, witImport, witExport, witPostReturn, declined, setsGeneratedBodyAside);
    }

    // .NET's JSExport generator names its wrapper __Wrapper_<method>_<hash> and marks it [DebuggerNonUserCode], in the
    // method's own type.
    private bool HasGeneratedExportWrapper(MethodDefinition method, string methodName)
    {
        var prefix = $"__Wrapper_{methodName}_";
        foreach (var handle in Reader.GetTypeDefinition(method.GetDeclaringType()).GetMethods())
        {
            var candidate = Reader.GetMethodDefinition(handle);
            var name = Reader.GetString(candidate.Name);
            if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal) &&
                name.AsSpan(prefix.Length).TrimStart('-').ToString() is { Length: > 0 } hash && hash.All(char.IsAsciiDigit) &&
                candidate.GetCustomAttributes().Any(attribute => GetAttributeTypeName(Reader.GetCustomAttribute(attribute).Constructor) ==
                    "System.Diagnostics.DebuggerNonUserCodeAttribute"))
            {
                return true;
            }
        }
        return false;
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
        // One argument names a function of the world itself; two name an
        // interface and its function. The empty interface means the world.
        var worldFunction = ConstructorParameterCount(attribute) == 1;
        var interfaceName = worldFunction ? string.Empty : reader.ReadSerializedString();
        var functionName = reader.ReadSerializedString();
        if (interfaceName is null || string.IsNullOrWhiteSpace(functionName) ||
            reader.ReadUInt16() != 0 || reader.RemainingBytes != 0)
        {
            throw InvalidInteropDeclaration(methodKey,
                $"{declaration} must specify an interface and non-empty function name with no named arguments");
        }
        if (!worldFunction && interfaceName.Length == 0)
        {
            throw InvalidInteropDeclaration(methodKey,
                $"{declaration} names a world's own function with one argument: [{declaration}(\"{functionName}\")]");
        }
        return (interfaceName, functionName);
    }

    private int ConstructorParameterCount(CustomAttribute attribute)
    {
        var signature = attribute.Constructor.Kind == HandleKind.MethodDefinition
            ? Reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).Signature
            : Reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Signature;
        var blob = Reader.GetBlobReader(signature);
        blob.ReadSignatureHeader();
        return blob.ReadCompressedInteger();
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

    // Each parameter's [JSMarshalAs<T>] marker and the result's (sequence 0).
    private JavaScriptMarshalAs ReadMarshalAs(MethodDefinition method, int count)
    {
        const string attribute = "System.Runtime.InteropServices.JavaScript.JSMarshalAsAttribute`1<";
        var markers = new string?[count + 1];
        foreach (var handle in method.GetParameters())
        {
            var parameter = Reader.GetParameter(handle);
            if (parameter.SequenceNumber > count)
            {
                continue;
            }
            foreach (var attributeHandle in parameter.GetCustomAttributes())
            {
                var constructor = Reader.GetCustomAttribute(attributeHandle).Constructor;
                if (constructor.Kind != HandleKind.MemberReference ||
                    Reader.GetMemberReference((MemberReferenceHandle)constructor).Parent is not
                    { Kind: HandleKind.TypeSpecification } parent)
                {
                    continue;
                }
                var type = Reader.GetTypeSpecification((TypeSpecificationHandle)parent)
                    .DecodeSignature(new JSTypeMarkerProvider(Reader), null);
                if (type.StartsWith(attribute, StringComparison.Ordinal) && type.EndsWith('>'))
                {
                    markers[parameter.SequenceNumber] = type[attribute.Length..^1];
                }
            }
        }
        return markers.All(marker => marker is null)
            ? JavaScriptMarshalAs.None
            : new([.. markers.Skip(1)], markers[0]);
    }

    private ImmutableArray<string> ReadParameterNames(MethodDefinition method, int count)
    {
        var names = new string[count];
        Array.Fill(names, string.Empty);
        foreach (var handle in method.GetParameters())
        {
            // Sequence 0 describes the return value; parameters start at 1.
            var parameter = Reader.GetParameter(handle);
            if (parameter.SequenceNumber >= 1 && parameter.SequenceNumber <= count)
            {
                names[parameter.SequenceNumber - 1] = Reader.GetString(parameter.Name);
            }
        }
        return [.. names];
    }

    // The top-level nullable annotation of each parameter and of the result
    // (sequence 0): its NullableAttribute, else the nearest NullableContextAttribute
    // on the method or an enclosing type. An oblivious annotation counts as nullable.
    private (ImmutableArray<bool> Parameters, bool Result) ReadNonNullable(MethodDefinition method, int count)
    {
        const byte notNull = 1;
        var context = FirstNullableByte(method.GetCustomAttributes(), NullableContextAttributeName)
            ?? TypeNullableContext(method.GetDeclaringType());
        var annotations = new byte?[count + 1];
        foreach (var handle in method.GetParameters())
        {
            var parameter = Reader.GetParameter(handle);
            if (parameter.SequenceNumber <= count)
            {
                annotations[parameter.SequenceNumber] =
                    FirstNullableByte(parameter.GetCustomAttributes(), NullableAttributeName);
            }
        }
        bool NotNull(int index) => (annotations[index] ?? context) == notNull;
        return ([.. Enumerable.Range(1, count).Select(NotNull)], NotNull(0));
    }

    private const string CoreLibraryName = "NetWasm.CoreLib";
    private const string NullableAttributeName = "System.Runtime.CompilerServices.NullableAttribute";
    private const string NullableContextAttributeName = "System.Runtime.CompilerServices.NullableContextAttribute";

    private byte? TypeNullableContext(TypeDefinitionHandle handle)
    {
        while (!handle.IsNil)
        {
            var type = Reader.GetTypeDefinition(handle);
            if (FirstNullableByte(type.GetCustomAttributes(), NullableContextAttributeName) is { } value)
            {
                return value;
            }
            handle = type.GetDeclaringType();
        }
        return null;
    }

    // NullableContextAttribute takes one byte. NullableAttribute takes one byte or
    // a byte array whose first element describes the outermost type.
    private byte? FirstNullableByte(CustomAttributeHandleCollection attributes, string name)
    {
        foreach (var handle in attributes)
        {
            var attribute = Reader.GetCustomAttribute(handle);
            if (GetAttributeTypeName(attribute.Constructor) != name)
            {
                continue;
            }
            var reader = Reader.GetBlobReader(attribute.Value);
            if (reader.Length < 5 || reader.ReadUInt16() != 1)
            {
                return null;
            }
            if (reader.Length == 5)
            {
                return reader.ReadByte();
            }
            return reader.ReadInt32() > 0 ? reader.ReadByte() : null;
        }
        return null;
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
        if (constructor.Kind == HandleKind.TypeDefinition)
        {
            return GetTypeDefinitionFullName((TypeDefinitionHandle)constructor);
        }
        if (constructor.Kind == HandleKind.TypeSpecification)
        {
            // Compiler declaration attributes are non-generic. A constructed
            // user attribute cannot match any of those declarations.
            return null;
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

    // Whether a state machine's builder (its <>t__builder field) is one of the task builders the rewrite takes.
    private bool TakesTaskBuilder(TypeDefinitionHandle machine, SignatureTypeProvider signatureProvider) =>
        TakesBuilder(machine, signatureProvider, AsyncStateMachineKickoffs.TakesBuilder);

    // Whether a machine's builder (its <>t__builder field) is one the predicate takes.
    private bool TakesBuilder(TypeDefinitionHandle machine, SignatureTypeProvider signatureProvider,
        Func<CliTypeIdentity, bool> takes)
    {
        foreach (var handle in Reader.GetTypeDefinition(machine).GetFields())
        {
            var field = Reader.GetFieldDefinition(handle);
            if (!Reader.StringComparer.Equals(field.Name, "<>t__builder")) continue;
            return takes(field.DecodeSignature(signatureProvider, genericContext: null));
        }
        return false;
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
