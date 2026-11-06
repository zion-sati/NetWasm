using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class SimdValueFrameSlotAlignmentResolverTests
{
    private static readonly IValueFrameSlotAlignmentResolver Resolver =
        ThroughContract(new SimdValueFrameSlotAlignmentResolver());

    [Theory]
    [InlineData("System.Numerics", "Vector2", 4, 8)]
    [InlineData("System.Numerics", "Vector2", 8, 8)]
    [InlineData("System.Numerics", "Vector3", 4, 16)]
    [InlineData("System.Numerics", "Vector4", 8, 16)]
    [InlineData("System.Numerics", "Quaternion", 4, 16)]
    [InlineData("System.Numerics", "Plane", 4, 16)]
    [InlineData("Test", "Value", 4, 4)]
    [InlineData("Test", "Value", 8, 8)]
    public void NamedVectorsTakeRyuJitsSimdAlignmentAndOtherTypesKeepTheirs(
        string @namespace,
        string name,
        int alignment,
        int expected)
    {
        var type = CliTypeIdentity.Named(Assembly, @namespace, name, isValueType: true);

        Assert.Equal(expected, Resolver.Resolve(type, alignment));
    }

    [Fact]
    public void Vector128TakesSixteenAndAWiderAlignmentKeepsTheLarger()
    {
        var definition = CliTypeIdentity.Named(Assembly, "System.Runtime.Intrinsics", "Vector128`1", isValueType: true);
        var vector = CliTypeIdentity.GenericInstantiation(definition, [CliTypeIdentity.FromStackKind(CliValueKind.F4)]);

        Assert.Equal(CliValueKind.V128, vector.StackKind);
        Assert.Equal(16, Resolver.Resolve(vector, 8));
        Assert.Equal(32, Resolver.Resolve(vector, 32));
    }

    [Fact]
    public void ScalarsKeepTheirAlignment()
    {
        Assert.Equal(4, Resolver.Resolve(CliTypeIdentity.FromStackKind(CliValueKind.F4), 4));
        Assert.Equal(8, Resolver.Resolve(CliTypeIdentity.FromStackKind(CliValueKind.F8), 8));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void RejectsANonPositiveAlignment(int alignment)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Resolver.Resolve(CliTypeIdentity.FromStackKind(CliValueKind.F4), alignment));
    }

    [Fact]
    public void RejectsAMissingType()
    {
        Assert.Throws<ArgumentNullException>(() => Resolver.Resolve(null!, 4));
    }

    private static IValueFrameSlotAlignmentResolver ThroughContract(IValueFrameSlotAlignmentResolver resolver) =>
        resolver;
}
