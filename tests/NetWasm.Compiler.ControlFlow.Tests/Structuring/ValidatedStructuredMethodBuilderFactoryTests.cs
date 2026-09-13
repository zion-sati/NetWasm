using NetWasm.Compiler.ControlFlow.Structuring;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class ValidatedStructuredMethodBuilderFactoryTests
{
    [Fact]
    public void CreateReturnsTheStructuredBuilderCapability()
    {
        var actor = Assert.IsAssignableFrom<IValidatedStructuredMethodBuilderFactory>(
            new ValidatedStructuredMethodBuilderFactory(
                new ControlFlowPostDominanceAnalyzer(),
                new ControlFlowComponentAnalyzer(),
                new ControlFlowNaturalLoopAnalyzer(new ControlFlowDominanceAnalyzer()),
                new ReachableSetOverlapClassifier(),
                new LoopConditionChooser(),
                new DispatcherBoundaryClipper()));

        Assert.NotNull(actor.Create());
    }

    [Fact]
    public void ConstructorRequiresEveryAnalysisCapability()
    {
        var postDominance = new ControlFlowPostDominanceAnalyzer();
        var components = new ControlFlowComponentAnalyzer();
        var naturalLoops = new ControlFlowNaturalLoopAnalyzer(new ControlFlowDominanceAnalyzer());
        var overlaps = new ReachableSetOverlapClassifier();
        var loopConditions = new LoopConditionChooser();
        var dispatcherBoundaries = new DispatcherBoundaryClipper();

        Assert.Throws<ArgumentNullException>(() => new ValidatedStructuredMethodBuilderFactory(
            null!, components, naturalLoops, overlaps, loopConditions, dispatcherBoundaries));
        Assert.Throws<ArgumentNullException>(() => new ValidatedStructuredMethodBuilderFactory(
            postDominance, null!, naturalLoops, overlaps, loopConditions, dispatcherBoundaries));
        Assert.Throws<ArgumentNullException>(() => new ValidatedStructuredMethodBuilderFactory(
            postDominance, components, null!, overlaps, loopConditions, dispatcherBoundaries));
        Assert.Throws<ArgumentNullException>(() => new ValidatedStructuredMethodBuilderFactory(
            postDominance, components, naturalLoops, null!, loopConditions, dispatcherBoundaries));
        Assert.Throws<ArgumentNullException>(() => new ValidatedStructuredMethodBuilderFactory(
            postDominance, components, naturalLoops, overlaps, null!, dispatcherBoundaries));
        Assert.Throws<ArgumentNullException>(() => new ValidatedStructuredMethodBuilderFactory(
            postDominance, components, naturalLoops, overlaps, loopConditions, null!));
    }
}
