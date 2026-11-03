using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class HostCallbackArgumentMarshallerTests
{
    [Fact]
    public void StringMaterializationEmitsNullFailureAllocationAndCopyPaths()
    {
        var layouts = new RecordingLayoutProvider();
        IHostCallbackStringArgumentMarshaller marshaller = new[]
        {
            new HostCallbackStringArgumentMarshaller(
                layouts,
                WasmRuntimeImports.CreateCatalog(),
                new ImplicitExceptionEmitter(layouts),
                new AddressInstructionEmitter(layouts)),
        }.Cast<IHostCallbackStringArgumentMarshaller>().Single();
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        marshaller.Emit(code, 0, 1, 2, 3, CompleteTarget(), OptionalFunctionIndex.At(7));

        Assert.Contains(WasmOpcodes.I32EqualZero, code.ToArray());
        Assert.Contains(WasmOpcodes.I32LessThanSigned, code.ToArray());
        Assert.Contains(WasmOpcodes.I32GreaterThanUnsigned, code.ToArray());
        Assert.Contains(WasmOpcodes.Call, code.ToArray());
    }

    [Fact]
    public void ByteArrayMaterializationEmitsNullFailureAllocationAndCopyPaths()
    {
        var layouts = new RecordingLayoutProvider();
        IHostCallbackByteArrayArgumentMarshaller marshaller = new[]
        {
            new HostCallbackByteArrayArgumentMarshaller(
                layouts,
                layouts,
                WasmRuntimeImports.CreateCatalog(),
                new ImplicitExceptionEmitter(layouts),
                new AddressInstructionEmitter(layouts)),
        }.Cast<IHostCallbackByteArrayArgumentMarshaller>().Single();
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        marshaller.Emit(code, ByteArrayType(), 0, 1, 2, 3, CompleteTarget(), OptionalFunctionIndex.At(7));

        Assert.Contains(WasmOpcodes.I32EqualZero, code.ToArray());
        Assert.Contains(WasmOpcodes.I32LessThanSigned, code.ToArray());
        Assert.Contains(WasmOpcodes.Call, code.ToArray());
    }

    [Fact]
    public void HostObjectMaterializationWrapsANonZeroHandle()
    {
        var layouts = new RecordingLayoutProvider();
        IHostCallbackHostObjectArgumentMarshaller marshaller = new[]
        {
            new HostCallbackHostObjectArgumentMarshaller(
                layouts,
                layouts,
                WasmRuntimeImports.CreateCatalog(),
                new ImplicitExceptionEmitter(layouts),
                new AddressInstructionEmitter(layouts)),
        }.Cast<IHostCallbackHostObjectArgumentMarshaller>().Single();
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        marshaller.Emit(code, CliTypeIdentity.Named(
            new AssemblyIdentity("System.Runtime.InteropServices.JavaScript"),
            "System.Runtime.InteropServices.JavaScript", "JSObject", isValueType: false), 1, 2, OptionalFunctionIndex.At(7));

        var bytes = code.ToArray();
        Assert.Contains(WasmOpcodes.I32EqualZero, bytes);
        Assert.Contains(WasmOpcodes.Call, bytes);
        Assert.Contains(WasmOpcodes.I32Store, bytes);
    }

    [Fact]
    public void RejectsStringMaterializationWithoutPlannedHostHelpers()
    {
        var layouts = new RecordingLayoutProvider();
        var marshaller = new HostCallbackStringArgumentMarshaller(
            layouts,
            WasmRuntimeImports.CreateCatalog(),
            new ImplicitExceptionEmitter(layouts),
            new AddressInstructionEmitter(layouts));
        var target = new InteropMarshallingTarget(new InteropImportPlan(
            [],
            default,
            default,
            default,
            default,
            default,
            default));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ((IHostCallbackStringArgumentMarshaller)marshaller).Emit(
                new EmitterTestSupport.RecordingInstructionWriter(),
                0,
                1,
                2,
                3,
                target, OptionalFunctionIndex.At(7)));

        Assert.Contains("string host result helpers", exception.Message);
    }

    [Fact]
    public void RejectsByteArrayMaterializationWithoutPlannedHostHelpers()
    {
        var layouts = new RecordingLayoutProvider();
        var marshaller = new HostCallbackByteArrayArgumentMarshaller(
                layouts,
                layouts,
                WasmRuntimeImports.CreateCatalog(),
                new ImplicitExceptionEmitter(layouts),
                new AddressInstructionEmitter(layouts));
        var target = new InteropMarshallingTarget(new InteropImportPlan(
            [],
            default,
            default,
            default,
            default,
            default,
            default));
        var arrayType = CliTypeIdentity.SzArray(
            CliTypeIdentity.Primitive("u1", CliValueKind.I4));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ((IHostCallbackByteArrayArgumentMarshaller)marshaller).Emit(
                new EmitterTestSupport.RecordingInstructionWriter(),
                arrayType,
                0,
                1,
                2,
                3,
                target, OptionalFunctionIndex.At(7)));

        Assert.Contains("byte host result helpers", exception.Message);
    }

    [Fact]
    public void RejectsMissingStringCopyHelperAfterLengthHelperIsPresent()
    {
        var layouts = new RecordingLayoutProvider();
        IHostCallbackStringArgumentMarshaller marshaller = new[]
        {
            new HostCallbackStringArgumentMarshaller(
                layouts,
                WasmRuntimeImports.CreateCatalog(),
                new ImplicitExceptionEmitter(layouts),
                new AddressInstructionEmitter(layouts)),
        }.Cast<IHostCallbackStringArgumentMarshaller>().Single();
        var target = new InteropMarshallingTarget(new InteropImportPlan(
            [], OptionalFunctionIndex.At(1), default, default, default, default, default));

        Assert.Throws<InvalidOperationException>(() => marshaller.Emit(
            new EmitterTestSupport.RecordingInstructionWriter(),
            0,
            1,
            2,
            3,
            target, OptionalFunctionIndex.At(7)));
    }

    [Fact]
    public void RejectsMissingByteCopyHelperAfterLengthHelperIsPresent()
    {
        var layouts = new RecordingLayoutProvider();
        IHostCallbackByteArrayArgumentMarshaller marshaller = new[]
        {
            new HostCallbackByteArrayArgumentMarshaller(
                layouts,
                layouts,
                WasmRuntimeImports.CreateCatalog(),
                new ImplicitExceptionEmitter(layouts),
                new AddressInstructionEmitter(layouts)),
        }.Cast<IHostCallbackByteArrayArgumentMarshaller>().Single();
        var target = new InteropMarshallingTarget(new InteropImportPlan(
            [], default, default, default, default, OptionalFunctionIndex.At(1), default));

        Assert.Throws<InvalidOperationException>(() => marshaller.Emit(
            new EmitterTestSupport.RecordingInstructionWriter(),
            ByteArrayType(),
            0,
            1,
            2,
            3,
            target, OptionalFunctionIndex.At(7)));
    }

    private static CliTypeIdentity ByteArrayType() => CliTypeIdentity.SzArray(
        CliTypeIdentity.Primitive("u1", CliValueKind.I4));

    private static InteropMarshallingTarget CompleteTarget() => new(
        new InteropImportPlan(
            [],
            OptionalFunctionIndex.At(1),
            OptionalFunctionIndex.At(2),
            default,
            default,
            OptionalFunctionIndex.At(3),
            OptionalFunctionIndex.At(4)));
}
