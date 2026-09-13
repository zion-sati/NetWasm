using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.LocalTime;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests.Emission.GeneratedFunctions.LocalTime;

public sealed class LocalTimePreflightCallEmitterTests
{
    private static readonly EntityKey MethodKey = new(new("LocalTimeTests"), 0x06000001);

    [Fact]
    public void EmitsSelectedManagedCall()
    {
        var selector = new FixedSelector(MethodKey);
        var indices = new RecordingFunctionIndexResolver(37);
        var emitter = CreateEmitter(selector);
        var code = new GeneratedFunctionWriterFactory().Create();

        var emitted = emitter.Emit(code, EmitterTestSupport.CreateEmissionRequest(), indices);

        Assert.True(emitted);
        Assert.Equal(MethodKey, indices.Method);
        Assert.Equal([WasmOpcodes.Call, 37], code.Snapshots.Read());
    }

    [Fact]
    public void EmitsNothingWithoutReachablePreflight()
    {
        var emitter = CreateEmitter(new FixedSelector(null));
        var code = new GeneratedFunctionWriterFactory().Create();

        var emitted = emitter.Emit(
            code,
            EmitterTestSupport.CreateEmissionRequest(),
            new RecordingFunctionIndexResolver(37));

        Assert.False(emitted);
        Assert.Empty(code.Snapshots.Read());
    }

    [Fact]
    public void ValidatesDependencyAndInputs()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LocalTimePreflightCallEmitter(null!));
        var emitter = CreateEmitter(new FixedSelector(null));
        var code = new GeneratedFunctionWriterFactory().Create();
        var request = EmitterTestSupport.CreateEmissionRequest();
        var indices = new RecordingFunctionIndexResolver(37);

        Assert.Throws<ArgumentNullException>(() =>
            emitter.Emit(null!, request, indices));
        Assert.Throws<ArgumentNullException>(() =>
            emitter.Emit(code, null!, indices));
        Assert.Throws<ArgumentNullException>(() =>
            emitter.Emit(code, request, null!));
    }

    private static ILocalTimePreflightCallEmitter CreateEmitter(
        ILocalTimePreflightMethodSelector selector) =>
        new[]
        {
            new LocalTimePreflightCallEmitter(selector),
        }.Cast<ILocalTimePreflightCallEmitter>().Single();

    private sealed class FixedSelector(EntityKey? method) :
        ILocalTimePreflightMethodSelector
    {
        public EntityKey? Select(WasmEmissionRequest request) => method;
    }

    private sealed class RecordingFunctionIndexResolver(int index) :
        IFunctionIndexResolver
    {
        public EntityKey? Method { get; private set; }

        public int Resolve(EntityKey method)
        {
            Method = method;
            return index;
        }

        public int Resolve(string method) => throw new NotSupportedException();

        public int Resolve(MethodInstanceModel method) =>
            throw new NotSupportedException();
    }
}
