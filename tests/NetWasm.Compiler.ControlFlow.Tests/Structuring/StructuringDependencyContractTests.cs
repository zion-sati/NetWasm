using System.Reflection;
using NetWasm.Compiler.ControlFlow.Structuring;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class StructuringDependencyContractTests
{
    private static readonly Type[] Actors =
    [
        typeof(BlockRangeStructurer),
        typeof(BranchReachabilityOverlapClassifier),
        typeof(CommonReachableBlockFinder),
        typeof(ControlFlowDomainFinder),
        typeof(ControlFlowPostDominatorFinder),
        typeof(ExceptionGroupStructurer),
        typeof(LoopContinuationBuilder),
        typeof(LoopContinueTargetFinder),
        typeof(ReachableBlockFinder),
    ];

    public static IEnumerable<object[]> MissingDependencies =>
        Actors.SelectMany(actor =>
            Assert.Single(actor.GetConstructors())
                .GetParameters()
                .Select(parameter => new object[] { actor, parameter.Position }));

    [Theory]
    [MemberData(nameof(MissingDependencies))]
    public void ConstructorsRejectEveryMissingCapability(Type actor, int missing)
    {
        var constructor = Assert.Single(actor.GetConstructors());
        var arguments = constructor.GetParameters()
            .Select(parameter => DispatchProxy.Create(
                parameter.ParameterType,
                typeof(UnusedCapabilityProxy)))
            .ToArray();
        arguments[missing] = null!;

        var exception = Assert.Throws<TargetInvocationException>(() =>
            constructor.Invoke(arguments));

        Assert.IsType<ArgumentNullException>(exception.InnerException);
    }

#pragma warning disable CA1852 // DispatchProxy requires an inheritable proxy base type.
    private class UnusedCapabilityProxy : DispatchProxy
    {
        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args) => throw new InvalidOperationException();
    }
#pragma warning restore CA1852
}
