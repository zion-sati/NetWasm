using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Tests;

public sealed class ReachableSetOverlapClassifierTests
{
    [Fact]
    public void ClassifiesDisjointAndOverlappingReachabilityThroughItsContract()
    {
        var classifier = As<IReachableSetOverlapClassifier>(
            new ReachableSetOverlapClassifier());

        Assert.False(classifier.Overlaps(
            [ImmutableHashSet.Create(1, 2), ImmutableHashSet.Create(3)]));
        Assert.True(classifier.Overlaps(
            [ImmutableHashSet.Create(1, 2), ImmutableHashSet.Create(2, 3)]));
    }

    [Fact]
    public void RejectsUninitializedAndNullReachabilitySets()
    {
        var classifier = As<IReachableSetOverlapClassifier>(
            new ReachableSetOverlapClassifier());

        Assert.Throws<ArgumentException>(() => classifier.Overlaps(default));
        Assert.Throws<ArgumentNullException>(() => classifier.Overlaps(
            [ImmutableHashSet.Create(1), null!]));
    }

    private static TContract As<TContract>(object actor) where TContract : class =>
        (TContract)actor;
}
