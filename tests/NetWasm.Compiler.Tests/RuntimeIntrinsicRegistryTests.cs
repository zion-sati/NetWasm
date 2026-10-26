using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.TestInfrastructure;

using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Tests;

public sealed class RuntimeIntrinsicRegistryTests
{
    [Theory]
    [InlineData("System.Math", CliValueKind.F8)]
    [InlineData("System.MathF", CliValueKind.F4)]
    public void RegistryRecognizesExactCopySignSignature(string name, CliValueKind kind)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var type = new EntityKey(assembly, 1);
        var method = new MethodDefinitionModel(new(assembly, 2), type, "CopySign", true,
            MethodSignatureModel.Create(kind, kind, kind), 0);
        var registry = new RuntimeIntrinsicRegistry(new DictionarySymbolFormatter((type, name)), [method]);

        AssertIntrinsic(registry, method.Key, RuntimeIntrinsic.FloatingCopySign);
    }

    [Theory]
    [InlineData("System.Math", CliValueKind.F8)]
    [InlineData("System.MathF", CliValueKind.F4)]
    public void RegistryLeavesNonmatchingCopySignSignaturesManaged(string name, CliValueKind kind)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var type = new EntityKey(assembly, 1);
        var method = new MethodDefinitionModel(new(assembly, 2), type, "CopySign", true,
            MethodSignatureModel.Create(kind, kind, kind), 0);
        var other = kind == CliValueKind.F4 ? CliValueKind.F8 : CliValueKind.F4;
        var invalid = new[]
        {
            method with { IsStatic = false },
            method with { GenericArity = 1 },
            method with { Signature = MethodSignatureModel.Create(other, kind, kind) },
            method with { Signature = MethodSignatureModel.Create(kind) },
            method with { Signature = MethodSignatureModel.Create(kind, kind) },
            method with { Signature = MethodSignatureModel.Create(kind, kind, kind, kind) },
            method with { Signature = MethodSignatureModel.Create(kind, other, kind) },
            method with { Signature = MethodSignatureModel.Create(kind, kind, other) },
            method with { Signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4, CliValueKind.I4) },
        };
        foreach (var candidate in invalid)
        {
            var registry = new RuntimeIntrinsicRegistry(
                new DictionarySymbolFormatter((type, name)), [candidate]);

            Assert.False(((IRuntimeIntrinsicRegistry)registry).TryGetIntrinsic(candidate.Key, out _));
        }
    }

    [Theory]
    [InlineData("System.SByte", "i1", CliValueKind.I4)]
    [InlineData("System.Byte", "u1", CliValueKind.I4)]
    [InlineData("System.Math", "u8", CliValueKind.I8)]
    [InlineData("System.Char", "char", CliValueKind.I4)]
    [InlineData("System.Int16", "i2", CliValueKind.I4)]
    [InlineData("System.UInt16", "u2", CliValueKind.I4)]
    [InlineData("System.Int32", "i4", CliValueKind.I4)]
    [InlineData("System.UInt32", "u4", CliValueKind.I4)]
    [InlineData("System.Int64", "i8", CliValueKind.I8)]
    [InlineData("System.IntPtr", "nativeint", CliValueKind.NativeInt)]
    [InlineData("System.UIntPtr", "nativeuint", CliValueKind.NativeInt)]
    public void RegistryRecognizesIntegralTupleDivRemContracts(
        string declaringTypeName,
        string primitiveName,
        CliValueKind stackKind)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringType = new EntityKey(assembly, 1);
        var operand = CliTypeIdentity.Primitive(primitiveName, stackKind);
        var tuple = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(
                assembly,
                "System",
                "ValueTuple`2",
                isValueType: true),
            [operand, operand]);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            declaringType,
            "DivRem",
            true,
            MethodSignatureModel.Create(tuple, operand, operand),
            0);

        AssertIntrinsic(
            new RuntimeIntrinsicRegistry(
                new DictionarySymbolFormatter((declaringType, declaringTypeName)),
                [method]),
            method.Key,
            RuntimeIntrinsic.IntegerDivRem);
    }

    [Fact]
    public void RegistryDoesNotIntrinsicizeLegacyOutParameterDivRem()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringType = new EntityKey(assembly, 1);
        var operand = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            declaringType,
            "DivRem",
            true,
            MethodSignatureModel.Create(
                operand,
                operand,
                operand,
                CliTypeIdentity.ManagedByReference(operand)),
            0);
        var registry = new RuntimeIntrinsicRegistry(
            new DictionarySymbolFormatter((declaringType, "System.Math")),
            [method]);

        Assert.False(registry.TryGetIntrinsic(method.Key, out _));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void RegistryRejectsDivRemWithMismatchedTupleElements(
        bool quotientMatches,
        bool remainderMatches)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringType = new EntityKey(assembly, 1);
        var operand = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var other = CliTypeIdentity.FromStackKind(CliValueKind.I8);
        var tuple = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(
                assembly,
                "System",
                "ValueTuple`2",
                isValueType: true),
            [quotientMatches ? operand : other,
                remainderMatches ? operand : other]);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            declaringType,
            "DivRem",
            true,
            MethodSignatureModel.Create(tuple, operand, operand),
            0);
        var registry = new RuntimeIntrinsicRegistry(
            new DictionarySymbolFormatter((declaringType, "System.Int32")),
            [method]);

        Assert.False(registry.TryGetIntrinsic(method.Key, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EnumMetadataLookupAcceptsOnlyItsSupportedGenericArities(int arity)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var type = new EntityKey(assembly, 1);
        var method = new MethodDefinitionModel(new(assembly, 2), type, "InternalGetMetadata", true,
            MethodSignatureModel.Create(CliValueKind.NativeInt), 0)
        { GenericArity = arity };
        var registry = Assert.IsAssignableFrom<IRuntimeIntrinsicRegistry>(new RuntimeIntrinsicRegistry(
            new DictionarySymbolFormatter((type, "System.Enum")), [method]));

        var recognized = registry.TryGetIntrinsic(method.Key, out var intrinsic);

        Assert.Equal(arity is 0 or 1, recognized);
        if (recognized) Assert.Equal(RuntimeIntrinsic.EnumGetMetadata, intrinsic);
    }

    [Theory]
    [InlineData(
        "System.Runtime.CompilerServices.ObjectArrayDelegateAdapter",
        "Create",
        RuntimeIntrinsic.ObjectArrayDelegateAdapterCreate)]
    [InlineData(
        "System.Runtime.CompilerServices.RuntimeMemberExecution",
        "InvokeMethod",
        RuntimeIntrinsic.MemberExecuteMethod)]
    [InlineData(
        "System.Runtime.CompilerServices.RuntimeMemberExecution",
        "ReadField",
        RuntimeIntrinsic.MemberReadField)]
    public void RegistryRecognizesBoundedExpressionExecutionContracts(
        string typeName,
        string methodName,
        RuntimeIntrinsic expected)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringType = new EntityKey(assembly, 1);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            declaringType,
            methodName,
            true,
            MethodSignatureModel.Create(CliValueKind.ManagedReference),
            0);

        AssertIntrinsic(
            new RuntimeIntrinsicRegistry(
                new DictionarySymbolFormatter((declaringType, typeName)),
                [method]),
            method.Key,
            expected);
    }

    [Theory]
    [InlineData(
        "System.Runtime.CompilerServices.RuntimeHelpersRuntime",
        "CloneBoxedValue",
        RuntimeIntrinsic.BoxedValueClone)]
    [InlineData(
        "System.Runtime.CompilerServices.RuntimeHelpersRuntime",
        "AreEqual",
        RuntimeIntrinsic.RuntimeHelpersEquals)]
    [InlineData("System.DelegateRuntime", "Clone", RuntimeIntrinsic.DelegateClone)]
    public void RegistryRecognizesRuntimeSupportContracts(
        string declaringTypeName,
        string methodName,
        RuntimeIntrinsic expected)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringType = new EntityKey(assembly, 1);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            declaringType,
            methodName,
            true,
            MethodSignatureModel.Create(CliValueKind.ManagedReference),
            0);

        AssertIntrinsic(
            new RuntimeIntrinsicRegistry(
                new DictionarySymbolFormatter((declaringType,
                    declaringTypeName)),
                [method]),
            method.Key,
            expected);
    }

    [Fact]
    public void RegistryCanBeProvedThroughItsLookupContractInMemory()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringType = new EntityKey(assembly, 1);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            declaringType,
            "get_Length",
            true,
            MethodSignatureModel.Create(CliValueKind.I4),
            0);
        AssertIntrinsic(
            new RuntimeIntrinsicRegistry(
                new DictionarySymbolFormatter((declaringType, "System.String")),
                [method]),
            method.Key,
            RuntimeIntrinsic.StringLength);
    }

    [Fact]
    public void RegistryRecognizesRawStringDataReferenceContract()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringType = new EntityKey(assembly, 1);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            declaringType,
            "GetRawStringData",
            true,
            MethodSignatureModel.Create(CliValueKind.ManagedAddress),
            0);

        AssertIntrinsic(
            new RuntimeIntrinsicRegistry(
                new DictionarySymbolFormatter((declaringType, "System.String")),
                [method]),
            method.Key,
            RuntimeIntrinsic.StringGetRawDataReference);
    }

    [Fact]
    public void RegistryRecognizesOrdinalStringEqualityHelperContract()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringType = new EntityKey(assembly, 1);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            declaringType,
            "EqualsHelper",
            true,
            MethodSignatureModel.Create(
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.ManagedReference),
            0);

        AssertIntrinsic(
            new RuntimeIntrinsicRegistry(
                new DictionarySymbolFormatter((declaringType, "System.String")),
                [method]),
            method.Key,
            RuntimeIntrinsic.StringEqualsOrdinal);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void RegistryRejectsMalformedOrdinalStringEqualityHelpers(
        bool isStatic,
        bool parametersMatch,
        bool returnMatches)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringType = new EntityKey(assembly, 1);
        var parameterTypes = parametersMatch
            ? new[] { CliValueKind.ManagedReference, CliValueKind.ManagedReference }
            : [CliValueKind.ManagedReference];
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            declaringType,
            "EqualsHelper",
            isStatic,
            MethodSignatureModel.Create(
                returnMatches ? CliValueKind.I4 : CliValueKind.I8,
                parameterTypes),
            0);
        var registry = new RuntimeIntrinsicRegistry(
            new DictionarySymbolFormatter((declaringType, "System.String")),
            [method]);

        Assert.False(registry.TryGetIntrinsic(method.Key, out _));
    }

    [Fact]
    public void RegistryRejectsUnsupportedEnumParseGenericArities()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringType = new EntityKey(assembly, 0x02000001);
        var methods = new[]
        {
            new MethodDefinitionModel(
                new EntityKey(assembly, 0x06000001),
                declaringType,
                "InternalParse",
                true,
                MethodSignatureModel.Create(CliValueKind.ManagedReference),
                0)
            {
                GenericArity = 2,
            },
            new MethodDefinitionModel(
                new EntityKey(assembly, 0x06000002),
                declaringType,
                "InternalTryParse",
                true,
                MethodSignatureModel.Create(CliValueKind.I4),
                0)
            {
                GenericArity = 2,
            },
        };
        var registry = new RuntimeIntrinsicRegistry(
            new DictionarySymbolFormatter((declaringType, "System.Enum")),
            methods);

        Assert.All(methods, method => Assert.False(registry.TryGetIntrinsic(method.Key, out _)));
    }

    [Fact]
    public void RegistryRecognizesArrayLengthContract()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringType = new EntityKey(assembly, 3);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 4),
            declaringType,
            "get_Length",
            false,
            MethodSignatureModel.Create(CliValueKind.I4),
            0);

        AssertIntrinsic(
            new RuntimeIntrinsicRegistry(
                new DictionarySymbolFormatter((declaringType, "System.Array")),
                [method]),
            method.Key,
            RuntimeIntrinsic.ArrayLength);
    }

    [Fact]
    public void RegistryDoesNotTreatConsoleOutputAsACompilerIntrinsic()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringType = new EntityKey(assembly, 7);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 8),
            declaringType,
            "WriteLine",
            true,
            MethodSignatureModel.Create(
                CliValueKind.Void,
                CliValueKind.I4),
            0);
        var registry = new RuntimeIntrinsicRegistry(
            new DictionarySymbolFormatter((declaringType, "System.Console")),
            [method]);

        Assert.False(registry.TryGetIntrinsic(method.Key, out _));
    }

    [Theory]
    [InlineData("InternalCopy", RuntimeIntrinsic.ArrayCopy)]
    [InlineData("InternalClear", RuntimeIntrinsic.ArrayClear)]
    [InlineData("InternalClone", RuntimeIntrinsic.ArrayClone)]
    [InlineData("InternalCloneRange", RuntimeIntrinsic.ArrayCloneRange)]
    [InlineData("GetLowerBound", RuntimeIntrinsic.ArrayGetLowerBound)]
    [InlineData("InternalGetValue", RuntimeIntrinsic.ArrayGetValue)]
    public void RegistryRecognizesNonGenericArrayRuntimeContracts(
        string methodName,
        RuntimeIntrinsic expected)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringType = new EntityKey(assembly, 5);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 6),
            declaringType,
            methodName,
            true,
            MethodSignatureModel.Create(CliValueKind.I4),
            0);

        AssertIntrinsic(
            new RuntimeIntrinsicRegistry(
                new DictionarySymbolFormatter((declaringType, "System.Array")),
                [method]),
            method.Key,
            expected);
    }

    [Theory]
    [InlineData("Clear", RuntimeIntrinsic.NativeMemoryClear)]
    [InlineData("Copy", RuntimeIntrinsic.NativeMemoryCopy)]
    [InlineData("Fill", RuntimeIntrinsic.NativeMemoryFill)]
    public void RegistryRecognizesNativeMemoryBulkContracts(
        string methodName,
        RuntimeIntrinsic expected)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringType = new EntityKey(assembly, 9);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 10),
            declaringType,
            methodName,
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            0);

        AssertIntrinsic(
            new RuntimeIntrinsicRegistry(
                new DictionarySymbolFormatter((declaringType,
                    "System.Runtime.InteropServices.NativeMemory")),
                [method]),
            method.Key,
            expected);
    }

    [Fact]
    public void IntrinsicRegistryRecognizesOnlyDeclaredRuntimeContracts()
    {
        using var assets = TestAssets.Create();
        using var compilation = MetadataCompilationTestFactory.Load(
            assets.Application,
            [assets.Library, assets.CoreLib]);
        var snapshot = compilation.Snapshot;
        var methodRepository = MetadataCompilationTestActors.Methods(snapshot);
        var symbols = MetadataCompilationTestActors.Symbols(snapshot);
        var methods = snapshot.Methods.ToArray();
        var registry = new RuntimeIntrinsicRegistry(symbols, methods);
        var stringType = snapshot.Types
            .Single(type => type.FullName == "System.String");
        var length = stringType.Methods
            .Select(methodRepository.GetMethod)
            .Single(method => method.Name == "get_Length");
        var chars = stringType.Methods
            .Select(methodRepository.GetMethod)
            .Single(method =>
                method.Name == "get_Chars" &&
                method.Signature.ParameterTypes.AsSpan().SequenceEqual(
                    [CliValueKind.I4]));
        var constructors = stringType.Methods
            .Select(methodRepository.GetMethod)
            .Where(method => method.Name == ".ctor")
            .ToArray();
        var runtimeHelpers = snapshot.Types
            .Single(type => type.FullName ==
                "System.Runtime.CompilerServices.RuntimeHelpers");
        var containsReferences = runtimeHelpers.Methods
            .Select(methodRepository.GetMethod)
            .Single(method => method.Name == "IsReferenceOrContainsReferences");
        var canonicalAbi = snapshot.Types
            .Single(type => type.FullName ==
                "System.Runtime.InteropServices.WebAssembly.CanonicalAbi");
        var resourceIntrinsics = canonicalAbi.Methods
            .Select(methodRepository.GetMethod)
            .Where(method => method.Name is
                "CreateResourceHandle" or
                "GetResourceHandle" or
                "ReleaseResourceHandle")
            .ToDictionary(method => method.Name);
        var taskDiagnostics = snapshot.Types
            .Single(type => type.FullName == "System.Threading.Tasks.TaskDiagnostics")
            .Methods
            .Select(methodRepository.GetMethod)
            .Single(method => method.Name == "ReportUnobservedException");

        Assert.True(registry.TryGetIntrinsic(length.Key, out var lengthIntrinsic));
        Assert.Equal(RuntimeIntrinsic.StringLength, lengthIntrinsic);
        Assert.True(registry.TryGetIntrinsic(chars.Key, out var charsIntrinsic));
        Assert.Equal(RuntimeIntrinsic.StringCharacterAt, charsIntrinsic);
        Assert.True(registry.TryGetIntrinsic(
            containsReferences.Key, out var containsReferencesIntrinsic));
        Assert.Equal(
            RuntimeIntrinsic.IsReferenceOrContainsReferences,
            containsReferencesIntrinsic);
        Assert.True(registry.TryGetIntrinsic(
            taskDiagnostics.Key,
            out var taskDiagnosticIntrinsic));
        Assert.Equal(
            RuntimeIntrinsic.ReportUnobservedTaskException,
            taskDiagnosticIntrinsic);
        AssertIntrinsic(
            "CreateResourceHandle",
            RuntimeIntrinsic.ComponentResourceHandleCreate);
        AssertIntrinsic(
            "GetResourceHandle",
            RuntimeIntrinsic.ComponentResourceHandleGet);
        AssertIntrinsic(
            "ReleaseResourceHandle",
            RuntimeIntrinsic.ComponentResourceHandleRelease);
        Assert.NotEmpty(constructors);
        Assert.All(constructors, constructor =>
            Assert.False(registry.TryGetIntrinsic(constructor.Key, out _)));
        Assert.Throws<ArgumentNullException>(
            () => new RuntimeIntrinsicRegistry(null!, methods));
        Assert.Throws<ArgumentNullException>(
            () => new RuntimeIntrinsicRegistry(symbols, null!));

        void AssertIntrinsic(string methodName, RuntimeIntrinsic expected)
        {
            Assert.True(registry.TryGetIntrinsic(
                resourceIntrinsics[methodName].Key,
                out var actual));
            Assert.Equal(expected, actual);
        }


    }

    private sealed class DictionarySymbolFormatter(
        params (EntityKey Key, string Name)[] entries) : ISymbolFormatter
    {
        private readonly Dictionary<EntityKey, string> _names =
            entries.ToDictionary(entry => entry.Key, entry => entry.Name);

        public string Format(EntityKey key) => _names[key];

        public string Format(MethodDefinitionModel method) =>
            _names[method.DeclaringType] + "::" + method.Name;
    }

    private static void AssertIntrinsic<TRegistry>(
        TRegistry registry,
        EntityKey method,
        RuntimeIntrinsic expected)
        where TRegistry : IRuntimeIntrinsicRegistry
    {
        Assert.True(registry.TryGetIntrinsic(method, out var actual));
        Assert.Equal(expected, actual);
    }

}
