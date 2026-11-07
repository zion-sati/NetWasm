using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Tests;

public sealed class MethodSpecializerFactoryTests
{
    [Fact]
    public void ConstructorRejectsMissingControlFlowCapabilities()
    {
        var validators = new TypedStackValidatorFactory(new StackTypeCompatibilityValidator());
        Assert.Throws<ArgumentNullException>(() => new MethodSpecializerFactory(
            null!,
            new ControlFlowGraphAnalyzerFactory(),
            validators));
        Assert.Throws<ArgumentNullException>(() => new MethodSpecializerFactory(
            new ControlFlowGraphBuilderFactory(),
            null!,
            validators));
        Assert.Throws<ArgumentNullException>(() => new MethodSpecializerFactory(
            new ControlFlowGraphBuilderFactory(),
            new ControlFlowGraphAnalyzerFactory(),
            null!));
    }
}
