using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class TypeMaterializationEmitterTests
{
    [Fact]
    public void TypeIdBecomesManagedTypeReferenceThroughNamedRuntimeImport()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var request = CreateInstructionRequest(
            CilOperation.MaterializeType,
            [CliValueKind.I4],
            maxStack: 1);
        var emitter = CreateEmitter(new RecordingRootPublicationEmitter(_ => { }));
        var command = ((IInstructionCommandProvider)emitter).Commands.Single(item =>
            item.Operation == CilOperation.MaterializeType);

        command.Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver());

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.True(GetCodeBytes(request).AsSpan().IndexOf(
            [WasmOpcodes.Call, (byte)imports.Resolve(RuntimeImportSymbol.GetTypeObject)]) >= 0);
    }

    [Theory]
    [InlineData(CilOperation.MaterializeType, CliValueKind.I4)]
    [InlineData(CilOperation.GetObjectType, CliValueKind.ManagedReference)]
    public void CommandsPublishRootsAndMaterializeManagedTypeReferences(
        CilOperation operation,
        CliValueKind inputKind)
    {
        var rootsPublished = false;
        var emitter = CreateEmitter(new RecordingRootPublicationEmitter(_ =>
            rootsPublished = true));
        var request = CreateInstructionRequest(operation, [inputKind], maxStack: 1);
        var command = ((IInstructionCommandProvider)emitter).Commands.Single(item =>
            item.Operation == operation);

        command.Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver());

        Assert.True(rootsPublished);
        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
    }

    [Fact]
    public void CommandsRejectMissingRequests()
    {
        var emitter = CreateEmitter(new RecordingRootPublicationEmitter(_ => { }));

        foreach (var command in ((IInstructionCommandProvider)emitter).Commands)
        {
            Assert.Throws<ArgumentNullException>(() => command.Emit(
                null!,
                new RecordingInstructionWriter(),
                CreateFunctionIndexResolver()));
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Multiply, WasmOpcodes.I32Load)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Multiply, WasmOpcodes.I64Load)]
    public void TypeFactsLookupUsesBoundedTargetWidthTableWithoutPublishingRoots(
        WasmTarget target,
        byte multiply,
        byte load)
    {
        var rootsPublished = false;
        var request = CreateInstructionRequest(
            CilOperation.GetTypeFacts,
            [CliValueKind.I4],
            maxStack: 1);
        var emitter = CreateEmitter(
            new RecordingRootPublicationEmitter(_ => rootsPublished = true),
            target);

        emitter.Emit(request);

        Assert.False(rootsPublished);
        Assert.Equal([CliValueKind.NativeInt], request.Stack);
        var bytes = GetCodeBytes(request);
        Assert.Contains(WasmOpcodes.I32LessThanUnsigned, bytes);
        Assert.Contains(multiply, bytes);
        Assert.Contains(load, bytes);
        Assert.Equal(
            target == WasmTarget.Wasm64,
            bytes.Contains(WasmOpcodes.I64ExtendI32Unsigned));
    }

    [Fact]
    public void TypeFactsLookupRejectsMissingFinalizedPlan()
    {
        var request = CreateInstructionRequest(
            CilOperation.GetTypeFacts,
            [CliValueKind.I4],
            maxStack: 1);
        var emitter = CreateEmitter(
            new RecordingRootPublicationEmitter(_ => { }),
            typeFacts: false);

        var exception = Assert.Throws<CompilerException>(() => emitter.Emit(request));

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ConstrainedObjectTypeMaterializesFromValueOrReferenceReceivers(
        bool isValueType,
        WasmTarget target)
    {
        var constrainedType = CliTypeIdentity.Named(
            new AssemblyIdentity("in-memory"),
            "Test",
            isValueType ? "Value" : "Reference",
            isValueType);
        var request = CreateInstructionRequest(
            CilOperation.GetObjectType,
            [CliValueKind.ManagedAddress],
            new CilOperand.TypeIdentity(constrainedType),
            CreateMethodEmissionContext(1));
        var emitter = CreateEmitter(
            new RecordingRootPublicationEmitter(_ => { }),
            target);

        emitter.Emit(request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(
            GetCodeBytes(request),
            opcode => opcode == (isValueType
                ? WasmOpcodes.I32Constant
                : target == WasmTarget.Wasm32
                    ? WasmOpcodes.I32Load
                    : WasmOpcodes.I64Load));
    }

    private static TypeMaterializationEmitter CreateEmitter(
        IRootPublicationEmitter roots,
        WasmTarget target = WasmTarget.Wasm32,
        bool typeFacts = true)
    {
        var layouts = new RecordingLayoutProvider(
            target == WasmTarget.Wasm32
                ? WasmTargetLayout.Wasm32
                : WasmTargetLayout.Wasm64)
        {
            TypeFactsTableAddress = typeFacts ? 160 : 0,
            TypeFactsTableCount = typeFacts ? 16 : 0,
        };
        var imports = WasmRuntimeImports.CreateCatalog();
        return new TypeMaterializationEmitter(
            layouts,
            new AddressInstructionEmitter(layouts),
            layouts,
            layouts,
            imports,
            new ImplicitExceptionEmitter(
                layouts,
                layouts,
                imports.Resolve(RuntimeImportSymbol.BeginThrow)),
            roots);
    }
}
