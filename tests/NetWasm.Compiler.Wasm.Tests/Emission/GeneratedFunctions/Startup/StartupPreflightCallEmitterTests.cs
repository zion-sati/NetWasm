using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.Startup;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests.Emission.GeneratedFunctions.Startup;

public sealed class StartupPreflightCallEmitterTests
{
    private static readonly EntityKey First = new(new("StartupTests"), 0x06000001);
    private static readonly EntityKey Second = new(new("StartupTests"), 0x06000002);

    [Fact]
    public void CallsEachPreflightInOrder()
    {
        var indices = new RecordingFunctionIndexResolver();
        var code = new GeneratedFunctionWriterFactory().Create();

        new StartupPreflightCallEmitter().Emit(code, [First, Second], indices);

        Assert.Equal([First, Second], indices.Methods);
        Assert.Equal([WasmOpcodes.Call, 1, WasmOpcodes.Call, 2], code.Snapshots.Read());
    }

    [Fact]
    public void EmitsNothingWithoutPreflights()
    {
        var code = new GeneratedFunctionWriterFactory().Create();

        new StartupPreflightCallEmitter().Emit(code, [], new RecordingFunctionIndexResolver());

        Assert.Empty(code.Snapshots.Read());
    }

    [Fact]
    public void ValidatesInputs()
    {
        var emitter = new StartupPreflightCallEmitter();
        var code = new GeneratedFunctionWriterFactory().Create();

        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, [], new RecordingFunctionIndexResolver()));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(code, [], null!));
    }

    private sealed class RecordingFunctionIndexResolver : IFunctionIndexResolver
    {
        public OptionalFunctionIndex ManagedThrowFunction => OptionalFunctionIndex.At(7);

        public List<EntityKey> Methods { get; } = [];

        public int Resolve(InteropImportKey import) => Resolve(import.Method);

        public int Resolve(EntityKey method)
        {
            Methods.Add(method);
            return Methods.Count;
        }

        public int Resolve(string method) => throw new NotSupportedException();

        public int Resolve(MethodInstanceModel method) => throw new NotSupportedException();
    }
}
