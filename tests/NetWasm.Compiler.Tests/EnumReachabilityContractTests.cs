using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class EnumReachabilityContractTests
{
    [Fact]
    public void GetValuesAsUnderlyingTypeRetainsMetadataWhileDerivedRootPlannerOwnsArrays()
    {
        var underlying = CliTypeIdentity.Primitive("u2", CliValueKind.I4);
        var enumType = CliTypeIdentity.Named(
            Assembly,
            "Fixtures",
            "State",
            true,
            CliValueKind.I4);
        var method = Method("InternalGetValuesAsUnderlyingType", RuntimeIntrinsic.EnumGetValues);
        var analysis = CreateClassifier(method, enumType, underlying).Classify(
            Request(method, enumType));

        Assert.Empty(analysis.ConstructedTypes);
        Assert.Contains(new EntityKey(Assembly, 0x02000002), analysis.Types);
        Assert.DoesNotContain(StringType, analysis.AllocatedTypes);
        Assert.DoesNotContain(StringDefinition.Key, analysis.Types);
        Assert.Equal(new EnumMetadataRequirement(new EntityKey(Assembly, 0x02000002),
            EnumMetadataPayload.Values), Assert.Single(analysis.EnumMetadataRequirements));
    }

    [Fact]
    public void GetValuesAsUnderlyingTypeWithoutAClosedEnumDoesNotInventAnArray()
    {
        var enumType = CliTypeIdentity.Named(
            Assembly,
            "Fixtures",
            "State",
            true,
            CliValueKind.I4);
        var method = Method("InternalGetValuesAsUnderlyingType", RuntimeIntrinsic.EnumGetValues);
        var analysis = CreateClassifier(
            method,
            enumType,
            CliTypeIdentity.Primitive("u2", CliValueKind.I4)).Classify(
                Request(method, enumType, []));

        Assert.Empty(analysis.ConstructedTypes);
        Assert.Equal(new EnumMetadataRequirement(null, EnumMetadataPayload.Values),
            Assert.Single(analysis.EnumMetadataRequirements));
    }

    [Fact]
    public void GenericClosedNestedEnumRetainsItsDefinitionPayload()
    {
        var definition = CliTypeIdentity.Named(Assembly, "Fixtures", "Outer`1+State", true, CliValueKind.I4);
        var closed = CliTypeIdentity.GenericInstantiation(definition, [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);
        var open = CliTypeIdentity.GenericInstantiation(definition, [CliTypeIdentity.GenericParameter(method: false, 0)]);
        var method = Method("InternalGetValues", RuntimeIntrinsic.EnumGetValues);
        var classifier = CreateClassifier(method, closed, CliTypeIdentity.Primitive("u2", CliValueKind.I4));

        var analysis = classifier.Classify(Request(method, closed));
        var openAnalysis = classifier.Classify(Request(method, open, [open]));

        var definitionKey = new EntityKey(Assembly, 0x02000002);
        Assert.Contains(definitionKey, analysis.Types);
        Assert.Equal(new EnumMetadataRequirement(definitionKey, EnumMetadataPayload.Values),
            Assert.Single(analysis.EnumMetadataRequirements));
        Assert.Empty(analysis.ConstructedTypes);
        Assert.Empty(openAnalysis.EnumMetadataRequirements);
    }

    [Fact]
    public void TypeBasedGetValuesRootsOpenDefinitionRejection()
    {
        var method = Method("InternalGetValues", RuntimeIntrinsic.EnumGetValues);
        var classifier = CreateClassifier(method, EnumType, CliTypeIdentity.FromStackKind(CliValueKind.I4));

        var typeBased = classifier.Classify(Request(method, EnumType, []));
        var closedGeneric = classifier.Classify(Request(method, EnumType, [EnumType]));

        var exception = Assert.Single(typeBased.Exceptions);
        Assert.Equal(ManagedExceptionKind.NotSupported, exception.Kind);
        Assert.Equal("System.NotSupportedException", exception.TypeName);
        Assert.Empty(closedGeneric.Exceptions);
    }

    [Fact]
    public void GenericEnumCallsDoNotTreatNonEnumArgumentsAsEnumMetadata()
    {
        var enumType = CliTypeIdentity.Named(
            Assembly,
            "Fixtures",
            "State",
            true,
            CliValueKind.I4);
        var nonEnum = CliTypeIdentity.Named(
            Assembly,
            "Fixtures",
            "NotAnEnum",
            true,
            CliValueKind.I4);
        var method = Method("InternalGetName", RuntimeIntrinsic.EnumGetName);
        var classifier = CreateClassifier(
            method,
            enumType,
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));

        var namedResult = classifier.Classify(Request(method, enumType, [nonEnum]));
        var primitiveResult = classifier.Classify(Request(
            method,
            enumType,
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]));

        Assert.DoesNotContain(new EntityKey(Assembly, 0x02000002), namedResult.Types);
        Assert.DoesNotContain(new EntityKey(Assembly, 0x02000002), primitiveResult.Types);
        Assert.Empty(namedResult.EnumMetadataRequirements);
        Assert.Empty(primitiveResult.EnumMetadataRequirements);
    }

    [Fact]
    public void MetadataLookupRequestsOnlyTheManagedAlgorithmPayload()
    {
        var enumType = CliTypeIdentity.Named(
            Assembly,
            "Fixtures",
            "State",
            true,
            CliValueKind.I4);
        var method = Method("InternalGetMetadata", RuntimeIntrinsic.EnumGetMetadata);
        var analysis = CreateClassifier(
            method,
            enumType,
            CliTypeIdentity.Primitive("i4", CliValueKind.I4)).Classify(
                Request(method, enumType, [enumType]));

        Assert.Equal(
            EnumMetadataPayload.Names | EnumMetadataPayload.Values |
            EnumMetadataPayload.RuntimeDescriptor,
            Assert.Single(analysis.EnumMetadataRequirements).Payload);
        Assert.Empty(analysis.Exceptions);
    }

    [Fact]
    public void CompareToRequiresArgumentExceptionSupport()
    {
        var enumType = CliTypeIdentity.Named(
            Assembly,
            "Fixtures",
            "State",
            true,
            CliValueKind.I4);
        var method = Method("InternalCompareTo", RuntimeIntrinsic.EnumCompareTo);
        var analysis = CreateClassifier(
            method,
            enumType,
            CliTypeIdentity.Primitive("i4", CliValueKind.I4)).Classify(
                Request(method, enumType, [enumType]));

        var argument = Assert.Single(analysis.Exceptions);
        Assert.Equal(ManagedExceptionKind.Argument, argument.Kind);
        Assert.Equal("System.ArgumentException", argument.TypeName);
    }

    [Theory]
    [InlineData("InternalToString", true)]
    [InlineData("InternalFormat", true)]
    [InlineData("InternalToInt32", false)]
    public void ClassifierSelectsManagedFormattingOnlyForFormattingOperations(string name, bool formats)
    {
        var method = Method(name, RuntimeIntrinsic.EnumConvert);
        var classifier = Assert.IsAssignableFrom<IReachabilityImportClassifier>(CreateClassifier(
            method, EnumType, CliTypeIdentity.FromStackKind(CliValueKind.I4)));

        var result = classifier.Classify(Request(method, EnumType));

        if (formats)
            Assert.Equal(EnumFormatKey, Assert.Single(result.EnqueuedMethods).Definition.Key);
        else
        {
            Assert.Empty(result.EnqueuedMethods);
            Assert.Contains(result.Exceptions, exception => exception.Kind == ManagedExceptionKind.InvalidCast);
        }
    }

    [Fact]
    public void ClassifierRejectsMissingRequirementPolicy()
    {
        var error = Assert.Throws<ArgumentNullException>(() => new ReachabilityImportClassifier(
            new EnumTypeFinder(), new EnumTypeResolver(EnumType, EnumType),
            new EnumMethodRepository(), new UnusedDelegateRecognizer(),
            new NoAsyncBindingResolver(), new SingleIntrinsicRegistry(default, ""),
            new EnumSymbolFormatter(), null!, new NetWasm.Compiler.Core.NativeInterop.NativeDeclarationValidator()));

        Assert.Equal("enumMetadataRequirements", error.ParamName);
    }

    private static ReachabilityImportClassifier CreateClassifier(
        MethodDefinitionModel method,
        CliTypeIdentity enumType,
        CliTypeIdentity underlying) =>
        new(
            new EnumTypeFinder(),
            new EnumTypeResolver(enumType, underlying),
            new EnumMethodRepository(),
            new UnusedDelegateRecognizer(),
            new NoAsyncBindingResolver(),
            new SingleIntrinsicRegistry(method.Key, method.Name),
            new EnumSymbolFormatter(),
            new EnumMetadataRequirementClassifier(),
            new NetWasm.Compiler.Core.NativeInterop.NativeDeclarationValidator());

    private static ReachabilityImportRequest Request(
        MethodDefinitionModel method,
        CliTypeIdentity enumType,
        ImmutableArray<CliTypeIdentity> methodArguments = default) =>
        new(new MethodInstanceModel(
            method,
            EnumType,
            methodArguments.IsDefault ? [enumType] : methodArguments,
            method.Signature));

    private static MethodDefinitionModel Method(
        string name,
        RuntimeIntrinsic intrinsic) =>
        new(
            new EntityKey(Assembly, NextToken()),
            EnumDeclaringType,
            name,
            true,
            MethodSignatureModel.Create(CliValueKind.ManagedReference),
            0);

    private static int _nextToken = 0x06000001;
    private static int NextToken() => _nextToken++;

    private static readonly AssemblyIdentity Assembly = new("enum-reachability");
    private static readonly EntityKey EnumDeclaringType = new(Assembly, 0x02000001);
    private static readonly CliTypeIdentity EnumType = CliTypeIdentity.Named(
        Assembly,
        "System",
        "Enum",
        false);
    private static readonly TypeDefinitionModel StringDefinition = new(
        new EntityKey(Assembly, 0x02000003),
        "System",
        "String",
        false,
        [],
        []);
    private static readonly CliTypeIdentity StringType = CliTypeIdentity.Named(
        Assembly,
        "System",
        "String",
        false);
    private static readonly EntityKey EnumAlgorithmsKey =
        new(Assembly, 0x02000004);
    private static readonly EntityKey EnumFormatKey =
        new(Assembly, 0x06000080);
    private static readonly MethodDefinitionModel EnumFormatMethod = new(
        EnumFormatKey,
        EnumAlgorithmsKey,
        "Format",
        true,
        MethodSignatureModel.Create(
            CliValueKind.ManagedReference,
            CliValueKind.NativeInt,
            CliValueKind.I8,
            CliValueKind.ManagedReference),
        0);
    private static readonly ImmutableArray<MethodDefinitionModel> OtherAlgorithmMethods =
    [
        EnumFormatMethod with { Key = new(Assembly, 0x06000081), Name = "Other" },
        EnumFormatMethod with { Key = new(Assembly, 0x06000082), IsStatic = false },
        EnumFormatMethod with { Key = new(Assembly, 0x06000083), Signature = MethodSignatureModel.Create(CliValueKind.Void) },
        EnumFormatMethod with
        {
            Key = new(Assembly, 0x06000084),
            Signature = MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4, CliValueKind.I8, CliValueKind.ManagedReference),
        },
    ];

    private static readonly TypeDefinitionModel EnumAlgorithmsDefinition = new(
        EnumAlgorithmsKey,
        "System",
        "EnumAlgorithms",
        false,
        [],
        [.. OtherAlgorithmMethods.Select(method => method.Key), EnumFormatKey]);

    private sealed class SingleIntrinsicRegistry(EntityKey method, string methodName) :
        IRuntimeIntrinsicRegistry
    {
        public bool TryGetIntrinsic(EntityKey candidate, out RuntimeIntrinsic intrinsic)
        {
            intrinsic = methodName switch
            {
                "InternalGetValuesAsUnderlyingType" or "InternalGetValues" => RuntimeIntrinsic.EnumGetValues,
                "InternalGetName" => RuntimeIntrinsic.EnumGetName,
                "InternalGetMetadata" => RuntimeIntrinsic.EnumGetMetadata,
                "InternalCompareTo" => RuntimeIntrinsic.EnumCompareTo,
                "InternalToInt32" => RuntimeIntrinsic.EnumConvert,
                "InternalToString" => RuntimeIntrinsic.EnumToString,
                "InternalFormat" => RuntimeIntrinsic.EnumFormat,
                _ => default,
            };
            return candidate == method;
        }
    }

    private sealed class EnumTypeResolver(
        CliTypeIdentity enumType,
        CliTypeIdentity underlying) : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            new(
                new EntityKey(Assembly, 0x02000002),
                "Fixtures",
                "State",
                true,
                [],
                [])
            {
                IsEnum = identity == enumType,
                EnumUnderlyingType = underlying,
            };
    }

    private sealed class EnumSymbolFormatter : ISymbolFormatter
    {
        public string Format(EntityKey key) => key == EnumDeclaringType
            ? "System.Enum"
            : key.ToString();

        public string Format(MethodDefinitionModel method) => method.Name;
    }

    private sealed class EnumTypeFinder : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName)
        {
            return fullName switch
            {
                "System.String" => StringDefinition,
                "System.EnumAlgorithms" => EnumAlgorithmsDefinition,
                _ => throw new InvalidOperationException(fullName),
            };
        }
    }

    private sealed class EnumMethodRepository : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) =>
            key == EnumFormatKey ? EnumFormatMethod : OtherAlgorithmMethods.Single(method => method.Key == key);
    }

    private sealed class UnusedDelegateRecognizer : IDelegateTypeRecognizer
    {
        public bool Recognize(CliTypeIdentity type) => false;
    }

    private sealed class NoAsyncBindingResolver : IJavaScriptAsyncBindingResolver
    {
        public JavaScriptAsyncMethodBinding? Resolve(MethodDefinitionModel method) => null;
    }
}
