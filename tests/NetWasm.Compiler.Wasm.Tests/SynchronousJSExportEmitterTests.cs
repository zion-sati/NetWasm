// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class SynchronousJSExportEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32EqualZero, false, false, false)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64EqualZero, false, false, false)]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32EqualZero, false, false, true)]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32EqualZero, true, false, false)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64EqualZero, true, true, false)]
    public void ScalarVoidExportRunsFinalizersWithoutRooting(
        WasmTarget target,
        byte expectedEqualZero,
        bool includeReporter,
        bool includeRaise,
        bool includeHint)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var imports = WasmRuntimeImports.CreateCatalog();
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(
                CliValueKind.Void,
                CliValueKind.I4),
        };
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(
                EntryKey,
                new(30)),
            [],
            [],
            []);
        var runtime = new RecordingRuntimeInitializer();
        var strings = new RecordingStringMarshaller();
        var byteArrays = new RecordingByteArrayMarshaller();
        var terminal = new RecordingTerminalBoundary();
        var emitter = new SynchronousJSExportEmitter(
            layouts,
            imports,
            runtime,
            new JavaScriptReferenceArgumentEmitter(strings, byteArrays, new RecordingHostObjectMarshaller()),
            EmitterTestSupport.ValueFramePlanner(layouts),
            EmitterTestSupport.ValueFrames(layouts, imports),
            terminal,
            terminal,
            new AddressInstructionEmitter(layouts),
            new GeneratedFunctionWriterFactory(), new NetWasm.Compiler.Wasm.Emission.StructResults.StackArgumentConventionFactory(), new NetWasm.Compiler.Wasm.Emission.StructResults.BufferResultConventionFactory());
        var selection = new RuntimeImportSelection(
            WasmModuleProfile.CoreApplication,
            IncludeTerminalExceptionReporter: includeReporter,
            IncludeTerminalExceptionRaise: includeRaise,
            IncludeFinalization: true,
            IncludeUnhandledExceptionHint: includeHint);
        var initialization = TestRuntimeInitialization.Create(512, selection);

        var body = emitter.Emit(
            method,
            initialization,
            hasFinalizers: true,
            new FunctionIndexResolver(program, program, indices),
            EmptyInteropImports());

        Assert.True(runtime.Called);
        Assert.False(strings.Called);
        Assert.False(byteArrays.Called);
        Assert.Equal(includeReporter, terminal.ReportCalled);
        Assert.Equal(!includeReporter, terminal.TrapCalled);
        // Without the reporter, a trap boundary hints at diagnostics when the hint's import is in the module.
        Assert.Equal(includeHint
            ? imports.Resolve(RuntimeImportSymbol.ReportUnhandledException, selection)
            : (int?)null, terminal.UnhandledExceptionReporter);
        Assert.Equal(includeRaise
            ? imports.Resolve(RuntimeImportSymbol.ManagedTerminalExceptionRaise, selection)
            : (int?)null, terminal.RaiseFunctionIndex);
        if (includeReporter)
            Assert.Equal(imports.Resolve(RuntimeImportSymbol.ManagedTerminalExceptionReport, selection),
                terminal.ReportFunctionIndex);
        Assert.True(body.AsSpan().IndexOf(new byte[]
        {
            WasmOpcodes.LocalGet,
            0,
            WasmOpcodes.Call,
            30,
        }) >= 0);
        Assert.True(body.AsSpan().IndexOf(new byte[]
        {
            WasmOpcodes.Call,
            (byte)imports.Resolve(
                RuntimeImportSymbol.FinalizerSafepoint,
                initialization.RuntimeImportSelection),
        }) >= 0);
        Assert.Contains(expectedEqualZero, body);
        Assert.Contains(WasmOpcodes.Throw, body);
        Assert.DoesNotContain(
            (byte)imports.Resolve(RuntimeImportSymbol.RootFrameEnter),
            Calls(body));
        Assert.DoesNotContain(
            (byte)imports.Resolve(RuntimeImportSymbol.RootFrameLeave),
            Calls(body));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void JSObjectExportWrapsParametersAndReturnsTheResultHandle(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var imports = WasmRuntimeImports.CreateCatalog();
        var program = new FakeProgram();
        var jsObject = CliTypeIdentity.Named(
            Assembly, "System.Runtime.InteropServices.JavaScript", "JSObject", isValueType: false);
        var method = program.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(jsObject, CliTypeIdentity.FromStackKind(CliValueKind.I4), jsObject),
        };
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(EntryKey, new(30)), [], [], []);
        var hostObjects = new RecordingHostObjectMarshaller();
        var terminal = new RecordingTerminalBoundary();
        var emitter = new SynchronousJSExportEmitter(
            layouts,
            imports,
            new RecordingRuntimeInitializer(),
            new JavaScriptReferenceArgumentEmitter(
                new RecordingStringMarshaller(), new RecordingByteArrayMarshaller(), hostObjects),
            EmitterTestSupport.ValueFramePlanner(layouts),
            EmitterTestSupport.ValueFrames(layouts, imports),
            terminal,
            terminal,
            new AddressInstructionEmitter(layouts),
            new GeneratedFunctionWriterFactory(), new NetWasm.Compiler.Wasm.Emission.StructResults.StackArgumentConventionFactory(), new NetWasm.Compiler.Wasm.Emission.StructResults.BufferResultConventionFactory());

        var body = emitter.Emit(
            method,
            TestRuntimeInitialization.Create(512),
            hasFinalizers: false,
            new FunctionIndexResolver(program, program, indices),
            EmptyInteropImports());

        Assert.Equal([(jsObject, 1)], hostObjects.Calls);
        // The parameter wrapper is rooted; the result leaves as its i32 handle.
        Assert.Equal(CliValueKind.I4, terminal.ResultType);
        Assert.Contains(WasmOpcodes.I32Load, body);
        Assert.Single(Calls(body), call => call == (byte)imports.Resolve(RuntimeImportSymbol.RootFrameEnter));
    }

    // A synchronous export's catch gives the exception leaving it to CoreLib's OfferExportFailure, which gives a
    // JSException's JavaScript error to the host; without that conversion the boundary has no observer.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExportCatchGivesItsExceptionToTheExportFailureOffer(bool hasOffer)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm32);
        var imports = WasmRuntimeImports.CreateCatalog();
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4),
        };
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(EntryKey, new(30)), [], [], []);
        var terminal = new RecordingTerminalBoundary();
        var emitter = new SynchronousJSExportEmitter(
            layouts,
            imports,
            new RecordingRuntimeInitializer(),
            new JavaScriptReferenceArgumentEmitter(
                new RecordingStringMarshaller(), new RecordingByteArrayMarshaller(), new RecordingHostObjectMarshaller()),
            EmitterTestSupport.ValueFramePlanner(layouts),
            EmitterTestSupport.ValueFrames(layouts, imports),
            terminal,
            terminal,
            new AddressInstructionEmitter(layouts),
            new GeneratedFunctionWriterFactory(), new NetWasm.Compiler.Wasm.Emission.StructResults.StackArgumentConventionFactory(), new NetWasm.Compiler.Wasm.Emission.StructResults.BufferResultConventionFactory());
        var interop = EmptyInteropImports() with
        {
            Conversions = hasOffer
                ? ImmutableDictionary<string, int>.Empty.Add(JavaScriptConversion.ExportFailure, 77)
                : ImmutableDictionary<string, int>.Empty,
        };

        emitter.Emit(
            method,
            TestRuntimeInitialization.Create(512, new RuntimeImportSelection(
                WasmModuleProfile.CoreApplication, IncludeTerminalExceptionReporter: true)),
            hasFinalizers: false,
            new FunctionIndexResolver(program, program, indices),
            interop);

        Assert.True(terminal.ReportCalled);
        Assert.Equal(hasOffer ? 77 : null, terminal.ExceptionObserver);
    }

    private static InteropImportPlan EmptyInteropImports() => new(
        [],
        OptionalFunctionIndex.Missing,
        OptionalFunctionIndex.Missing,
        OptionalFunctionIndex.Missing,
        OptionalFunctionIndex.Missing);

    private static byte[] Calls(byte[] body) => [.. body
        .Select((value, index) => (value, index))
        .Where(item =>
            item.value == WasmOpcodes.Call &&
            item.index + 1 < body.Length)
        .Select(item => body[item.index + 1])];

    private sealed class RecordingRuntimeInitializer : IEntryRuntimeInitializer
    {
        public bool Called { get; private set; }

        public void Initialize(
            GeneratedFunctionWriterLease code,
            RuntimeInitializationPlan initialization) => Called = true;
    }

    private sealed class RecordingStringMarshaller :
        IHostCallbackStringArgumentMarshaller
    {
        public bool Called { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            int handleParameter,
            int destination,
            int temporaryI4,
            int objectTemporary,
            InteropMarshallingTarget target,
            OptionalFunctionIndex throwFunction) => Called = true;
    }

    private sealed class RecordingByteArrayMarshaller :
        IStagedValueArrayEmitter
    {
        public bool Called { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            CliTypeIdentity arrayType,
            int handleParameter,
            int lengthLocal,
            int destination,
            int objectTemporary,
            OptionalFunctionIndex throwFunction) => Called = true;
    }

    private sealed class RecordingHostObjectMarshaller : IHostCallbackHostObjectArgumentMarshaller
    {
        public List<(CliTypeIdentity Type, int Parameter)> Calls { get; } = [];

        public void Emit(
            IWasmInstructionWriter code,
            CliTypeIdentity objectType,
            int handleParameter,
            int destination,
            OptionalFunctionIndex throwFunction) => Calls.Add((objectType, handleParameter));
    }

    private sealed class RecordingTerminalBoundary :
        IManagedTerminalExceptionBoundaryEmitter,
        IManagedTerminalTrapBoundaryEmitter
    {
        public bool ReportCalled { get; private set; }
        public bool TrapCalled { get; private set; }
        public int? ReportFunctionIndex { get; private set; }
        public int? RaiseFunctionIndex { get; private set; }
        public CliValueKind? ResultType { get; private set; }
        public int? UnhandledExceptionReporter { get; private set; }
        public int? ExceptionObserver { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            CliValueKind resultType,
            int resultLocal,
            Action emitBody,
            int? unhandledExceptionReporter = null)
        {
            TrapCalled = true;
            UnhandledExceptionReporter = unhandledExceptionReporter;
            ResultType = resultType;
            emitBody();
        }

        public void Emit(
            IWasmInstructionWriter code,
            int exceptionLocal,
            int rootFrameLocal,
            int typeIdLocal,
            int messageLocal,
            int messageLengthLocal,
            int stackTraceLocal,
            int stackTraceLengthLocal,
            CliValueKind resultType,
            int resultLocal,
            int reportFunctionIndex,
            int? raiseFunctionIndex,
            Action emitBody,
            int? exceptionObserverFunctionIndex = null)
        {
            ReportCalled = true;
            ExceptionObserver = exceptionObserverFunctionIndex;
            ResultType = resultType;
            ReportFunctionIndex = reportFunctionIndex;
            RaiseFunctionIndex = raiseFunctionIndex;
            emitBody();
        }
    }
}
