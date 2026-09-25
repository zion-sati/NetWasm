using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using System;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ManagedMethodEmitterTests
{
    [Fact]
    public void SeparateEmissionsUseSeparateMethodContextsAndStableBytes()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey);
        var structured = Structure(
            program,
            method,
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var roots = new MethodRootMap(
            EntryKey,
            [],
            []);
        var emitter = CreateEmitter(program);
        MethodEmissionContext? firstContext = null;
        MethodEmissionContext? secondContext = null;

        var first = EmitThroughContract(
            emitter,
            method,
            structured,
            roots,
            null,
            (code, _, context) =>
            {
                firstContext = context;
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(0)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
            });
        var second = EmitThroughContract(
            emitter,
            method,
            structured,
            roots,
            null,
            (code, _, context) =>
            {
                secondContext = context;
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(0)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
            });

        Assert.NotSame(firstContext, secondContext);
        Assert.Equal(first.Body, second.Body);
        Assert.Same(FilterEnvironmentLayout.Empty, first.FilterEnvironment);
        Assert.Same(FilterEnvironmentLayout.Empty, second.FilterEnvironment);
    }

    [Fact]
    public void UsesConstructedSignatureAndBoundaryWhenRootsArePresent()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(ConstructorKey);
        var structured = Structure(
            program,
            method,
            I(0, CilOperation.Return));
        var roots = new MethodRootMap(
            ConstructorKey,
            ImmutableDictionary<RootSource, int>.Empty.Add(
                new RootSource(RootSourceKind.Local, 0),
                0),
            []);
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "Test", "Constructed", isValueType: false),
            [],
            method.Signature);

        var result = EmitThroughContract(
            CreateEmitter(program),
            method,
            structured,
            roots,
            instance,
            static (code, _, _) => code.Write(
                WasmInstruction.NoOperand(WasmOpcodes.Unreachable)));

        Assert.NotEmpty(result.Body);
        Assert.True(result.WasmInstructionCount > 0);
    }

    [Fact]
    public void ReservesManagedAddressForValueTypeReturn()
    {
        var program = new FakeProgram();
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Result",
            isValueType: true);
        var method = program.GetMethod(ConstructorKey) with
        {
            Signature = MethodSignatureModel.Create(valueType),
        };
        var structured = Structure(
            program,
            method,
            I(0, CilOperation.DefaultValue, new CilOperand.TypeIdentity(valueType)),
            I(1, CilOperation.Return));

        var result = EmitThroughContract(
            CreateEmitter(program),
            method,
            structured,
            new MethodRootMap(ConstructorKey, [], []),
            null,
            static (code, _, _) => code.Write(
                WasmInstruction.NoOperand(WasmOpcodes.Unreachable)));

        Assert.NotEmpty(result.Body);
    }

    [Fact]
    public void ExceptionPayloadRootsRequireAnExceptionalMethodExitBoundary()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey);
        var group = new StructuredExceptionGroup(new(0), null, 0, 1, [], [], [], null, null);
        var structured = Structure(program, method,
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(1, CilOperation.Return)) with
        {
            TopLevelExceptionGroups = [group.Id],
            ExceptionGroups = ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty.Add(group.Id, group),
        };
        var calls = 0;
        EmitThroughContract(CreateEmitter(program), method, structured,
            new MethodRootMap(EntryKey, [], []), null, (code, _, context) =>
            {
                calls++;
                Assert.Equal(0, context.RootMap.SlotCount);
                Assert.Equal(1, context.RootSlotCount);
                Assert.False(context.LeaveFrameOnExceptionalExit);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
            });
        Assert.Equal(1, calls);
    }

    private static ManagedMethodEmitter CreateEmitter(FakeProgram program)
    {
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var types = CreateArgumentSignatureTypes(program);
        var frameEntry = CreateMethodFrameEntry(program, layouts, imports);
        var frameExit = CreateMethodFrameExit(imports);
        return new ManagedMethodEmitter(
            layouts,
            new ManagedMethodFunctionTypeResolver(),
            CreateValueFrameLayoutPlanner(program, layouts),
            new FilterEnvironmentLayoutPlanner(
                layouts,
                layouts,
                types,
                new ExceptionGroupEnumerator()),
            new ExceptionGroupEnumerator(),
            frameEntry,
            new MethodExceptionBoundaryEmitter(
                new ExceptionPayloadBlockEmitter(layouts),
                frameExit),
            new GeneratedFunctionWriterFactory(),
            new InstructionCountingWriterFactory());
    }

    private delegate ManagedMethodEmission MethodEmitterCall(
        IManagedMethodEmitter emitter,
        MethodDefinitionModel method,
        StructuredMethod structured,
        MethodRootMap rootMap,
        MethodInstanceModel? methodInstance,
        Action<IWasmInstructionWriter, StructuredMethod, MethodEmissionContext> emitBody);

    private static readonly MethodEmitterCall EmitThroughContract =
        static (emitter, method, structured, rootMap, methodInstance, emitBody) =>
            emitter.Emit(
                method,
                new ManagedMethodIdentity("Tests.Caller"),
                structured,
                rootMap,
                methodInstance,
                0,
                new RuntimeImportSelection(
                    WasmModuleProfile.CoreApplication,
                    IncludeTerminalExceptionReporter: true),
                emitBody);
}
