// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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

    // A PackedSimd lane index outside the vector throws as upstream does; the other shapes, the internal Shuffle
    // among them (any index is defined: zero beyond 31), throw nothing.
    [Theory]
    [InlineData("Add", "v:i4,v:i4", false)]
    [InlineData("Shuffle", "v:u1,v:u1,v:u1", false)]
    [InlineData("ExtractScalar", "v:i4,s:u1", true)]
    [InlineData("LoadScalarAndInsert", "p:i4,v:i4,s:u1", true)]
    [InlineData("StoreSelectedScalar", "p:i4,v:i4,s:u1", true)]
    public void APackedSimdLaneIndexAloneRequiresArgumentOutOfRange(string name, string parameters, bool laneIndex)
    {
        var types = parameters.Split(',').Select(static token =>
        {
            var element = token[2..];
            var primitive = CliTypeIdentity.Primitive(element, CliValueKind.I4);
            return token[0] switch
            {
                'v' => CliTypeIdentity.GenericInstantiation(
                    CliTypeIdentity.Named(Assembly, "System.Runtime.Intrinsics", "Vector128`1", true), [primitive]),
                'p' => CliTypeIdentity.UnmanagedPointer(primitive),
                _ => primitive,
            };
        }).ToArray();
        var method = new MethodDefinitionModel(
            new EntityKey(Assembly, 3),
            new EntityKey(Assembly, 2),
            name,
            true,
            MethodSignatureModel.Create(CliValueKind.V128, [.. types.Select(static type => type.StackKind)]) with
            {
                ParameterSignatureTypes = [.. types],
            },
            0);
        var provider = new RuntimeIntrinsicExceptionRequirementProvider(
            new FixedRegistry(method.Key, RuntimeIntrinsic.PackedSimdInstruction));

        Assert.Equal(
            laneIndex ? [ManagedExceptionKind.ArgumentOutOfRange] : [],
            provider.Discover(method).Select(requirement => requirement.Kind));
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
            RuntimeIntrinsic.ArrayPrimitiveElementType or
            RuntimeIntrinsic.JSObjectDispose or
            RuntimeIntrinsic.JSSubscriptionDispose or
            RuntimeIntrinsic.GetArrayDataReference or
            RuntimeIntrinsic.EnumEquals or
            RuntimeIntrinsic.EnumGetHashCode or
            RuntimeIntrinsic.EnumGetTypeCode => [ManagedExceptionKind.NullReference],
            RuntimeIntrinsic.ArrayElementType or RuntimeIntrinsic.ArrayCreateInstance or
                RuntimeIntrinsic.ArrayCreateRectangularInstance or RuntimeIntrinsic.ArrayCreateBoundedInstance or
                RuntimeIntrinsic.ArrayCreateSharedInstance or
                RuntimeIntrinsic.AllocateUninitializedObject => [ManagedExceptionKind.OutOfMemory],
            // The frozen OutOfMemoryException it returns is built only when required.
            RuntimeIntrinsic.PreallocatedOutOfMemoryException => [ManagedExceptionKind.OutOfMemory],
            RuntimeIntrinsic.NullableGetUnderlyingType => [ManagedExceptionKind.ArgumentNull],
            RuntimeIntrinsic.AllocateUninitializedArray => [ManagedExceptionKind.Overflow, ManagedExceptionKind.OutOfMemory],
            RuntimeIntrinsic.ArrayGetLength or RuntimeIntrinsic.ArrayGetLowerBound or
            RuntimeIntrinsic.StringCharacterAt =>
                [ManagedExceptionKind.NullReference, ManagedExceptionKind.IndexOutOfRange],
            RuntimeIntrinsic.ObjectClone =>
                [ManagedExceptionKind.NullReference, ManagedExceptionKind.OutOfMemory],
            RuntimeIntrinsic.ArrayGetValue =>
                [ManagedExceptionKind.NullReference, ManagedExceptionKind.OutOfMemory, ManagedExceptionKind.NotSupported],
            RuntimeIntrinsic.MemberExecuteMethod =>
                [ManagedExceptionKind.NullReference, ManagedExceptionKind.OutOfMemory,
                    ManagedExceptionKind.InvalidCast,
                    ManagedExceptionKind.InvalidOperation],
            RuntimeIntrinsic.DelegateDynamicInvoke =>
                [ManagedExceptionKind.OutOfMemory, ManagedExceptionKind.Argument],
            RuntimeIntrinsic.MemberReadField =>
                [ManagedExceptionKind.NullReference, ManagedExceptionKind.OutOfMemory,
                    ManagedExceptionKind.InvalidCast],
            RuntimeIntrinsic.MemberWriteField =>
                [ManagedExceptionKind.NullReference, ManagedExceptionKind.InvalidCast],
            RuntimeIntrinsic.NativeMemoryAlloc or
            RuntimeIntrinsic.NativeMemoryRealloc or
            RuntimeIntrinsic.NativeMemoryAlignedAlloc or
            RuntimeIntrinsic.NativeMemoryAlignedRealloc or
            RuntimeIntrinsic.ObjectArrayDelegateAdapterCreate =>
                [ManagedExceptionKind.OutOfMemory],
            RuntimeIntrinsic.UnsafeUnbox =>
                [ManagedExceptionKind.NullReference, ManagedExceptionKind.InvalidCast],
            RuntimeIntrinsic.EnumCompareTo =>
                [ManagedExceptionKind.NullReference, ManagedExceptionKind.Argument],
            RuntimeIntrinsic.EnumHasFlag =>
                [ManagedExceptionKind.NullReference, ManagedExceptionKind.ArgumentNull, ManagedExceptionKind.Argument],
            RuntimeIntrinsic.IntegerDivRem =>
                [ManagedExceptionKind.DivideByZero, ManagedExceptionKind.Overflow],
            RuntimeIntrinsic.StringAllocate =>
                [ManagedExceptionKind.Overflow, ManagedExceptionKind.OutOfMemory],
            RuntimeIntrinsic.EnumGetNames or
            RuntimeIntrinsic.EnumGetName or
            RuntimeIntrinsic.EnumGetValues or
            RuntimeIntrinsic.EnumIsDefined or
            RuntimeIntrinsic.EnumGetUnderlyingType or
            RuntimeIntrinsic.EnumFormat or
            RuntimeIntrinsic.EnumToObject =>
                [ManagedExceptionKind.ArgumentNull, ManagedExceptionKind.Argument],
            RuntimeIntrinsic.SimdGetElement or RuntimeIntrinsic.SimdWithElement =>
                [ManagedExceptionKind.NotSupported, ManagedExceptionKind.ArgumentOutOfRange],
            RuntimeIntrinsic.SimdDivide =>
                [ManagedExceptionKind.NotSupported, ManagedExceptionKind.DivideByZero, ManagedExceptionKind.Overflow],
            _ when RuntimeIntrinsicKinds.IsSimdVector(intrinsic) =>
                [ManagedExceptionKind.NotSupported],
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
        ManagedExceptionKind.NotSupported => "System.NotSupportedException",
        ManagedExceptionKind.ArgumentOutOfRange => "System.ArgumentOutOfRangeException",
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
