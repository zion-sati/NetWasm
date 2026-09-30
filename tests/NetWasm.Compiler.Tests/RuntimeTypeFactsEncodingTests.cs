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
            ReadAddress(data, RuntimeTypeFactsEncoding.NameFactsOffset(target), target));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EncodesDemandedTypeNamesAtTargetWidth(WasmTarget targetKind)
    {
        var target = WasmTargetLayout.For(targetKind);
        var state = new ManagedStaticDataBuildState();
        state.Strings.Add("Box`1", new StringLayout(64, 5, 8));
        state.Strings.Add("Fixtures", new StringLayout(96, 8, 8));
        state.Strings.Add(
            "Fixtures.Box`1[[System.Int32, type-facts]]",
            new StringLayout(128, 30, 8));
        state.Strings.Add(
            "Fixtures.Box`1[System.Int32]",
            new StringLayout(160, 30, 8));

        var address = RuntimeTypeFactsEncoding.AddNames(
            state,
            target,
            new RuntimeTypeNames(
                RuntimeTypeNamePayload.Name |
                    RuntimeTypeNamePayload.Namespace |
                    RuntimeTypeNamePayload.FullName |
                    RuntimeTypeNamePayload.DisplayName,
                "Box`1",
                "Fixtures",
                "Fixtures.Box`1[[System.Int32, type-facts]]",
                "Fixtures.Box`1[System.Int32]"));

        var segment = Assert.Single(state.Segments);
        Assert.Equal(address, segment.Address);
        Assert.Equal(RuntimeTypeFactsEncoding.NamesSize(target), segment.Data.Length);
        Assert.Equal(
            (int)(RuntimeTypeNamePayload.Name |
                  RuntimeTypeNamePayload.Namespace |
                  RuntimeTypeNamePayload.FullName |
                  RuntimeTypeNamePayload.DisplayName),
            ReadInt32(segment.Data, RuntimeTypeFactsEncoding.NamePayloadOffset));
        Assert.Equal(
            64,
            ReadAddress(segment.Data, RuntimeTypeFactsEncoding.NameOffset(target), target));
        Assert.Equal(
            96,
            ReadAddress(
                segment.Data,
                RuntimeTypeFactsEncoding.NamespaceOffset(target),
                target));
        Assert.Equal(
            128,
            ReadAddress(
                segment.Data,
                RuntimeTypeFactsEncoding.FullNameOffset(target),
                target));
        Assert.Equal(
            160,
            ReadAddress(
                segment.Data,
                RuntimeTypeFactsEncoding.DisplayNameOffset(target),
                target));
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

    [Fact]
    public void TypeNameEncodingRejectsMissingStringStorageWithRuntimeContract()
    {
        var exception = Assert.Throws<CompilerException>(() =>
            RuntimeTypeFactsEncoding.AddNames(
                new ManagedStaticDataBuildState(),
                WasmTargetLayout.Wasm32,
                new RuntimeTypeNames(
                    RuntimeTypeNamePayload.Name,
                    "Missing",
                    null,
                    null,
                    null)));

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EncodesDemandedNullNamespaceWithoutAStringAddress(
        WasmTarget targetKind)
    {
        var target = WasmTargetLayout.For(targetKind);
        var state = new ManagedStaticDataBuildState();

        RuntimeTypeFactsEncoding.AddNames(
            state,
            target,
            new RuntimeTypeNames(
                RuntimeTypeNamePayload.Namespace,
                null,
                null,
                null,
                null));

        var data = Assert.Single(state.Segments).Data;
        Assert.Equal(
            (int)RuntimeTypeNamePayload.Namespace,
            ReadInt32(data, RuntimeTypeFactsEncoding.NamePayloadOffset));
        Assert.Equal(
            0,
            ReadAddress(data, RuntimeTypeFactsEncoding.NamespaceOffset(target), target));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void RejectsInvalidTypeNamePayloadWithRuntimeContract(int payload)
    {
        var exception = Assert.Throws<CompilerException>(() =>
            RuntimeTypeFactsEncoding.AddNames(
                new ManagedStaticDataBuildState(),
                WasmTargetLayout.Wasm32,
                new RuntimeTypeNames(
                    (RuntimeTypeNamePayload)payload,
                    null,
                    null,
                    null,
                    null)));

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
    }

    [Fact]
    public void FormatsNamedConstructedAndArrayTypeNamesWithoutRuntimeDiscovery()
    {
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "Fixtures",
            "Box`1",
            IsValueType: false,
            [],
            [])
        {
            GenericArity = 1,
            GenericParameterNames = ["T"],
        };
        var intDefinition = new TypeDefinitionModel(
            new EntityKey(Assembly, 2),
            "System",
            "Int32",
            IsValueType: true,
            [],
            []);
        var box = CliTypeIdentity.Named(
            Assembly,
            definition.Namespace,
            definition.Name,
            isValueType: false);
        var constructed = CliTypeIdentity.GenericInstantiation(
            box,
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var resolver = new TypeDefinitions(definition, intDefinition);

        Assert.Equal(
            new RuntimeTypeNames(
                RuntimeTypeNamePayload.Name |
                    RuntimeTypeNamePayload.Namespace |
                    RuntimeTypeNamePayload.FullName |
                    RuntimeTypeNamePayload.DisplayName,
                "Box`1[]",
                "Fixtures",
                "Fixtures.Box`1[[System.Int32, type-facts]][]",
                "Fixtures.Box`1[System.Int32][]"),
            RuntimeTypeNameFormatter.Format(
                CliTypeIdentity.SzArray(constructed),
                resolver,
                new AssemblyNames(),
                RuntimeTypeNamePayload.Name |
                    RuntimeTypeNamePayload.Namespace |
                    RuntimeTypeNamePayload.FullName |
                    RuntimeTypeNamePayload.DisplayName));
    }

    [Theory]
    [InlineData(RuntimeTypeNamePayload.Name)]
    [InlineData(RuntimeTypeNamePayload.Namespace)]
    [InlineData(RuntimeTypeNamePayload.FullName)]
    [InlineData(RuntimeTypeNamePayload.DisplayName)]
    public void FormatterRetainsOnlyTheDemandedNamePayload(
        RuntimeTypeNamePayload payload)
    {
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "Fixtures",
            "Sample",
            IsValueType: false,
            [],
            []);

        var names = RuntimeTypeNameFormatter.Format(
            CliTypeIdentity.Named(
                Assembly,
                definition.Namespace,
                definition.Name,
                isValueType: false),
            new TypeDefinitions(definition),
            new AssemblyNames(),
            payload);

        Assert.Equal(payload, names.Payload);
        Assert.Equal(
            payload == RuntimeTypeNamePayload.Name ? "Sample" : null,
            names.Name);
        Assert.Equal(
            payload == RuntimeTypeNamePayload.Namespace ? "Fixtures" : null,
            names.Namespace);
        Assert.Equal(
            payload == RuntimeTypeNamePayload.FullName ? "Fixtures.Sample" : null,
            names.FullName);
        Assert.Equal(
            payload == RuntimeTypeNamePayload.DisplayName ? "Fixtures.Sample" : null,
            names.DisplayName);
    }

    [Fact]
    public void FormatterIncludesOpenGenericParameterNamesOnlyInDisplayName()
    {
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "Fixtures",
            "Pair`2",
            IsValueType: false,
            [],
            [])
        {
            GenericArity = 2,
            GenericParameterNames = ["TLeft", "TRight"],
        };

        var names = RuntimeTypeNameFormatter.Format(
            CliTypeIdentity.Named(
                Assembly,
                definition.Namespace,
                definition.Name,
                isValueType: false),
            new TypeDefinitions(definition),
            new AssemblyNames(),
            RuntimeTypeNamePayload.Name |
                RuntimeTypeNamePayload.FullName |
                RuntimeTypeNamePayload.DisplayName);

        Assert.Equal("Pair`2", names.Name);
        Assert.Equal("Fixtures.Pair`2", names.FullName);
        Assert.Equal("Fixtures.Pair`2[TLeft,TRight]", names.DisplayName);
    }

    [Fact]
    public void FormatterRejectsInconsistentOpenGenericParameterNames()
    {
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "Fixtures",
            "Box`1",
            IsValueType: false,
            [],
            [])
        {
            GenericArity = 1,
        };

        var exception = Assert.Throws<CompilerException>(() =>
            RuntimeTypeNameFormatter.Format(
                CliTypeIdentity.Named(
                    Assembly,
                    definition.Namespace,
                    definition.Name,
                    isValueType: false),
                new TypeDefinitions(definition),
                new AssemblyNames(),
                RuntimeTypeNamePayload.DisplayName));

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
    }

    [Fact]
    public void FormatterUsesSimpleNestedNameAndQualifiedFullName()
    {
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "Fixtures",
            "Outer+Inner",
            IsValueType: false,
            [],
            []);

        Assert.Equal(
            new RuntimeTypeNames(
                RuntimeTypeNamePayload.Name |
                    RuntimeTypeNamePayload.Namespace |
                    RuntimeTypeNamePayload.FullName |
                    RuntimeTypeNamePayload.DisplayName,
                "Inner",
                "Fixtures",
                "Fixtures.Outer+Inner",
                "Fixtures.Outer+Inner"),
            RuntimeTypeNameFormatter.Format(
                CliTypeIdentity.Named(
                    Assembly,
                    definition.Namespace,
                    definition.Name,
                    isValueType: false),
                new TypeDefinitions(definition),
                new AssemblyNames(),
                RuntimeTypeNamePayload.Name |
                    RuntimeTypeNamePayload.Namespace |
                    RuntimeTypeNamePayload.FullName |
                    RuntimeTypeNamePayload.DisplayName));
    }

    [Theory]
    [InlineData(1, "Sample[*]")]
    [InlineData(2, "Sample[,]")]
    public void FormatterDistinguishesBoundedArraysFromVectors(
        int rank,
        string expectedName)
    {
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "Fixtures",
            "Sample",
            IsValueType: false,
            [],
            []);

        var names = RuntimeTypeNameFormatter.Format(
            CliTypeIdentity.Array(
                CliTypeIdentity.Named(
                    Assembly,
                    definition.Namespace,
                    definition.Name,
                    isValueType: false),
                rank),
            new TypeDefinitions(definition),
            new AssemblyNames(),
            RuntimeTypeNamePayload.Name);

        Assert.Equal(expectedName, names.Name);
    }

    [Theory]
    [InlineData(false, "Sample&")]
    [InlineData(true, "Sample*")]
    public void FormatterAppendsManagedAddressAndPointerSuffixes(
        bool unmanaged,
        string expectedName)
    {
        var definition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "Fixtures",
            "Sample",
            IsValueType: true,
            [],
            []);
        var element = CliTypeIdentity.Named(
            Assembly,
            definition.Namespace,
            definition.Name,
            isValueType: true);

        var names = RuntimeTypeNameFormatter.Format(
            unmanaged
                ? CliTypeIdentity.UnmanagedPointer(element)
                : CliTypeIdentity.ManagedByReference(element),
            new TypeDefinitions(definition),
            new AssemblyNames(),
            RuntimeTypeNamePayload.Name |
                RuntimeTypeNamePayload.FullName |
                RuntimeTypeNamePayload.DisplayName);

        Assert.Equal(expectedName, names.Name);
        Assert.Equal("Fixtures." + expectedName, names.FullName);
        Assert.Equal("Fixtures." + expectedName, names.DisplayName);
    }

    [Theory]
    [InlineData(false, "!0")]
    [InlineData(true, "!!0")]
    public void FormatterHandlesOpenGenericParameters(
        bool method,
        string expectedName)
    {
        var names = RuntimeTypeNameFormatter.Format(
            CliTypeIdentity.GenericParameter(method, 0),
            new TypeDefinitions(),
            new AssemblyNames(),
            RuntimeTypeNamePayload.Name |
                RuntimeTypeNamePayload.FullName |
                RuntimeTypeNamePayload.DisplayName);

        Assert.Equal(expectedName, names.Name);
        Assert.Null(names.FullName);
        Assert.Equal(expectedName, names.DisplayName);
    }

    [Fact]
    public void FormatterUsesArrayAssemblyForClosedGenericArgument()
    {
        var boxDefinition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "Fixtures",
            "Box`1",
            IsValueType: false,
            [],
            [])
        {
            GenericArity = 1,
        };
        var sampleDefinition = new TypeDefinitionModel(
            new EntityKey(Assembly, 2),
            "Fixtures",
            "Sample",
            IsValueType: false,
            [],
            []);
        var box = CliTypeIdentity.Named(
            Assembly,
            boxDefinition.Namespace,
            boxDefinition.Name,
            isValueType: false);
        var sample = CliTypeIdentity.Named(
            Assembly,
            sampleDefinition.Namespace,
            sampleDefinition.Name,
            isValueType: false);

        var names = RuntimeTypeNameFormatter.Format(
            CliTypeIdentity.GenericInstantiation(
                box,
                [CliTypeIdentity.SzArray(sample)]),
            new TypeDefinitions(boxDefinition, sampleDefinition),
            new AssemblyNames(),
            RuntimeTypeNamePayload.FullName);

        Assert.Equal(
            "Fixtures.Box`1[[Fixtures.Sample[], type-facts]]",
            names.FullName);
    }

    [Fact]
    public void FormatterReturnsNullFullNameForOpenConstructedType()
    {
        var boxDefinition = new TypeDefinitionModel(
            new EntityKey(Assembly, 1),
            "Fixtures",
            "Box`1",
            IsValueType: false,
            [],
            [])
        {
            GenericArity = 1,
        };
        var box = CliTypeIdentity.Named(
            Assembly,
            boxDefinition.Namespace,
            boxDefinition.Name,
            isValueType: false);

        var names = RuntimeTypeNameFormatter.Format(
            CliTypeIdentity.GenericInstantiation(
                box,
                [CliTypeIdentity.GenericParameter(method: false, 0)]),
            new TypeDefinitions(boxDefinition),
            new AssemblyNames(),
            RuntimeTypeNamePayload.FullName |
                RuntimeTypeNamePayload.DisplayName);

        Assert.Null(names.FullName);
        Assert.Equal("Fixtures.Box`1[!0]", names.DisplayName);
    }

    [Fact]
    public void FormatterAppendsShapeToOpenGenericParameterWithoutFullName()
    {
        var names = RuntimeTypeNameFormatter.Format(
            CliTypeIdentity.SzArray(
                CliTypeIdentity.GenericParameter(method: false, 0)),
            new TypeDefinitions(),
            new AssemblyNames(),
            RuntimeTypeNamePayload.Name |
                RuntimeTypeNamePayload.FullName |
                RuntimeTypeNamePayload.DisplayName);

        Assert.Equal("!0[]", names.Name);
        Assert.Null(names.FullName);
        Assert.Equal("!0[]", names.DisplayName);
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

    private sealed class AssemblyNames : IAssemblyIdentityFormatter
    {
        public string Format(AssemblyIdentity identity) => identity.Name;
    }
}
