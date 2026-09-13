using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class CallEmissionRegistryTests
{
    [Fact]
    public void ResolvesEveryKindByItsImmutableKey()
    {
        var emitters = Enum.GetValues<CallEmissionKind>()
            .ToDictionary(kind => kind, _ => new RecordingCallEmitter());

        var registry = new CallEmissionRegistry(
            emitters.Select(pair => new CallEmissionRegistration(pair.Key, pair.Value)));

        AssertContract(registry, emitters);
    }

    [Fact]
    public void RejectsDuplicateAndMissingKeysDuringComposition()
    {
        var emitter = new RecordingCallEmitter();
        var complete = Enum.GetValues<CallEmissionKind>()
            .Select(kind => new CallEmissionRegistration(kind, emitter))
            .ToArray();

        var duplicate = Assert.Throws<InvalidOperationException>(() =>
            new CallEmissionRegistry(
                complete.Append(new(CallEmissionKind.Direct, emitter))));
        var missing = Assert.Throws<InvalidOperationException>(() =>
            new CallEmissionRegistry(
                complete.Where(registration =>
                    registration.Kind != CallEmissionKind.Direct)));

        Assert.Contains("more than once", duplicate.Message, StringComparison.Ordinal);
        Assert.Contains("Direct", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNullRegistrationsDuringComposition()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CallEmissionRegistry([null!]));
    }

    [Fact]
    public void RejectsAnUnknownKeyAtLookup()
    {
        var emitter = new RecordingCallEmitter();
        var registry = new CallEmissionRegistry(
            Enum.GetValues<CallEmissionKind>()
                .Select(kind => new CallEmissionRegistration(kind, emitter)));

        Assert.Throws<InvalidOperationException>(() =>
            registry.Get((CallEmissionKind)int.MaxValue));
    }

    private static void AssertContract<TRegistry>(
        TRegistry registry,
        IReadOnlyDictionary<CallEmissionKind, RecordingCallEmitter> emitters)
        where TRegistry : ICallEmissionRegistry =>
        Assert.All(emitters, pair => Assert.Same(pair.Value, registry.Get(pair.Key)));

    private sealed class RecordingCallEmitter : ICallEmitter
    {
        public void Emit(
            CallEmissionRequest request,
            IWasmInstructionWriter code,
            IFunctionIndexResolver functionIndices)
        {
        }
    }
}
