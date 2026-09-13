using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ManagedTerminalExceptionBoundaryEmitterTests
{
    [Theory]
    [InlineData(CliValueKind.I4)]
    [InlineData(CliValueKind.I8)]
    [InlineData(CliValueKind.F4)]
    [InlineData(CliValueKind.F8)]
    [InlineData(CliValueKind.NativeInt)]
    [InlineData(CliValueKind.ManagedReference)]
    [InlineData(CliValueKind.ManagedAddress)]
    public void TerminalBoundarySupportsEveryScalarResultKind(CliValueKind resultType)
    {
        EmitTerminalBoundary(WasmTarget.Wasm32, resultType);
        EmitTerminalBoundary(WasmTarget.Wasm64, resultType);
    }

    [Fact]
    public void RejectsUnrepresentableResultKind()
    {
        Assert.Throws<InvalidOperationException>(() =>
            EmitTerminalBoundary(WasmTarget.Wasm32, CliValueKind.ValueType));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TerminalLeafRootsReportsUnrootsAndTerminatesInOrder(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var imports = WasmRuntimeImports.CreateCatalog();
        var program = new FakeProgram();
        var exceptionState = new ExceptionObjectStateReader(
            layouts,
            layouts, new ExceptionFieldLayoutResolver(
            layouts,
            program,
            program,
            layouts));
        var emitter = new ManagedTerminalExceptionBoundaryEmitter(
            layouts,
            imports,
            new ExceptionPayloadBlockEmitter(layouts),
            exceptionState);
        var outputBuffer = new WasmBinaryBuffer();
        var output = new WasmBinaryWriter(outputBuffer);
        var code = new WasmInstructionWriter(output);

        ((IManagedTerminalExceptionBoundaryEmitter)emitter).Emit(
            code,
            exceptionLocal: 0,
            rootFrameLocal: 1,
            typeIdLocal: 2,
            messageLocal: 3,
            messageLengthLocal: 4,
            CliValueKind.Void,
            resultLocal: 0,
            reportFunctionIndex: imports.Resolve(
                RuntimeImportSymbol.ManagedTerminalExceptionReport,
                WasmModuleProfile.CoreApplication),
            () => { });

        var body = new WasmBinarySnapshotReader(outputBuffer).Read();
        var root = body.AsSpan().IndexOf(Call(imports, RuntimeImportSymbol.RootFrameEnter));
        var rootStoreOpcode = target == WasmTarget.Wasm64
            ? WasmOpcodes.I64Store
            : WasmOpcodes.I32Store;
        var rootStore = body.AsSpan(root + 1).IndexOf(rootStoreOpcode) + root + 1;
        var report = body.AsSpan().IndexOf(Call(imports, RuntimeImportSymbol.ManagedTerminalExceptionReport));
        var unroot = body.AsSpan().IndexOf(Call(imports, RuntimeImportSymbol.RootFrameLeave));
        var terminateOffset = body.AsSpan(unroot + 1).IndexOf(WasmOpcodes.Unreachable);

        Assert.True(root >= 0);
        Assert.True(rootStore > root);
        Assert.True(report > rootStore);
        Assert.True(unroot > report);
        Assert.True(terminateOffset >= 0);
    }

    private static byte[] EmitTerminalBoundary(
        WasmTarget target,
        CliValueKind resultType)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var imports = WasmRuntimeImports.CreateCatalog();
        var program = new FakeProgram();
        var exceptionState = new ExceptionObjectStateReader(
            layouts,
            layouts, new ExceptionFieldLayoutResolver(
            layouts,
            program,
            program,
            layouts));
        var emitter = new ManagedTerminalExceptionBoundaryEmitter(
            layouts,
            imports,
            new ExceptionPayloadBlockEmitter(layouts),
            exceptionState);
        var outputBuffer = new WasmBinaryBuffer();
        var output = new WasmBinaryWriter(outputBuffer);
        var code = new WasmInstructionWriter(output);

        ((IManagedTerminalExceptionBoundaryEmitter)emitter).Emit(
            code,
            exceptionLocal: 0,
            rootFrameLocal: 1,
            typeIdLocal: 2,
            messageLocal: 3,
            messageLengthLocal: 4,
            resultType,
            resultLocal: 5,
            reportFunctionIndex: imports.Resolve(
                RuntimeImportSymbol.ManagedTerminalExceptionReport,
                WasmModuleProfile.CoreApplication),
            static () => { });

        return new WasmBinarySnapshotReader(outputBuffer).Read();
    }

    private static byte[] Call(
        RuntimeImportCatalog imports,
        RuntimeImportSymbol symbol) =>
        [WasmOpcodes.Call, (byte)imports.Resolve(symbol, WasmModuleProfile.CoreApplication)];
}
