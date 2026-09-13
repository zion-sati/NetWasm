using NetWasm.Compiler.ComponentModel.ManagedExecutables;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.Tests.ManagedExecutables;

public sealed class SynchronousManagedExecutableComponentAdapterWriterTests
{
    [Theory]
    [InlineData("wasm32", ManagedExecutableParameterShape.None,
        ManagedExecutableReturnShape.Void, "cm32p2")]
    [InlineData("wasm32", ManagedExecutableParameterShape.None,
        ManagedExecutableReturnShape.ExitCode, "cm32p2")]
    [InlineData("wasm32", ManagedExecutableParameterShape.StringArray,
        ManagedExecutableReturnShape.Void, "cm32p2")]
    [InlineData("wasm32", ManagedExecutableParameterShape.StringArray,
        ManagedExecutableReturnShape.ExitCode, "cm32p2")]
    [InlineData("wasm64", ManagedExecutableParameterShape.None,
        ManagedExecutableReturnShape.Void, "cm64p2")]
    [InlineData("wasm64", ManagedExecutableParameterShape.None,
        ManagedExecutableReturnShape.ExitCode, "cm64p2")]
    [InlineData("wasm64", ManagedExecutableParameterShape.StringArray,
        ManagedExecutableReturnShape.Void, "cm64p2")]
    [InlineData("wasm64", ManagedExecutableParameterShape.StringArray,
        ManagedExecutableReturnShape.ExitCode, "cm64p2")]
    public void WritesTargetAwareCommandAdapterForEveryManagedMainShape(
        string width,
        ManagedExecutableParameterShape parameterShape,
        ManagedExecutableReturnShape returnShape,
        string canonicalPrefix)
    {
        var modules = new RecordingWasmTextModuleWriter();
        var writer = Assert.IsAssignableFrom<IManagedExecutableComponentAdapterWriter>(
            new SynchronousManagedExecutableComponentAdapterWriter(modules));
        var expectedResult = returnShape == ManagedExecutableReturnShape.ExitCode
            ? "(result i32)"
            : string.Empty;
        var expectedNormalization = returnShape == ManagedExecutableReturnShape.ExitCode
            ? "i32.eqz i32.eqz"
            : "i32.const 0";

        writer.Write(new(
            "managed-executable.wasm",
            new ComponentTarget(width, "0.2", "utf8"),
            new(parameterShape, returnShape)));

        Assert.Equal("managed-executable.wasm", modules.OutputPath);
        Assert.Contains(
            $"(func $application_run {expectedResult})",
            modules.Source);
        Assert.DoesNotContain("(memory", modules.Source);
        Assert.Contains("call $application_run", modules.Source);
        Assert.DoesNotContain(".const 0 call $application_run", modules.Source);
        Assert.Contains(expectedNormalization, modules.Source);
        Assert.Contains(
            $"(export \"{canonicalPrefix}|wasi:cli/run@0.2|run\")",
            modules.Source);
        Assert.DoesNotContain($"{canonicalPrefix}_memory", modules.Source);
    }

    [Fact]
    public void RejectsMissingOrUnsupportedInput()
    {
        var modules = new RecordingWasmTextModuleWriter();
        var writer = Assert.IsAssignableFrom<IManagedExecutableComponentAdapterWriter>(
            new SynchronousManagedExecutableComponentAdapterWriter(modules));

        Assert.Throws<ArgumentNullException>(() =>
            new SynchronousManagedExecutableComponentAdapterWriter(null!));
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
        Assert.Throws<ArgumentException>(() => writer.Write(new(
            " ",
            ComponentTarget.Wasm32Wasi02,
            new(ManagedExecutableParameterShape.None, ManagedExecutableReturnShape.Void))));
        Assert.Throws<ArgumentNullException>(() => writer.Write(new(
            "output.wasm",
            null!,
            new(ManagedExecutableParameterShape.None, ManagedExecutableReturnShape.Void))));
        Assert.Throws<ArgumentNullException>(() => writer.Write(new(
            "output.wasm",
            ComponentTarget.Wasm32Wasi02,
            null!)));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Write(new(
            "output.wasm",
            ComponentTarget.Wasm32Wasi02,
            new((ManagedExecutableParameterShape)99, ManagedExecutableReturnShape.Void))));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Write(new(
            "output.wasm",
            ComponentTarget.Wasm32Wasi02,
            new(ManagedExecutableParameterShape.None, (ManagedExecutableReturnShape)99))));
        var asyncError = Assert.Throws<CompilerException>(() => writer.Write(new(
            "output.wasm",
            ComponentTarget.Wasm32Wasi02,
            new(
                ManagedExecutableParameterShape.None,
                ManagedExecutableReturnShape.ExitCode,
                ManagedExecutableCompletionShape.Asynchronous))));
        Assert.Equal(DiagnosticCode.ComponentContract, asyncError.Diagnostic.Code);
    }
}
