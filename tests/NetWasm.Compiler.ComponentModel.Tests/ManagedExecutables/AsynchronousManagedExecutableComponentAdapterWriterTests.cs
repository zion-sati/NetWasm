using NetWasm.Compiler.ComponentModel.ManagedExecutables;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.Tests.ManagedExecutables;

public sealed class AsynchronousManagedExecutableComponentAdapterWriterTests
{
    public static TheoryData<string, ManagedExecutableParameterShape, ManagedExecutableReturnShape> Shapes
    {
        get
        {
            var shapes = new TheoryData<string, ManagedExecutableParameterShape, ManagedExecutableReturnShape>();
            foreach (var width in new[] { "wasm32", "wasm64" })
                foreach (var parameter in Enum.GetValues<ManagedExecutableParameterShape>())
                    foreach (var result in Enum.GetValues<ManagedExecutableReturnShape>())
                        shapes.Add(width, parameter, result);
            return shapes;
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ForwardsTheProcessLifecycleWithoutClaimingSynchronousCommandCompletion(
        string width, ManagedExecutableParameterShape parameter, ManagedExecutableReturnShape result)
    {
        var modules = new RecordingWasmTextModuleWriter();
        var writer = Assert.IsAssignableFrom<IManagedExecutableComponentAdapterWriter>(
            new AsynchronousManagedExecutableComponentAdapterWriter(modules));
        var prefix = width == "wasm64" ? "cm64p2" : "cm32p2";

        writer.Write(new("output.wasm", new(width, "0.2", "utf8"),
            new(parameter, result, ManagedExecutableCompletionShape.Asynchronous)));

        Assert.Equal("output.wasm", modules.OutputPath);
        foreach (var operation in new[] { "start", "status", "exit-code", "complete" })
            Assert.Contains($"(export \"{prefix}|netwasm:runtime/process@1|{operation}\")", modules.Source);
        Assert.Contains("(func $process_start (result i32))", modules.Source);
        Assert.Contains("(result i32) call $process_start)", modules.Source);
        Assert.DoesNotContain(".const 0 call $process_start", modules.Source);
        Assert.Contains("local.get 0 call $process_status)", modules.Source);
        Assert.Contains("local.get 0 call $process_complete)", modules.Source);
        Assert.DoesNotContain("wasi:cli/run", modules.Source);
        if (result == ManagedExecutableReturnShape.ExitCode)
        {
            Assert.Contains("\"netwasm.process.result\"", modules.Source);
            Assert.Contains("local.get 0 call $process_result)", modules.Source);
        }
        else
        {
            Assert.DoesNotContain("\"netwasm.process.result\"", modules.Source);
            Assert.Contains("(param i32) (result i32) i32.const 0)", modules.Source);
        }
    }

    [Fact]
    public void RejectsInvalidInputsWithoutWritingAModule()
    {
        var modules = new RecordingWasmTextModuleWriter();
        var writer = Assert.IsAssignableFrom<IManagedExecutableComponentAdapterWriter>(
            new AsynchronousManagedExecutableComponentAdapterWriter(modules));
        var request = new ManagedExecutableComponentAdapterRequest("output.wasm", ComponentTarget.Wasm32Wasi02,
            new(ManagedExecutableParameterShape.None, ManagedExecutableReturnShape.Void,
                ManagedExecutableCompletionShape.Asynchronous));
        Assert.Throws<ArgumentNullException>(() => new AsynchronousManagedExecutableComponentAdapterWriter(null!));
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
        Assert.Throws<ArgumentException>(() => writer.Write(request with { OutputPath = " " }));
        Assert.Throws<ArgumentNullException>(() => writer.Write(request with { Target = null! }));
        Assert.Throws<ArgumentNullException>(() => writer.Write(request with { EntryPoint = null! }));
        Assert.Throws<ArgumentException>(() => writer.Write(request with
        { EntryPoint = request.EntryPoint with { CompletionShape = ManagedExecutableCompletionShape.Synchronous } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Write(request with { Target = new("invalid", "0.2", "utf8") }));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Write(request with
        { EntryPoint = request.EntryPoint with { ParameterShape = (ManagedExecutableParameterShape)99 } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Write(request with
        { EntryPoint = request.EntryPoint with { ReturnShape = (ManagedExecutableReturnShape)99 } }));
        Assert.Empty(modules.OutputPath);
        Assert.Empty(modules.Source);
    }
}
