using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class RuntimeIntrinsicExceptionRequirementProviderTests
{
    private static readonly AssemblyIdentity Assembly = new("in-memory");

    [Theory]
    [MemberData(nameof(IntrinsicCases))]
    public void ProviderDeclaresEveryIntrinsicLoweringException(
        RuntimeIntrinsic intrinsic,
        string methodName,
        int genericArity,
        ManagedExceptionKind[] expected)
    {
        var method = Method(methodName, genericArity);
        var provider = Assert.IsAssignableFrom<
            IRuntimeIntrinsicExceptionRequirementProvider>(
                new RuntimeIntrinsicExceptionRequirementProvider(
                    new FixedRegistry(method.Key, intrinsic)));

        var actual = provider.Discover(method);

        Assert.Equal(expected, actual.Select(requirement => requirement.Kind));
        Assert.All(actual, requirement => Assert.Equal(
            ExpectedTypeName(requirement.Kind),
            requirement.TypeName));
    }

    [Fact]
    public void ProviderReturnsNoRequirementsForAnOrdinaryMethod()
    {
        var provider = Assert.IsAssignableFrom<
            IRuntimeIntrinsicExceptionRequirementProvider>(
                new RuntimeIntrinsicExceptionRequirementProvider(new EmptyRegistry()));

        Assert.Empty(provider.Discover(Method("Run", 0)));
    }

    [Fact]
    public void UnsignedDivRemRequiresOnlyDivideByZero()
    {
        var operand = CliTypeIdentity.Primitive("u8", CliValueKind.I8);
        var method = new MethodDefinitionModel(
            new EntityKey(Assembly, 3),
            new EntityKey(Assembly, 2),
            "DivRem",
            true,
            MethodSignatureModel.Create(
                CliValueKind.ValueType,
                operand.StackKind,
                operand.StackKind) with
            {
                ParameterSignatureTypes = [operand, operand],
            },
            0);
        var provider = new RuntimeIntrinsicExceptionRequirementProvider(
            new FixedRegistry(method.Key, RuntimeIntrinsic.IntegerDivRem));

        var requirement = Assert.Single(provider.Discover(method));
        Assert.Equal(ManagedExceptionKind.DivideByZero, requirement.Kind);
    }

    [Fact]
    public void ProviderRejectsMissingDependenciesAndMethods()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RuntimeIntrinsicExceptionRequirementProvider(null!));
        var provider = Assert.IsAssignableFrom<
            IRuntimeIntrinsicExceptionRequirementProvider>(
                new RuntimeIntrinsicExceptionRequirementProvider(new EmptyRegistry()));
        Assert.Throws<ArgumentNullException>(() => provider.Discover(null!));
    }

    public static TheoryData<RuntimeIntrinsic, string, int, ManagedExceptionKind[]>
        IntrinsicCases
    {
        get
        {
            var cases = new TheoryData<
                RuntimeIntrinsic,
                string,
                int,
                ManagedExceptionKind[]>();
            foreach (var intrinsic in Enum.GetValues<RuntimeIntrinsic>())
            {
                cases.Add(
                    intrinsic,
                    intrinsic == RuntimeIntrinsic.IntegerDivRem ? "DivRem" : "Run",
                    0,
                    DefaultRequirements(intrinsic));
            }

            foreach (var intrinsic in new[]
                     {
                         RuntimeIntrinsic.EnumGetNames,
                         RuntimeIntrinsic.EnumGetName,
                         RuntimeIntrinsic.EnumGetValues,
                         RuntimeIntrinsic.EnumIsDefined,
                     })
            {
                cases.Add(intrinsic, "Run", 1, []);
            }

            cases.Add(
                RuntimeIntrinsic.EnumConvert,
                "InternalToDateTime",
                0,
                [ManagedExceptionKind.InvalidCast]);
            return cases;
        }
    }

    private static ManagedExceptionKind[] DefaultRequirements(
        RuntimeIntrinsic intrinsic) => intrinsic switch
        {
            RuntimeIntrinsic.StringLength or
            RuntimeIntrinsic.ArrayLength or
            RuntimeIntrinsic.ArrayRank or
            RuntimeIntrinsic.JSObjectDispose or
            RuntimeIntrinsic.JSSubscriptionDispose or
            RuntimeIntrinsic.GetArrayDataReference or
            RuntimeIntrinsic.EnumEquals or
            RuntimeIntrinsic.EnumGetHashCode or
            RuntimeIntrinsic.EnumGetTypeCode => [ManagedExceptionKind.NullReference],
            RuntimeIntrinsic.NullableGetUnderlyingType => [ManagedExceptionKind.ArgumentNull],
            RuntimeIntrinsic.ArrayGetLength or RuntimeIntrinsic.ArrayGetLowerBound or
            RuntimeIntrinsic.StringCharacterAt =>
                [ManagedExceptionKind.NullReference, ManagedExceptionKind.IndexOutOfRange],
            RuntimeIntrinsic.ArrayGetValue =>
                [ManagedExceptionKind.NullReference, ManagedExceptionKind.OutOfMemory],
            RuntimeIntrinsic.MemberExecuteMethod =>
                [ManagedExceptionKind.NullReference, ManagedExceptionKind.OutOfMemory,
                    ManagedExceptionKind.InvalidCast,
                    ManagedExceptionKind.InvalidOperation],
            RuntimeIntrinsic.DelegateDynamicInvoke =>
                [ManagedExceptionKind.OutOfMemory, ManagedExceptionKind.Argument],
            RuntimeIntrinsic.MemberReadField =>
                [ManagedExceptionKind.NullReference, ManagedExceptionKind.OutOfMemory],
            RuntimeIntrinsic.NativeMemoryAlloc or
            RuntimeIntrinsic.NativeMemoryRealloc or
            RuntimeIntrinsic.NativeMemoryAlignedAlloc or
            RuntimeIntrinsic.NativeMemoryAlignedRealloc or
            RuntimeIntrinsic.ObjectArrayDelegateAdapterCreate =>
                [ManagedExceptionKind.OutOfMemory],
            RuntimeIntrinsic.UnsafeUnbox =>
                [ManagedExceptionKind.NullReference, ManagedExceptionKind.InvalidCast],
            RuntimeIntrinsic.EnumCompareTo or RuntimeIntrinsic.EnumHasFlag =>
                [ManagedExceptionKind.NullReference, ManagedExceptionKind.Argument],
            RuntimeIntrinsic.IntegerDivRem =>
                [ManagedExceptionKind.DivideByZero, ManagedExceptionKind.Overflow],
            RuntimeIntrinsic.EnumGetNames or
            RuntimeIntrinsic.EnumGetName or
            RuntimeIntrinsic.EnumGetValues or
            RuntimeIntrinsic.EnumIsDefined or
            RuntimeIntrinsic.EnumGetUnderlyingType or
            RuntimeIntrinsic.EnumFormat or
            RuntimeIntrinsic.EnumToObject =>
                [ManagedExceptionKind.ArgumentNull, ManagedExceptionKind.Argument],
            _ => [],
        };

    private static string ExpectedTypeName(ManagedExceptionKind kind) => kind switch
    {
        ManagedExceptionKind.NullReference => "System.NullReferenceException",
        ManagedExceptionKind.IndexOutOfRange => "System.IndexOutOfRangeException",
        ManagedExceptionKind.OutOfMemory => "System.OutOfMemoryException",
        ManagedExceptionKind.ArgumentNull => "System.ArgumentNullException",
        ManagedExceptionKind.Argument => "System.ArgumentException",
        ManagedExceptionKind.InvalidCast => "System.InvalidCastException",
        ManagedExceptionKind.Overflow => "System.OverflowException",
        ManagedExceptionKind.DivideByZero => "System.DivideByZeroException",
        ManagedExceptionKind.InvalidOperation => "System.InvalidOperationException",
        _ => throw new InvalidOperationException($"Unexpected requirement '{kind}'."),
    };

    private static MethodDefinitionModel Method(string name, int genericArity)
    {
        var signature = name == "DivRem"
            ? MethodSignatureModel.Create(
                CliValueKind.ValueType,
                CliValueKind.I4,
                CliValueKind.I4)
            : MethodSignatureModel.Create(CliValueKind.Void);
        return new(
            new EntityKey(Assembly, 1),
            new EntityKey(Assembly, 2),
            name,
            true,
            signature,
            0)
        {
            GenericArity = genericArity,
        };
    }

    private sealed class FixedRegistry(
        EntityKey method,
        RuntimeIntrinsic intrinsic) : IRuntimeIntrinsicRegistry
    {
        public bool TryGetIntrinsic(EntityKey candidate, out RuntimeIntrinsic result)
        {
            result = intrinsic;
            return candidate == method;
        }
    }

    private sealed class EmptyRegistry : IRuntimeIntrinsicRegistry
    {
        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic)
        {
            intrinsic = default;
            return false;
        }
    }
}
