using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ExceptionAndReturnEmitterTests
{
    [Fact]
    public void ThrowChecksNullBeginsDispatchAndClearsStack()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = CreateEmitter(imports);
        var request = CreateRequest(
            CilOperation.Throw,
            [CliValueKind.ManagedReference]);

        Emit(emitter, request);

        Assert.Empty(request.Stack);
        Assert.Contains(WasmOpcodes.Throw, GetCodeBytes(request));
        Assert.Contains(
            checked((byte)imports.Resolve(RuntimeImportSymbol.BeginThrow)),
            GetCodeBytes(request));
    }

    [Fact]
    public void EndFilterRequiresFilterContextAndSingleValue()
    {
        var invalid = CreateRequest(CilOperation.EndFilter, [CliValueKind.I4]);
        var validRequest = CreateRequest(CilOperation.EndFilter, [CliValueKind.I4]);
        var validCode = GetCodeWriter(validRequest);
        var valid = validRequest with
        {
            Context = CreateMethodEmissionContext(2) with { IsFilterFunclet = true },
        };
        var emitter = CreateEmitter();

        Assert.Throws<InvalidOperationException>(() =>
            Emit(emitter, invalid));
        Emit(emitter, valid, validCode);

        Assert.Empty(valid.Stack);
        Assert.Contains(WasmOpcodes.Return, GetCodeBytes(validRequest));
    }

    [Fact]
    public void ReturnLeavesFramesAndReturnsScalar()
    {
        var request = CreateRequest(CilOperation.Return, [CliValueKind.I4]);

        Emit(CreateEmitter(), request);

        Assert.Empty(request.Stack);
        Assert.Contains(WasmOpcodes.Return, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(CliValueKind.ManagedAddress, CliValueKind.NativeInt)]
    [InlineData(CliValueKind.NativeInt, CliValueKind.ManagedAddress)]
    public void Memory64ReturnReadsTheActualCompatibleAddressLocal(
        CliValueKind declared,
        CliValueKind actual)
    {
        var program = new FakeProgram();
        var definition = program.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(declared),
        };
        var context = CreateMethodEmissionContext();
        var request = WithBody(
            CreateInstructionRequest(
                CilOperation.Return,
                [actual],
                context: context),
            new CilMethodBody(definition, 2, [], []));

        Emit(CreateEmitter(target: WasmTargetLayout.Wasm64), request);

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions();
        var actualLocal = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            context.StackLocals,
            0,
            actual,
            WasmTargetLayout.Wasm64);
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand.UnsignedValue == (uint)actualLocal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RethrowHonorsFrameExitPolicyAndClearsStack(bool leaveFrame)
    {
        var request = CreateRequest(CilOperation.Rethrow, [CliValueKind.I4]);
        var actual = request with
        {
            Context = request.Context with { LeaveFrameOnRethrow = leaveFrame },
        };
        RegisterInstructionWriter(actual,
            new EmitterTestSupport.RecordingInstructionWriter());

        Emit(CreateEmitter(), actual);

        Assert.Empty(actual.Stack);
        Assert.Contains(WasmOpcodes.Throw, GetCodeBytes(actual));
    }

    [Fact]
    public void EndFinallyOwnsCommandAndWritesNothing()
    {
        var request = CreateRequest(CilOperation.EndFinally, []);

        Emit(CreateEmitter(), request);

        Assert.Empty(GetCodeBytes(request));
    }

    [Fact]
    public void EndFinallyRejectsMissingRequest()
    {
        var command = CreateEmitter().Commands.Single(item =>
            item.Operation == CilOperation.EndFinally);

        Assert.Throws<ArgumentNullException>(() => command.Emit(
            null!,
            new EmitterTestSupport.RecordingInstructionWriter(),
            CreateFunctionIndexResolver()));
    }

    [Fact]
    public void EndFilterRejectsWrongStackDepthInsideFilter()
    {
        var request = CreateRequest(CilOperation.EndFilter, []) with
        {
            Context = CreateMethodEmissionContext(2) with { IsFilterFunclet = true },
        };
        RegisterInstructionWriter(request,
            new EmitterTestSupport.RecordingInstructionWriter());

        Assert.Throws<InvalidOperationException>(() => Emit(CreateEmitter(), request));
    }

    [Fact]
    public void ReturnCopiesValueTypeAndSupportsVoidAndMethodInstanceSignatures()
    {
        var program = new FakeProgram();
        var definition = program.GetMethod(EntryKey);
        var valueType = CliTypeIdentity.Named(Assembly, "Test", "Value",
            isValueType: true, CliValueKind.ValueType);
        var valueRequest = WithBody(CreateRequest(CilOperation.Return,
            [CliValueKind.ValueType]), new CilMethodBody(definition with
            {
                Signature = MethodSignatureModel.Create(valueType),
            }, 2, [], []));
        var voidRequest = WithBody(CreateRequest(CilOperation.Return, []),
            new CilMethodBody(definition with
            {
                Signature = MethodSignatureModel.Create(CliValueKind.Void),
            }, 2, [], []));
        var instance = new MethodInstanceModel(definition,
            CliTypeIdentity.Named(Assembly, "Test", "Generic", isValueType: false),
            [], MethodSignatureModel.Create(CliValueKind.I8));
        var instanceRequest = WithBody(CreateRequest(CilOperation.Return,
            [CliValueKind.I8]), new CilMethodBody(definition, 2, [], [])
            {
                MethodInstance = instance,
            });

        Emit(CreateEmitter(), valueRequest);
        Emit(CreateEmitter(), voidRequest);
        Emit(CreateEmitter(), instanceRequest);

        Assert.Contains(WasmOpcodes.Prefixed, GetCodeBytes(valueRequest));
        Assert.Contains(WasmOpcodes.Return, GetCodeBytes(voidRequest));
        Assert.Contains(WasmOpcodes.Return, GetCodeBytes(instanceRequest));
    }

    [Fact]
    public void ExposesEachExceptionAndReturnCommandOnce()
    {
        Assert.Equal(
            [CilOperation.Throw, CilOperation.Rethrow, CilOperation.EndFilter,
                CilOperation.EndFinally, CilOperation.Return],
            CreateEmitter().Commands.Select(command => command.Operation));
    }

    private static IInstructionCommandProvider CreateEmitter(
        RuntimeImportCatalog? imports = null,
        WasmTargetLayout? target = null)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(target ?? WasmTargetLayout.Wasm32);
        var actualImports = imports ?? WasmRuntimeImports.CreateCatalog();
        var types = CreateArgumentSignatureTypes(program);
        var exceptions = new ImplicitExceptionEmitter(
            layouts,
            layouts,
            actualImports.Resolve(RuntimeImportSymbol.BeginThrow));
        var addresses = CreateAddressInstructions(layouts);
        return new[]
        {
            new ExceptionAndReturnEmitter(layouts, layouts, actualImports,
                exceptions, CreateMethodFrameExit(actualImports),
                CreateFilterEnvironmentRoots(layouts),
                addresses),
        }.Cast<IInstructionCommandProvider>().Single();
    }

    private static InstructionEmissionRequest CreateRequest(
        CilOperation operation,
        IEnumerable<CliValueKind> stack) =>
        CreateInstructionRequest(operation, stack, maxStack: 2);

    private static InstructionEmissionRequest WithBody(
        InstructionEmissionRequest request,
        CilMethodBody body)
    {
        var actual = request with { Header = Header(body) };
        RegisterInstructionWriter(actual,
            new EmitterTestSupport.RecordingInstructionWriter());
        return actual;
    }

    private static void Emit(IInstructionCommandProvider provider,
        InstructionEmissionRequest request,
        IWasmInstructionWriter? writer = null)
    {
        var command = provider.Commands.Single(item =>
            item.Operation == request.Instruction.Operation);
        command.Emit(request, writer ?? GetCodeWriter(request),
            CreateFunctionIndexResolver());
    }
}
