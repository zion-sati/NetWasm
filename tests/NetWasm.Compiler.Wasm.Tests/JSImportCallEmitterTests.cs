// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class JSImportCallEmitterTests
{
    [Fact]
    public void AcceptsOnlyMethodsDeclaredAsJSImport()
    {
        var program = new FakeProgram();
        var ordinary = CreateMethodInstance(program.GetMethod(EntryKey));
        var imported = CreateMethodInstance(program.GetMethod(EntryKey) with
        {
            JSImport = new("run", "tests"),
        });
        var instruction = CreateInstructionRequest(CilOperation.Call);
        var resolver = new CallEmissionKindResolver(program, new FakeIntrinsics());

        Assert.Equal(
            CallEmissionKind.Direct,
            resolver.Resolve(
                new(instruction, ordinary, 0, 0),
                GetCodeWriter(instruction)));
        Assert.Equal(
            CallEmissionKind.JavaScriptImport,
            resolver.Resolve(
                new(instruction, imported, 0, 0),
                GetCodeWriter(instruction)));
    }

    [Fact]
    public void ForwardsTheCallSiteStackAsTheInvocationArgumentSource()
    {
        var program = new FakeProgram();
        var method = CreateMethodInstance(program.GetMethod(EntryKey) with
        {
            JSImport = new("run", "tests"),
        });
        var context = CreateMethodEmissionContext();
        var instruction = CreateInstructionRequest(
            CilOperation.Call,
            [CliValueKind.I4, CliValueKind.ManagedReference, CliValueKind.I4],
            new CilOperand.Entity(EntryKey),
            context);
        var invocations = new RecordingInvocationEmitter();
        var code = new RecordingInstructionWriter();
        var functionIndices = CreateFunctionIndexResolver(program);
        var emitter = Assert.IsAssignableFrom<ICallEmitter>(new JSImportCallEmitter(invocations));

        emitter.Emit(new CallEmissionRequest(instruction, method, 1, 2), code, functionIndices);

        var invocation = Assert.Single(invocations.Invocations);
        Assert.Same(method, invocation.Method);
        Assert.Same(context, invocation.Context);
        Assert.Same(instruction.Stack, invocation.Stack);
        Assert.Equal(1, invocation.ArgumentBase);
        Assert.Equal(2, invocation.Consumed);
        Assert.Same(instruction.Target, invocation.Target);
        Assert.Same(code, invocations.Code);
        Assert.Same(functionIndices, invocations.FunctionIndices);
    }

    private static MethodInstanceModel CreateMethodInstance(MethodDefinitionModel method) => new(
        method,
        CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
        [],
        method.Signature);

    private sealed class RecordingInvocationEmitter : IJSImportInvocationEmitter
    {
        public List<JSImportInvocation> Invocations { get; } = [];
        public IWasmInstructionWriter? Code { get; private set; }
        public IFunctionIndexResolver? FunctionIndices { get; private set; }

        public void Emit(
            JSImportInvocation invocation,
            IWasmInstructionWriter code,
            IFunctionIndexResolver functionIndices)
        {
            Invocations.Add(invocation);
            Code = code;
            FunctionIndices = functionIndices;
        }
    }
}
