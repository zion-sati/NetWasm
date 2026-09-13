using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class JavaScriptResultLengthValidatorTests
{
    [Fact]
    public void NegativeResultReleasesTheHostHandleAndThrowsJSException()
    {
        var handles = new RecordingHandleReleaser();
        var exceptions = new RecordingExceptionEmitter();
        var target = new InteropMarshallingTarget(new(
            [], default, default, OptionalFunctionIndex.At(23),
            default, default, default));
        var code = new RecordingInstructionWriter();
        var validator = ThroughContract(new JavaScriptResultLengthValidator(
            handles,
            exceptions));

        validator.Validate(code, CreateMethodEmissionContext(), target);

        Assert.Same(target, handles.Target);
        Assert.Equal([ManagedExceptionKind.JSException], exceptions.Kinds);
        Assert.Contains(WasmOpcodes.I32LessThanSigned, code.ToArray());
        Assert.Contains(WasmOpcodes.If, code.ToArray());
        Assert.Equal(WasmOpcodes.End, code.ToArray()[^1]);
    }

    private static IJavaScriptResultLengthValidator ThroughContract(
        JavaScriptResultLengthValidator validator) => new[]
        {
            validator,
        }.Cast<IJavaScriptResultLengthValidator>().Single();

    private sealed class RecordingHandleReleaser : IInteropHandleReleaser
    {
        public InteropMarshallingTarget? Target { get; private set; }

        public void Release(
            IWasmInstructionWriter code,
            MethodEmissionContext context,
            InteropMarshallingTarget target) => Target = target;

        public void Release(
            IWasmInstructionWriter code,
            MethodEmissionContext context) => throw new InvalidOperationException();
    }

    private sealed class RecordingExceptionEmitter : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(
            IWasmInstructionWriter code,
            ManagedExceptionKind kind) => Kinds.Add(kind);
    }
}
