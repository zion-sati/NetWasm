using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class OptionalFunctionIndexValidatorTests
{
    [Fact]
    public void ValidatesOptionalHostFunctionPresence()
    {
        var validator = EmitterTestSupport.CreateOptionalFunctionIndexValidator();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            validator.Validate(OptionalFunctionIndex.Missing, "missing helper"));

        Assert.Equal("missing helper", exception.Message);
        validator.Validate(OptionalFunctionIndex.At(4), "unused");
    }
}
