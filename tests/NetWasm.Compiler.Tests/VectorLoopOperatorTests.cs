using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// Vector128<T> declares op_Multiply for two vectors and for a vector and a
// scalar either way round; a vectorized loop's multiplication of two vectors
// takes only the first.
public sealed class VectorLoopOperatorTests
{
    private static readonly AssemblyIdentity Core = new("NetWasm.CoreLib");
    private static readonly CliTypeIdentity Scalar = CliTypeIdentity.GenericParameter(method: false, 0);
    private static readonly CliTypeIdentity Vector = CliTypeIdentity.GenericInstantiation(
        CliTypeIdentity.Named(Core, "System.Runtime.Intrinsics", "Vector128`1", isValueType: true), [Scalar]);

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void ABinaryOperatorTakesTwoVectors(bool leftVector, bool rightVector, bool matches)
    {
        var method = new MethodDefinitionModel(new(Core, 0x06000001), new(Core, 0x02000001), "op_Multiply", true,
            MethodSignatureModel.Create(Vector, leftVector ? Vector : Scalar, rightVector ? Vector : Scalar), 1);

        Assert.Equal(matches, new VectorLoopOperator("op_Multiply").Matches(method));
    }

    // A shift takes a vector and an int count.
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void AShiftTakesAVectorAndACount(bool counted, bool matches)
    {
        var count = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var method = new MethodDefinitionModel(new(Core, 0x06000002), new(Core, 0x02000001), "op_LeftShift", true,
            counted ? MethodSignatureModel.Create(Vector, Vector, count) : MethodSignatureModel.Create(Vector, Vector), 1);

        Assert.Equal(matches, new VectorLoopOperator("op_LeftShift").Matches(method));
    }
}
