using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Tests;

public sealed class StackTypeCompatibilityValidatorTests
{
    [Fact]
    public void StrategyDefinesTheCompleteStackTypeCrossProduct()
    {
        foreach (var expected in Enum.GetValues<CliValueKind>())
        {
            foreach (var actual in Enum.GetValues<CliValueKind>())
            {
                var shouldAccept = expected == actual ||
                    expected == CliValueKind.NativeInt && actual == CliValueKind.ManagedAddress ||
                    expected == CliValueKind.ManagedAddress && actual == CliValueKind.NativeInt;

                Assert.Equal(shouldAccept, Accepts(expected, actual));
            }
        }
    }

    [Fact]
    public void StrategyAcceptsIdenticalStackTypes()
    {
        foreach (var kind in Enum.GetValues<CliValueKind>())
        {
            Assert.True(Accepts(kind, kind));
        }
    }

    [Fact]
    public void StrategyAcceptsManagedAddressesWhereNativePointersAreExpected()
    {
        Assert.True(Accepts(
            CliValueKind.NativeInt,
            CliValueKind.ManagedAddress));
    }

    [Fact]
    public void StrategyAcceptsNativePointersWhereManagedAddressesAreExpected()
    {
        Assert.True(Accepts(
            CliValueKind.ManagedAddress,
            CliValueKind.NativeInt));
    }

    [Theory]
    [InlineData(CliValueKind.I4, CliValueKind.ManagedAddress)]
    [InlineData(CliValueKind.NativeInt, CliValueKind.ManagedReference)]
    public void StrategyRejectsOtherMismatchedStackTypes(
        CliValueKind expected,
        CliValueKind actual)
    {
        Assert.False(Accepts(expected, actual));
    }

    private static bool Accepts(CliValueKind expected, CliValueKind actual) =>
        ((IStackTypeCompatibilityValidator)new StackTypeCompatibilityValidator())
        .Accepts(expected, actual);
}
