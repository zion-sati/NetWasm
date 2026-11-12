using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

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
                CallableMethods = Lookups.Whole(ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                    callable.CanonicalName,
                    callable)),
            },
        };

        emitter.Emit(
            request,
            code,
            CreateFunctionIndexResolver(program));

        Assert.Equal([CliValueKind.I4], request.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(originalRequest));
    }

    // Issue 226: an unmanaged calli calls its signature's trampoline with the C ABI arguments and
    // then the function pointer, instead of matching managed candidates.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnmanagedCallPassesItsArgumentsThenThePointerToItsTrampoline(bool memory64)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I8, CliValueKind.I4, CliValueKind.F8);
        var callSites = NativeAbiTestSupport.CallSitePlanner().Plan([new(signature, "M", 0)], 23);
        var request = CreateInstructionRequest(
            CilOperation.CallIndirect,
            [CliValueKind.I4, CliValueKind.F8, CliValueKind.NativeInt],
            new CilOperand.CallSite(signature, Unmanaged: true));
        var original = request;
        request = request with
        {
            Target = request.Target with { NativeImports = NativeImportPlan.Empty with { CallSites = callSites } },
        };

        CreateEmitter(memory64 ? WasmTargetLayout.For(WasmTarget.Wasm64) : null).Emit(
            request, GetCodeWriter(original), CreateFunctionIndexResolver());

        Assert.Equal([CliValueKind.I8], request.Stack);
        var code = GetCodeBytes(original);
        var call = Array.IndexOf(code, WasmOpcodes.Call);
        Assert.Equal((byte)23, code[call + 1]);
        // The three operands, the pointer last, precede the call; nothing compares candidates.
        Assert.Equal(3, code.Take(call).Count(value => value == WasmOpcodes.LocalGet));
        Assert.DoesNotContain(memory64 ? WasmOpcodes.I64Equal : WasmOpcodes.I32Equal, code);
    }

    [Fact]
    public void UnmanagedCallWithoutAPlannedTrampolineIsAnInvariantFailure()
    {
        var request = CreateInstructionRequest(
            CilOperation.CallIndirect,
            [CliValueKind.NativeInt],
            new CilOperand.CallSite(MethodSignatureModel.Create(CliValueKind.Void), Unmanaged: true));

        var error = Assert.Throws<CompilerException>(() =>
            CreateEmitter().Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver()));

        Assert.Equal(DiagnosticCode.CompilerInvariant, error.Diagnostic.Code);
        Assert.Empty(GetCodeBytes(request));
    }

    // A value-type result goes to the call's frame buffer: the buffer's address is the
    // candidate's first argument and, after the call, the evaluation stack's value.
    [Fact]
    public void ValueTypeReturnGoesThroughTheFrameBuffer()
    {
        var program = new FakeProgram();
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true);
        var signature = MethodSignatureModel.Create(valueType);
        var definition = program.GetMethod(EntryKey) with { Signature = signature };
        var callable = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
            [],
            signature);
        var context = CreateMethodEmissionContext(2) with
        {
            ValueLayout = CreateMethodEmissionContext(2).ValueLayout with
            {
                Size = 32,
                TemporaryOffsets = ImmutableDictionary<int, int>.Empty.Add(0, 16),
            },
        };
        var request = CreateInstructionRequest(
            CilOperation.CallIndirect,
            [CliValueKind.NativeInt],
            new CilOperand.CallSite(signature),
            context: context);
        var code = GetCodeWriter(request);
        var originalRequest = request;
        request = request with
        {
            Target = CreateInstructionModuleTarget(program) with
            {
                CallableMethods = Lookups.Whole(ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                    callable.CanonicalName,
                    callable)),
            },
        };

        CreateEmitter().Emit(request, code, CreateFunctionIndexResolver(program));

        Assert.Equal([CliValueKind.ValueType], request.Stack);
        var bytes = GetCodeBytes(originalRequest);
        var frame = (byte)context.ValueFrame;
        // The buffer address (frame + 16) precedes the candidate's call and becomes the result.
        Assert.Equal(2, CountSequence(bytes, [WasmOpcodes.LocalGet, frame, WasmOpcodes.I32Constant, 16, WasmOpcodes.I32Add]));
        Assert.Contains(WasmOpcodes.Call, bytes);
    }

    private static int CountSequence(byte[] bytes, byte[] sequence)
    {
        var count = 0;
        for (var index = 0; index + sequence.Length <= bytes.Length; index++)
        {
            if (bytes.AsSpan(index, sequence.Length).SequenceEqual(sequence)) count++;
        }
        return count;
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
                CallableMethods = Lookups.Whole(new[] { first, second, nonStatic, incompatible }
                    .ToImmutableDictionary(method => method.CanonicalName)),
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
            new ImplicitExceptionEmitter(layouts),
            new ValueFrameAddressEmitter(layouts),
            new NativeAbiCallEmitter(layouts, new NoInitialization(), new AddressInstructionEmitter(layouts),
                new ImplicitExceptionEmitter(layouts)));
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

    private sealed class NoInitialization : IStaticInitializationEmitter
    {
        public void Emit(StaticInitializationEmissionRequest request, IWasmInstructionWriter code,
            IFunctionIndexResolver functionIndices)
        {
        }
    }
}
