using System.Buffers.Binary;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class RuntimeTypeFactsEncodingTests
{
    private static readonly AssemblyIdentity Assembly = new("type-facts");

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EncodesClassificationTypeCodeAndRelationshipsAtTargetWidth(
        WasmTarget targetKind)
    {
        var target = WasmTargetLayout.For(targetKind);
        var state = new ManagedStaticDataBuildState();
        var identity = CliTypeIdentity.Named(
            Assembly,
            "Fixtures",
            "Number",
            isValueType: true,
            CliValueKind.I4);
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "Fixtures",
            "Number",
            IsValueType: true,
            [],
            [])
        {
            IsEnum = true,
            IsSealed = true,
            EnumUnderlyingType = CliTypeIdentity.Primitive("u2", CliValueKind.I4),
        };

        RuntimeTypeFactsEncoding.Add(
            state,
            target,
            identity,
            definition,
            typeId: 7,
            baseTypeId: 3,
            assignableTypeIdsAddress: 88,
            assignableTypeIdCount: 2);

        var address = state.TypeFacts[7];
        var data = Assert.Single(state.Segments).Data;
        Assert.Equal(address, Assert.Single(state.Segments).Address);
        Assert.Equal(RuntimeTypeFactsEncoding.Size(target), data.Length);
        Assert.Equal(
            (int)(RuntimeTypeFactsFlags.ValueType |
                  RuntimeTypeFactsFlags.Enum |
                  RuntimeTypeFactsFlags.Sealed),
            ReadInt32(data, RuntimeTypeFactsEncoding.FlagsOffset));
        Assert.Equal(8, ReadInt32(data, RuntimeTypeFactsEncoding.TypeCodeOffset));
        Assert.Equal(3, ReadInt32(data, RuntimeTypeFactsEncoding.BaseTypeIdOffset));
        Assert.Equal(2, ReadInt32(data, RuntimeTypeFactsEncoding.AssignableTypeCountOffset));
        Assert.Equal(
            88,
            ReadAddress(data, RuntimeTypeFactsEncoding.AssignableTypeIdsOffset, target));
        Assert.Equal(
            0,
            ReadAddress(data, RuntimeTypeFactsEncoding.DelegateInvokeOffset(target), target));
        Assert.Equal(
            0,
            ReadInt32(data, RuntimeTypeFactsEncoding.GenericArgumentCountOffset(target)));
        Assert.Equal(
            0,
            ReadAddress(
                data,
                RuntimeTypeFactsEncoding.GenericArgumentTypeIdsOffset(target),
                target));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EncodesGenericArgumentMetadataAtTargetWidth(WasmTarget targetKind)
    {
        var target = WasmTargetLayout.For(targetKind);
        var state = new ManagedStaticDataBuildState();
        var identity = CliTypeIdentity.Named(
            Assembly,
            "Fixtures",
            "Box`1",
            isValueType: false);
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "Fixtures",
            "Box`1",
            IsValueType: false,
            [],
            []);

        RuntimeTypeFactsEncoding.Add(
            state,
            target,
            identity,
            definition,
            typeId: 7,
            baseTypeId: 3,
            assignableTypeIdsAddress: 0,
            assignableTypeIdCount: 0,
            genericArgumentTypeIdsAddress: 96,
            genericArgumentCount: 2);

        var data = Assert.Single(state.Segments).Data;
        Assert.Equal(
            2,
            ReadInt32(data, RuntimeTypeFactsEncoding.GenericArgumentCountOffset(target)));
        Assert.Equal(
            96,
            ReadAddress(
                data,
                RuntimeTypeFactsEncoding.GenericArgumentTypeIdsOffset(target),
                target));
    }

    [Fact]
    public void EncodesMetadataOnlyGenericParameterWithoutDefinitionFlags()
    {
        var state = new ManagedStaticDataBuildState();

        RuntimeTypeFactsEncoding.Add(
            state,
            WasmTargetLayout.Wasm32,
            CliTypeIdentity.ScopedGenericParameter("owner", method: false, index: 0),
            definition: null,
            typeId: 7,
            baseTypeId: 0,
            assignableTypeIdsAddress: 0,
            assignableTypeIdCount: 0);

        var flags = (RuntimeTypeFactsFlags)ReadInt32(
            Assert.Single(state.Segments).Data,
            RuntimeTypeFactsEncoding.FlagsOffset);
        Assert.Equal(RuntimeTypeFactsFlags.ContainsGenericParameters, flags);
    }

    [Theory]
    [InlineData(0, 96)]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    public void RejectsInconsistentGenericArgumentMetadata(int count, int address)
    {
        var exception = Assert.Throws<CompilerException>(() =>
            RuntimeTypeFactsEncoding.Add(
                new ManagedStaticDataBuildState(),
                WasmTargetLayout.Wasm32,
                CliTypeIdentity.Named(Assembly, "Fixtures", "Box`1", false),
                new TypeDefinitionModel(
                    new EntityKey(Assembly, 1),
                    "Fixtures",
                    "Box`1",
                    IsValueType: false,
                    [],
                    []),
                typeId: 7,
                baseTypeId: 0,
                assignableTypeIdsAddress: 0,
                assignableTypeIdCount: 0,
                genericArgumentTypeIdsAddress: address,
                genericArgumentCount: count));

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
    }

    [Fact]
    public void RejectsMissingDefinitionForDefinitionBackedIdentity()
    {
        var exception = Assert.Throws<CompilerException>(() =>
            RuntimeTypeFactsEncoding.Add(
                new ManagedStaticDataBuildState(),
                WasmTargetLayout.Wasm32,
                CliTypeIdentity.Named(Assembly, "Fixtures", "Box`1", false),
                definition: null,
                typeId: 7,
                baseTypeId: 0,
                assignableTypeIdsAddress: 0,
                assignableTypeIdCount: 0));

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EncodesDelegateInvokeDescriptorAtTargetWidth(WasmTarget targetKind)
    {
        var target = WasmTargetLayout.For(targetKind);
        var state = new ManagedStaticDataBuildState();
        var identity = CliTypeIdentity.Named(
            Assembly,
            "Fixtures",
            "Callback",
            isValueType: false);
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "Fixtures",
            "Callback",
            IsValueType: false,
            [],
            []);

        RuntimeTypeFactsEncoding.Add(
            state,
            target,
            identity,
            definition,
            typeId: 7,
            baseTypeId: 0,
            assignableTypeIdsAddress: 0,
            assignableTypeIdCount: 0,
            delegateInvokeAddress: 4096);

        Assert.Equal(
            4096,
            ReadAddress(
                Assert.Single(state.Segments).Data,
                RuntimeTypeFactsEncoding.DelegateInvokeOffset(target),
                target));
    }

    [Fact]
    public void EncodesNullableOnlyForClosedNullableIdentity()
    {
        var state = new ManagedStaticDataBuildState();
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "System",
            "Nullable`1",
            IsValueType: true,
            [],
            [])
        {
            GenericArity = 1,
            IsSealed = true,
        };
        var identity = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(
                Assembly,
                definition.Namespace,
                definition.Name,
                isValueType: true),
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);

        RuntimeTypeFactsEncoding.Add(
            state,
            WasmTargetLayout.Wasm32,
            identity,
            definition,
            typeId: 7,
            baseTypeId: 3,
            assignableTypeIdsAddress: 0,
            assignableTypeIdCount: 0);

        Assert.Equal(
            (int)(RuntimeTypeFactsFlags.ValueType |
                  RuntimeTypeFactsFlags.GenericType |
                  RuntimeTypeFactsFlags.Sealed |
                  RuntimeTypeFactsFlags.Nullable),
            ReadInt32(
                Assert.Single(state.Segments).Data,
                RuntimeTypeFactsEncoding.FlagsOffset));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void EncodesAbstractOnlyForDefinitionBackedIdentities(
        bool abstractDefinition,
        bool expected)
    {
        var state = new ManagedStaticDataBuildState();
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "Fixtures",
            "Base",
            IsValueType: false,
            [],
            [])
        {
            IsAbstract = abstractDefinition,
        };

        RuntimeTypeFactsEncoding.Add(
            state,
            WasmTargetLayout.Wasm32,
            CliTypeIdentity.Named(
                Assembly,
                definition.Namespace,
                definition.Name,
                isValueType: false),
            definition,
            typeId: 7,
            baseTypeId: 3,
            assignableTypeIdsAddress: 0,
            assignableTypeIdCount: 0);

        var flags = (RuntimeTypeFactsFlags)ReadInt32(
            Assert.Single(state.Segments).Data,
            RuntimeTypeFactsEncoding.FlagsOffset);
        Assert.Equal(expected, (flags & RuntimeTypeFactsFlags.Abstract) != 0);
    }

    [Theory]
    [InlineData("primitive:bool", 3)]
    [InlineData("primitive:char", 4)]
    [InlineData("primitive:i1", 5)]
    [InlineData("primitive:u1", 6)]
    [InlineData("primitive:i2", 7)]
    [InlineData("primitive:u2", 8)]
    [InlineData("primitive:i4", 9)]
    [InlineData("primitive:u4", 10)]
    [InlineData("primitive:i8", 11)]
    [InlineData("primitive:u8", 12)]
    [InlineData("primitive:f4", 13)]
    [InlineData("primitive:f8", 14)]
    [InlineData("primitive:string", 18)]
    public void EncodesEveryPrimitiveTypeCode(string canonicalName, int expected)
    {
        var state = new ManagedStaticDataBuildState();
        var identity = Primitive(canonicalName);
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "System",
            "Primitive",
            identity.IsValueType,
            [],
            []);

        RuntimeTypeFactsEncoding.Add(
            state,
            WasmTargetLayout.Wasm32,
            identity,
            definition,
            1,
            0,
            0,
            0);

        Assert.Equal(
            expected,
            ReadInt32(
                Assert.Single(state.Segments).Data,
                RuntimeTypeFactsEncoding.TypeCodeOffset));
    }

    [Theory]
    [InlineData("System.DBNull", false, 2)]
    [InlineData("System.Decimal", true, 15)]
    [InlineData("System.DateTime", true, 16)]
    public void EncodesSpecialNamedTypeCodes(
        string fullName,
        bool isValueType,
        int expected)
    {
        var separator = fullName.LastIndexOf('.');
        var identity = CliTypeIdentity.Named(
            Assembly,
            fullName[..separator],
            fullName[(separator + 1)..],
            isValueType);
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            fullName[..separator],
            fullName[(separator + 1)..],
            isValueType,
            [],
            []);
        var state = new ManagedStaticDataBuildState();

        RuntimeTypeFactsEncoding.Add(
            state,
            WasmTargetLayout.Wasm32,
            identity,
            definition,
            1,
            0,
            0,
            0);

        Assert.Equal(
            expected,
            ReadInt32(
                Assert.Single(state.Segments).Data,
                RuntimeTypeFactsEncoding.TypeCodeOffset));
    }

    [Theory]
    [InlineData(false, (int)(RuntimeTypeFactsFlags.ByReference |
                             RuntimeTypeFactsFlags.Class))]
    [InlineData(true, (int)(RuntimeTypeFactsFlags.Pointer |
                            RuntimeTypeFactsFlags.Class))]
    public void EncodesManagedAddressAndPointerFlags(
        bool unmanaged,
        int expected)
    {
        var element = CliTypeIdentity.Named(
            Assembly,
            "Fixtures",
            "Value",
            isValueType: true);
        var identity = unmanaged
            ? CliTypeIdentity.UnmanagedPointer(element)
            : CliTypeIdentity.ManagedByReference(element);
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "Fixtures",
            "Value",
            IsValueType: true,
            [],
            [])
        {
            IsEnum = true,
            IsInterface = true,
            IsSealed = true,
            EnumUnderlyingType = CliTypeIdentity.Primitive("u2", CliValueKind.I4),
        };
        var state = new ManagedStaticDataBuildState();

        RuntimeTypeFactsEncoding.Add(
            state,
            WasmTargetLayout.Wasm32,
            identity,
            definition,
            1,
            0,
            0,
            0);

        Assert.Equal(
            expected,
            ReadInt32(
                Assert.Single(state.Segments).Data,
                RuntimeTypeFactsEncoding.FlagsOffset));
        Assert.Equal(
            1,
            ReadInt32(
                Assert.Single(state.Segments).Data,
                RuntimeTypeFactsEncoding.TypeCodeOffset));
    }

    [Theory]
    [InlineData(
        "i4",
        CliValueKind.I4,
        true,
        (int)(RuntimeTypeFactsFlags.ValueType | RuntimeTypeFactsFlags.Sealed))]
    [InlineData(
        "string",
        CliValueKind.ManagedReference,
        false,
        (int)(RuntimeTypeFactsFlags.Class | RuntimeTypeFactsFlags.Sealed))]
    public void EncodesDefinitionBackedPrimitiveFlags(
        string name,
        CliValueKind stackKind,
        bool isValueType,
        int expected)
    {
        var identity = CliTypeIdentity.Primitive(name, stackKind, isValueType);
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "System",
            isValueType ? "Int32" : "String",
            isValueType,
            [],
            [])
        {
            IsSealed = true,
        };
        var state = new ManagedStaticDataBuildState();

        RuntimeTypeFactsEncoding.Add(
            state,
            WasmTargetLayout.Wasm32,
            identity,
            definition,
            1,
            0,
            0,
            0);

        Assert.Equal(
            expected,
            ReadInt32(
                Assert.Single(state.Segments).Data,
                RuntimeTypeFactsEncoding.FlagsOffset));
    }

    [Theory]
    [InlineData(0, (int)RuntimeTypeFactsFlags.ContainsGenericParameters)]
    [InlineData(1, (int)(RuntimeTypeFactsFlags.ContainsGenericParameters |
                         RuntimeTypeFactsFlags.GenericTypeDefinition |
                         RuntimeTypeFactsFlags.GenericType |
                         RuntimeTypeFactsFlags.Class))]
    [InlineData(2, (int)RuntimeTypeFactsFlags.Interface)]
    [InlineData(3, (int)RuntimeTypeFactsFlags.Class)]
    public void EncodesGenericParameterAndReferenceClassificationFlags(
        int kind,
        int expected)
    {
        var identity = kind switch
        {
            0 => CliTypeIdentity.GenericParameter(method: false, 0),
            _ => CliTypeIdentity.Named(
                Assembly,
                "Fixtures",
                kind == 1 ? "Box`1" : kind == 2 ? "IMarker" : "Reference",
                isValueType: false),
        };
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "Fixtures",
            kind == 1 ? "Box`1" : kind == 2 ? "IMarker" : "Reference",
            IsValueType: false,
            [],
            [])
        {
            GenericArity = kind == 1 ? 1 : 0,
            IsInterface = kind == 2,
        };
        var state = new ManagedStaticDataBuildState();

        RuntimeTypeFactsEncoding.Add(
            state,
            WasmTargetLayout.Wasm32,
            identity,
            definition,
            1,
            0,
            0,
            0);

        Assert.Equal(
            expected,
            ReadInt32(
                Assert.Single(state.Segments).Data,
                RuntimeTypeFactsEncoding.FlagsOffset));
    }

    [Fact]
    public void RejectsDuplicateSemanticTypeIdsWithRuntimeContract()
    {
        var state = new ManagedStaticDataBuildState();
        var identity = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "System",
            "Int32",
            IsValueType: true,
            [],
            []);
        RuntimeTypeFactsEncoding.Add(
            state,
            WasmTargetLayout.Wasm32,
            identity,
            definition,
            1,
            0,
            0,
            0);

        var exception = Assert.Throws<CompilerException>(() =>
            RuntimeTypeFactsEncoding.Add(
                state,
                WasmTargetLayout.Wasm32,
                identity,
                definition,
                1,
                0,
                0,
                0));

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
    }

    private static CliTypeIdentity Primitive(string canonicalName) => canonicalName switch
    {
        "primitive:bool" => CliTypeIdentity.Primitive("bool", CliValueKind.I4),
        "primitive:char" => CliTypeIdentity.Primitive("char", CliValueKind.I4),
        "primitive:i1" => CliTypeIdentity.Primitive("i1", CliValueKind.I4),
        "primitive:u1" => CliTypeIdentity.Primitive("u1", CliValueKind.I4),
        "primitive:i2" => CliTypeIdentity.Primitive("i2", CliValueKind.I4),
        "primitive:u2" => CliTypeIdentity.Primitive("u2", CliValueKind.I4),
        "primitive:i4" => CliTypeIdentity.Primitive("i4", CliValueKind.I4),
        "primitive:u4" => CliTypeIdentity.Primitive("u4", CliValueKind.I4),
        "primitive:i8" => CliTypeIdentity.Primitive("i8", CliValueKind.I8),
        "primitive:u8" => CliTypeIdentity.Primitive("u8", CliValueKind.I8),
        "primitive:f4" => CliTypeIdentity.Primitive("f4", CliValueKind.F4),
        "primitive:f8" => CliTypeIdentity.Primitive("f8", CliValueKind.F8),
        "primitive:string" => CliTypeIdentity.Primitive(
            "string",
            CliValueKind.ManagedReference,
            isValueType: false),
        _ => throw new InvalidOperationException(canonicalName),
    };

    private static int ReadInt32(ImmutableArray<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan().Slice(offset));

    private static long ReadAddress(
        ImmutableArray<byte> data,
        int offset,
        WasmTargetLayout target) => target.AddressSize == sizeof(int)
        ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan().Slice(offset))
        : BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan().Slice(offset));

    private sealed class TypeDefinitions(params TypeDefinitionModel[] definitions) :
        ITypeDefinitionResolver
    {
        private readonly Dictionary<string, TypeDefinitionModel> _definitions =
            definitions.ToDictionary(
                definition => definition.FullName,
                StringComparer.Ordinal);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            _definitions[identity.FullName ?? identity.ElementType?.FullName ??
                identity.CanonicalName switch
                {
                    "primitive:i4" => "System.Int32",
                    _ => throw new InvalidOperationException(identity.CanonicalName),
                }];
    }

}
