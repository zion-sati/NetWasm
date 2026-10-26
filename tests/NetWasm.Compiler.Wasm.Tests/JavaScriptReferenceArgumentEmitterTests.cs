using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

// Exports and callbacks share one conversion for each reference value that
// enters managed code from JavaScript.
public sealed class JavaScriptReferenceArgumentEmitterTests
{
    [Fact]
    public void EachReferenceKindUsesItsConversion()
    {
        var strings = new RecordingStrings();
        var arrays = new RecordingArrays();
        var objects = new RecordingObjects();
        IJavaScriptReferenceArgumentEmitter emitter = new[]
        {
            new JavaScriptReferenceArgumentEmitter(strings, arrays, objects),
        }.Cast<IJavaScriptReferenceArgumentEmitter>().Single();
        var target = new InteropMarshallingTarget(new InteropImportPlan([], OptionalFunctionIndex.At(1),
            OptionalFunctionIndex.At(2), OptionalFunctionIndex.Missing, OptionalFunctionIndex.Missing,
            OptionalFunctionIndex.At(3), OptionalFunctionIndex.At(4)));
        var code = new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer()));
        var stringType = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);
        var arrayType = CliTypeIdentity.SzArray(CliTypeIdentity.Primitive("f8", CliValueKind.F8));
        var objectType = CliTypeIdentity.Named(
            Assembly, "System.Runtime.InteropServices.JavaScript", "JSObject", isValueType: false);

        emitter.Emit(code, stringType, 1, 5, 6, 7, target, OptionalFunctionIndex.At(9));
        emitter.Emit(code, arrayType, 2, 8, 6, 7, target, OptionalFunctionIndex.At(9));
        emitter.Emit(code, objectType, 3, 10, 6, 7, target, OptionalFunctionIndex.At(9));

        Assert.Equal([(1, 5)], strings.Calls);
        Assert.Equal([(arrayType, 2, 8)], arrays.Calls);
        Assert.Equal([(objectType, 3, 10)], objects.Calls);
        Assert.Throws<InvalidOperationException>(() => emitter.Emit(code,
            CliTypeIdentity.FromStackKind(CliValueKind.I4), 4, 11, 6, 7, target, OptionalFunctionIndex.At(9)));
    }

    private sealed class RecordingStrings : IHostCallbackStringArgumentMarshaller
    {
        public List<(int, int)> Calls { get; } = [];

        public void Emit(IWasmInstructionWriter code, int handleParameter, int destination, int temporaryI4,
            int objectTemporary, InteropMarshallingTarget target, OptionalFunctionIndex throwFunction) =>
            Calls.Add((handleParameter, destination));
    }

    private sealed class RecordingArrays : IHostCallbackByteArrayArgumentMarshaller
    {
        public List<(CliTypeIdentity, int, int)> Calls { get; } = [];

        public void Emit(IWasmInstructionWriter code, CliTypeIdentity arrayType, int handleParameter, int destination,
            int temporaryI4, int objectTemporary, InteropMarshallingTarget target, OptionalFunctionIndex throwFunction) =>
            Calls.Add((arrayType, handleParameter, destination));
    }

    private sealed class RecordingObjects : IHostCallbackHostObjectArgumentMarshaller
    {
        public List<(CliTypeIdentity, int, int)> Calls { get; } = [];

        public void Emit(IWasmInstructionWriter code, CliTypeIdentity objectType, int handleParameter, int destination,
            OptionalFunctionIndex throwFunction) =>
            Calls.Add((objectType, handleParameter, destination));
    }
}
