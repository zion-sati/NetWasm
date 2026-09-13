using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class AsyncJSImportCallEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, JavaScriptAsyncReturnKind.Task, 0)]
    [InlineData(WasmTarget.Wasm64, JavaScriptAsyncReturnKind.Task, 0)]
    [InlineData(WasmTarget.Wasm32, JavaScriptAsyncReturnKind.ValueTask, 0)]
    [InlineData(WasmTarget.Wasm32, JavaScriptAsyncReturnKind.ValueTask, 4)]
    [InlineData(WasmTarget.Wasm64, JavaScriptAsyncReturnKind.ValueTask, 4)]
    public void EmitsTaskAndValueTaskCallsForEachTargetWidth(
        WasmTarget target,
        JavaScriptAsyncReturnKind returnKind,
        int temporaryOffset)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var valueTaskType = CliTypeIdentity.Named(
            Assembly,
            "System.Threading.Tasks",
            "ValueTask`1",
            isValueType: true,
            CliValueKind.ValueType);
        var definition = program.GetMethod(EntryKey) with
        {
            Signature = returnKind == JavaScriptAsyncReturnKind.Task
                ? MethodSignatureModel.Create(CliValueKind.ManagedReference, CliValueKind.I4)
                : MethodSignatureModel.Create(
                    valueTaskType,
                    CliTypeIdentity.FromStackKind(CliValueKind.I4)),
        };
        var method = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(Assembly, "Test", "Imports", isValueType: false),
            [],
            definition.Signature);
        var taskType = CliTypeIdentity.Named(
            Assembly,
            "System.Threading.Tasks",
            "Task`1",
            isValueType: false);
        var binding = new JavaScriptAsyncMethodBinding(
            EntryKey,
            new(returnKind, CliTypeIdentity.FromStackKind(CliValueKind.I4)),
            taskType,
            method,
            method,
            method);
        if (returnKind == JavaScriptAsyncReturnKind.ValueTask)
        {
            var field = program.GetField(InstanceFieldKey);
            binding = binding with
            {
                ValueTaskTaskField = new FieldInstanceModel(
                    field,
                    valueTaskType,
                    taskType),
            };
        }
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new ValueFrameLayout(
                16,
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, temporaryOffset),
                []),
        };
        var instruction = CreateInstructionRequest(
            CilOperation.Call,
            [CliValueKind.I4],
            new CilOperand.Entity(EntryKey),
            context) with
        {
            Target = CreateInstructionModuleTarget(program) with
            {
                JavaScriptAsyncBindings =
                    ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>.Empty.Add(
                        EntryKey,
                        binding),
            },
        };
        var arguments = new RecordingArgumentEmitter();
        ICallEmitter emitter = new[]
        {
            new AsyncJSImportCallEmitter(
                layouts,
                layouts,
                layouts,
                layouts,
                WasmRuntimeImports.CreateCatalog(),
                arguments,
                new ImplicitExceptionEmitter(layouts, layouts, 7)),
        }.Cast<ICallEmitter>().Single();
        var code = new RecordingInstructionWriter();

        emitter.Emit(
            new CallEmissionRequest(instruction, method, 0, 1),
            code,
            CreateFunctionIndexResolver(program));

        Assert.True(arguments.Called);
        Assert.Equal(
            returnKind == JavaScriptAsyncReturnKind.Task
                ? CliValueKind.ManagedReference
                : CliValueKind.ValueType,
            Assert.Single(instruction.Stack));
        Assert.Contains(WasmOpcodes.Call, code.ToArray());
    }

    private sealed class RecordingArgumentEmitter : IJavaScriptImportArgumentEmitter
    {
        public bool Called { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            CliTypeIdentity type,
            int local,
            MethodEmissionContext context) => Called = true;
    }
}
