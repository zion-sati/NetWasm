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

    // typeof names its type, so it loads that type's static Type object: no
    // runtime call, no allocation check and no roots to publish.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ConstantMaterializationLoadsTheStaticTypeObject(WasmTarget target)
    {
        var type = CliTypeIdentity.Named(Assembly, "Test", "Named", isValueType: false);
        var rootsPublished = false;
        var emitter = CreateEmitter(new RecordingRootPublicationEmitter(_ => rootsPublished = true), target,
            typeObjects: ImmutableDictionary<string, int>.Empty.Add(type.CanonicalName, 512));
        var request = CreateInstructionRequest(CilOperation.MaterializeType, [CliValueKind.I4],
            new CilOperand.TypeIdentity(type), maxStack: 1);
        var command = ((IInstructionCommandProvider)emitter).Commands.Single(item =>
            item.Operation == CilOperation.MaterializeType);

        command.Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver());

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions();
        Assert.False(rootsPublished);
        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Equal(target == WasmTarget.Wasm32
                ? WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(512))
                : WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(512)),
            instructions[0]);
        Assert.Equal(WasmOpcodes.LocalSet, instructions[1].Opcode);
        Assert.Equal(2, instructions.Length);
    }

    // GetType on a receiver of one possible type checks it for null, then uses
    // the type's static Type object without reading the header or calling.
    [Fact]
    public void ExactObjectTypeChecksForNullAndLoadsTheStaticTypeObject()
    {
        var type = CliTypeIdentity.Named(Assembly, "Test", "Leaf", isValueType: false);
        var imports = WasmRuntimeImports.CreateCatalog();
        var rootsPublished = false;
        var emitter = CreateEmitter(new RecordingRootPublicationEmitter(_ => rootsPublished = true),
            typeObjects: ImmutableDictionary<string, int>.Empty.Add(type.CanonicalName, 768));
        var request = CreateInstructionRequest(CilOperation.GetObjectType, [CliValueKind.ManagedReference],
            new CilOperand.ExactType(type), maxStack: 1);
        var command = ((IInstructionCommandProvider)emitter).Commands.Single(item =>
            item.Operation == CilOperation.GetObjectType);

        command.Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver());

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions();
        Assert.False(rootsPublished);
        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Equal(WasmOpcodes.If, instructions[2].Opcode);
        Assert.Contains(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(768)), instructions);
        Assert.DoesNotContain(instructions, instruction => instruction.Opcode == WasmOpcodes.I32Load ||
            instruction == WasmInstruction.WithOperand(WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)imports.Resolve(RuntimeImportSymbol.GetTypeObject))));
    }

    // GetType on a receiver of unknown type reads the runtime's Type object cache
    // at the receiver's type id, through the word the runtime initializer fills,
    // and asks the runtime only in the branch where the cache or entry is empty.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DynamicObjectTypeReadsTheTypeObjectCacheBeforeAskingTheRuntime(WasmTarget target)
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = CreateEmitter(new RecordingRootPublicationEmitter(_ => { }), target, typeObjectCache: 1024);
        var request = CreateInstructionRequest(CilOperation.GetObjectType, [CliValueKind.ManagedReference], maxStack: 1);
        var command = ((IInstructionCommandProvider)emitter).Commands.Single(item =>
            item.Operation == CilOperation.GetObjectType);

        command.Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver());

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions().ToList();
        var word = instructions.IndexOf(target == WasmTarget.Wasm32
            ? WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(1024))
            : WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(1024)));
        var lookup = instructions.IndexOf(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)imports.Resolve(RuntimeImportSymbol.GetTypeObject))));
        Assert.InRange(word, 0, lookup - 1);
        Assert.Contains(WasmInstruction.NoOperand(WasmOpcodes.Else), instructions[lookup..]);
        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
    }

    // Without its static object a constant materialization has no safe fallback:
    // it is not a safepoint, so the allocating runtime call would leave roots unpublished.
    [Fact]
    public void ConstantMaterializationWithoutAStaticTypeObjectIsACompilerError()
    {
        var type = CliTypeIdentity.Named(Assembly, "Test", "Named", isValueType: false);
        var emitter = CreateEmitter(new RecordingRootPublicationEmitter(_ => { }));
        var request = CreateInstructionRequest(CilOperation.MaterializeType, [CliValueKind.I4],
            new CilOperand.TypeIdentity(type), maxStack: 1);
        var command = ((IInstructionCommandProvider)emitter).Commands.Single(item =>
            item.Operation == CilOperation.MaterializeType);

        var error = Assert.Throws<CompilerException>(() =>
            command.Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver()));
        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
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
        bool typeFacts = true,
        ImmutableDictionary<string, int>? typeObjects = null,
        int typeObjectCache = 0)
    {
        var layouts = new RecordingLayoutProvider(
            target == WasmTarget.Wasm32
                ? WasmTargetLayout.Wasm32
                : WasmTargetLayout.Wasm64)
        {
            TypeFactsTableAddress = typeFacts ? 160 : 0,
            TypeFactsTableCount = typeFacts ? 16 : 0,
            TypeObjectAddresses = typeObjects ?? ImmutableDictionary<string, int>.Empty,
            TypeObjectCacheAddress = typeObjectCache,
        };
        var imports = WasmRuntimeImports.CreateCatalog();
        return new TypeMaterializationEmitter(
            layouts,
            new AddressInstructionEmitter(layouts),
            layouts,
            layouts,
            imports,
            new ImplicitExceptionEmitter(layouts, layouts),
            roots,
            new CilTypeOperandResolver(new NoTypeIdentities()));
    }

    private sealed class NoTypeIdentities : ICilTypeIdentityResolver
    {
        public CliTypeIdentity Resolve(EntityKey key) => throw new InvalidOperationException();
    }
}
