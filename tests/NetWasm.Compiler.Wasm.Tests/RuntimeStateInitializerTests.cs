using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class RuntimeStateInitializerTests
{
    [Fact]
    public void EntriesCallTheModulesRuntimeInitializer()
    {
        var code = new GeneratedFunctionWriterFactory().Create();
        var writer = new EmitterTestSupport.RecordingInstructionWriter();

        new RuntimeStateInitializer().Initialize(
            new GeneratedFunctionWriterLease(code.Bytes, code.Snapshots, writer),
            TestRuntimeInitialization.Create(512) with { InitializerFunctionIndex = 300 });

        var call = Assert.Single(writer.ToInstructions());
        Assert.Equal(WasmOpcodes.Call, call.Opcode);
        Assert.Equal(300u, call.Operand.UnsignedValue);
    }

    [Fact]
    public void InitializationRequiresTheInitializerFunction()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new RuntimeStateInitializer().Initialize(
            new GeneratedFunctionWriterFactory().Create(),
            TestRuntimeInitialization.Create(512) with { InitializerFunctionIndex = null }));

        Assert.Contains("initializer function", exception.Message, StringComparison.Ordinal);
    }
}
