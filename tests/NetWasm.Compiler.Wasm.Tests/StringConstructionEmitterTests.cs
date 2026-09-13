using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StringConstructionEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RepeatedCharacterConstructorAllocatesForEachTarget(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = CreateEmitter(layouts);
        var signature = MethodSignatureModel.Create(
            CliValueKind.Void,
            CliValueKind.I4,
            CliValueKind.I4);
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            [CliValueKind.I4, CliValueKind.I4]);

        emitter.Emit(request, GetCodeWriter(request), signature, 0);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        var code = GetCodeBytes(request);
        Assert.Contains(WasmOpcodes.Call, code);
        Assert.Contains(target == WasmTarget.Wasm64
            ? WasmOpcodes.I64EqualZero
            : WasmOpcodes.I32EqualZero, code);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CharacterArrayConstructorCopiesEveryCharacterForEachTarget(
        WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = CreateEmitter(layouts);
        var signature = MethodSignatureModel.Create(
            CliTypeIdentity.Primitive("void", CliValueKind.Void),
            CliTypeIdentity.SzArray(
                CliTypeIdentity.Primitive("char", CliValueKind.I4)));
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            [CliValueKind.ManagedReference]);

        emitter.Emit(request, GetCodeWriter(request), signature, 0);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        var code = GetCodeBytes(request);
        Assert.Contains(WasmOpcodes.MemoryCopy, code);
        Assert.Contains(target == WasmTarget.Wasm64
            ? WasmOpcodes.I64Multiply
            : WasmOpcodes.I32Multiply, code);
        Assert.Equal(
            target == WasmTarget.Wasm64,
            code.Contains(WasmOpcodes.I64ExtendI32Unsigned));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CharacterArraySliceConstructorValidatesAndCopiesTheSelectedRange(
        WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = CreateEmitter(layouts);
        var signature = MethodSignatureModel.Create(
            CliTypeIdentity.Primitive("void", CliValueKind.Void),
            CliTypeIdentity.SzArray(
                CliTypeIdentity.Primitive("char", CliValueKind.I4)),
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4]);

        emitter.Emit(request, GetCodeWriter(request), signature, 0);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        var code = GetCodeBytes(request);
        Assert.Contains(WasmOpcodes.MemoryCopy, code);
        Assert.Contains(WasmOpcodes.I32Subtract, code);
        Assert.Contains(WasmOpcodes.I32GreaterThanSigned, code);
        Assert.Contains(target == WasmTarget.Wasm64
            ? WasmOpcodes.I64Multiply
            : WasmOpcodes.I32Multiply, code);
        Assert.Equal(
            target == WasmTarget.Wasm64,
            code.Contains(WasmOpcodes.I64ExtendI32Unsigned));
    }

    [Fact]
    public void UnsupportedStringConstructorHasDeterministicDiagnostic()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = CreateEmitter(layouts);
        var signature = MethodSignatureModel.Create(
            CliValueKind.Void,
            CliValueKind.ManagedReference);
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            [CliValueKind.ManagedReference]);

        var exception = Assert.Throws<CompilerException>(() => emitter.Emit(
            request,
            GetCodeWriter(request),
            signature,
            0));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("System.String constructor", exception.Message);
    }

    [Fact]
    public void NonCharacterArrayConstructorHasDeterministicDiagnostic()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = CreateEmitter(layouts);
        var signature = MethodSignatureModel.Create(
            CliTypeIdentity.Primitive("void", CliValueKind.Void),
            CliTypeIdentity.SzArray(
                CliTypeIdentity.Primitive("i4", CliValueKind.I4)));
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            [CliValueKind.ManagedReference]);

        var exception = Assert.Throws<CompilerException>(() => emitter.Emit(
            request,
            GetCodeWriter(request),
            signature,
            0));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
    }

    [Fact]
    public void ParameterlessStringConstructorHasDeterministicDiagnostic()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = CreateEmitter(layouts);
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        var request = CreateInstructionRequest(CilOperation.NewObject);

        var exception = Assert.Throws<CompilerException>(() => emitter.Emit(
            request,
            GetCodeWriter(request),
            signature,
            0));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
    }

    [Fact]
    public void UnknownConstructionSourceKindFailsDeterministically()
    {
        var layouts = new RecordingLayoutProvider();
        var plans = new FixedStringConstructionPlanResolver(new StringConstructionPlan(
            (StringConstructionSourceKind)int.MaxValue,
            null,
            null));
        var emitter = CreateEmitter(layouts, plans);
        var signature = MethodSignatureModel.Create(
            CliValueKind.Void,
            CliValueKind.I4,
            CliValueKind.I4);
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            [CliValueKind.I4, CliValueKind.I4]);

        Assert.Throws<InvalidOperationException>(() => emitter.Emit(
            request,
            GetCodeWriter(request),
            signature,
            0));
    }

    private static IStringConstructionEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        IStringConstructionPlanResolver? plans = null)
    {
        IAddressInstructionEmitter addresses = new AddressInstructionEmitter(layouts);
        IImplicitExceptionEmitter exceptions = new ImplicitExceptionEmitter(
            layouts,
            layouts,
            7);
        return Assert.IsAssignableFrom<IStringConstructionEmitter>(new StringConstructionEmitter(
            layouts,
            addresses,
            layouts,
            layouts,
            WasmRuntimeImports.CreateCatalog(),
            exceptions,
            plans ?? new StringConstructionPlanResolver()));
    }

    private sealed class FixedStringConstructionPlanResolver(
        StringConstructionPlan plan) : IStringConstructionPlanResolver
    {
        public StringConstructionPlan Resolve(
            MethodSignatureModel signature,
            int instructionOffset) => plan;
    }
}
