using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class CallIndirectEmitterTests
{
    [Fact]
    public void RejectsCallWithoutItsCallSiteSignature()
    {
        var request = CreateInstructionRequest(
            CilOperation.CallIndirect,
            [CliValueKind.NativeInt]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateEmitter().Emit(
                request,
                GetCodeWriter(request),
                CreateFunctionIndexResolver()));

        Assert.Contains("call-site signature", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchingCallableConsumesFunctionPointerAndProducesResult()
    {
        var program = new FakeProgram();
        var callable = CreateCallable(program);
        var emitter = CreateEmitter();
        var request = CreateInstructionRequest(
            CilOperation.CallIndirect,
            [CliValueKind.I4, CliValueKind.NativeInt],
            new CilOperand.CallSite(MethodSignatureModel.Create(
                CliValueKind.I4,
                CliValueKind.I4)));
        var code = GetCodeWriter(request);
        var originalRequest = request;
        request = request with
        {
            Target = CreateInstructionModuleTarget(program) with
            {
                CallableMethods = ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                    callable.CanonicalName,
                    callable),
            },
        };

        emitter.Emit(
            request,
            code,
            CreateFunctionIndexResolver(program));

        Assert.Equal([CliValueKind.I4], request.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(originalRequest));
    }

    [Fact]
    public void ValueTypeReturnIsRejectedDeterministically()
    {
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true);
        var request = CreateInstructionRequest(
            CilOperation.CallIndirect,
            [CliValueKind.NativeInt],
            new CilOperand.CallSite(MethodSignatureModel.Create(valueType)));

        var exception = Assert.Throws<CompilerException>(() =>
            CreateEmitter().Emit(
                request,
                GetCodeWriter(request),
                CreateFunctionIndexResolver()));

        Assert.Equal(DiagnosticCode.UnsupportedCil, exception.Diagnostic.Code);
    }

    [Fact]
    public void Memory64FiltersIncompatibleCallablesAndOrdersMatchingTargets()
    {
        var program = new FakeProgram();
        var definition = program.GetMethod(EntryKey);
        var first = CreateCallable(program);
        var second = Instance(definition, "Second");
        var nonStatic = Instance(program.GetMethod(ConstructorKey), "Instance");
        var incompatible = Instance(
            definition with
            {
                Signature = MethodSignatureModel.Create(CliValueKind.Void),
            },
            "Incompatible");
        var request = CreateInstructionRequest(
            CilOperation.CallIndirect,
            [CliValueKind.I4, CliValueKind.NativeInt],
            new CilOperand.CallSite(definition.Signature));
        var originalRequest = request;
        request = request with
        {
            Target = CreateInstructionModuleTarget(program) with
            {
                CallableMethods = new[] { first, second, nonStatic, incompatible }
                    .ToImmutableDictionary(method => method.CanonicalName),
            },
        };

        CreateEmitter(WasmTargetLayout.Wasm64).Emit(
            request,
            GetCodeWriter(originalRequest),
            CreateFunctionIndexResolver(program));

        Assert.Equal(CliValueKind.I4, Assert.Single(request.Stack));
        Assert.Contains(WasmOpcodes.I64Equal, GetCodeBytes(originalRequest));
    }

    private static CallIndirectEmitter CreateEmitter(
        WasmTargetLayout? target = null)
    {
        var layouts = new RecordingLayoutProvider(target);
        return new CallIndirectEmitter(
            layouts,
            new ImplicitExceptionEmitter(layouts, layouts, 7));
    }

    private static MethodInstanceModel CreateCallable(FakeProgram program)
    {
        var method = program.GetMethod(EntryKey);
        return new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
            [],
            method.Signature);
    }

    private static MethodInstanceModel Instance(
        MethodDefinitionModel definition,
        string declaringTypeName) => new(
        definition,
        CliTypeIdentity.Named(
            Assembly,
            "Test",
            declaringTypeName,
            isValueType: false),
        [],
        definition.Signature);
}
