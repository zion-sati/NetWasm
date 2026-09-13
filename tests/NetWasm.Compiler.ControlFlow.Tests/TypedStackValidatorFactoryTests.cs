using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.ControlFlow.Tests;

public sealed class TypedStackValidatorFactoryTests
{
    [Fact]
    public void FactoryRejectsMissingCollaborators()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TypedStackValidatorFactory(
                null!));
    }

    [Fact]
    public void FactoryCreatesAValidatorFromNarrowRepositories()
    {
        var program = new FakeProgram();

        Assert.NotNull(((ITypedStackValidatorFactory)new TypedStackValidatorFactory(
            new StackTypeCompatibilityValidator())).Create(
                program,
                program,
                program));
    }
}
