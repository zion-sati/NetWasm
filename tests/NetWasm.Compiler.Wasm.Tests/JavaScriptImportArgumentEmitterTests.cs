using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class JavaScriptImportArgumentEmitterTests
{
    [Fact]
    public void ScalarArgumentLoadsTheSuppliedLocalDirectly()
    {
        var addresses = new RecordingAddressEmitter();
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        CreateEmitter(new RecordingLayoutProvider(), addresses).Emit(
            code,
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            7,
            EmitterTestSupport.CreateMethodEmissionContext());

        Assert.Empty(addresses.Values);
        Assert.Equal([WasmOpcodes.LocalGet, 7], code.ToArray());
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 8)]
    public void HostObjectArgumentLoadsItsHandleWithTargetHeaderWidth(
        bool memory64,
        int expectedHeaderSize)
    {
        var target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32;
        var addresses = new RecordingAddressEmitter();
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var type = CliTypeIdentity.Named(
            new AssemblyIdentity("Tests"),
            "System.Runtime.InteropServices.JavaScript",
            "JSObject",
            isValueType: false);

        CreateEmitter(new RecordingLayoutProvider(target), addresses).Emit(
            code,
            type,
            7,
            EmitterTestSupport.CreateMethodEmissionContext());

        Assert.Equal(
            [AddressOperation.EqualZero, expectedHeaderSize, AddressOperation.Add],
            addresses.Values);
        Assert.Contains(WasmOpcodes.If, code.ToArray());
        Assert.Contains(WasmOpcodes.I32Load, code.ToArray());
    }

    private static IJavaScriptImportArgumentEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        IAddressInstructionEmitter addresses) =>
        new[] { new JavaScriptImportArgumentEmitter(layouts, addresses) }
            .Cast<IJavaScriptImportArgumentEmitter>()
            .Single();

    private sealed class RecordingAddressEmitter : IAddressInstructionEmitter
    {
        public List<object> Values { get; } = [];

        public void Emit(IWasmInstructionWriter code, int constant) =>
            Values.Add(constant);

        public void Emit(IWasmInstructionWriter code, AddressOperation operation) =>
            Values.Add(operation);
    }
}
