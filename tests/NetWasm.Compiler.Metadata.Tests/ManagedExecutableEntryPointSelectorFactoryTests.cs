using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.ManagedExecutables;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class ManagedExecutableEntryPointSelectorFactoryTests
{
    [Fact]
    public void CreatesIndependentSelectorsWithWorkingValidation()
    {
        var factory = Assert.IsAssignableFrom<IManagedExecutableEntryPointSelectorFactory>(
            new ManagedExecutableEntryPointSelectorFactory());
        var first = factory.Create();
        var second = factory.Create();
        Assert.NotSame(first, second);
        Assert.Throws<ArgumentException>(() => first.SelectEntryPoint("", ReadOnlyMemory<byte>.Empty));
        Assert.Throws<CompilerException>(() => second.SelectEntryPoint("invalid.dll", ReadOnlyMemory<byte>.Empty));
    }
}
