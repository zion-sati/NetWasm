// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class NativeCallbackThunkEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TrapsBeforeBoundaryThenInitializesAndCallsManagedBody(
        WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var imports = WasmRuntimeImports.CreateCatalog();
        var initialization = new RecordingStaticInitializationEmitter();
        var terminal = new RecordingTerminalBoundaryEmitter();
        var writers = new RecordingWriterFactory();
        var callback = CallbackPlan();
        var moduleData = ModuleDataPlan.Empty with
        {
            NativeCallbackReadinessAddress = 256,
            StaticInitializerGuards = ImmutableDictionary<string, StaticInitializerGuard>.Empty.Add("guard", new(240, null, "guard")),
        };
        var selection = new RuntimeImportSelection(
            WasmModuleProfile.CoreApplication,
            IncludeTerminalExceptionReporter: true);
        var emitter = Assert.IsAssignableFrom<INativeCallbackThunkEmitter>(
            new NativeCallbackThunkEmitter(
                layouts,
                imports,
                new AddressInstructionEmitter(layouts),
                initialization,
                terminal,
                writers));

        emitter.Emit(
            callback,
            moduleData,
            selection,
            new FixedFunctionIndexResolver());

        var instructions = writers.Instructions.ToInstructions();
        Assert.Equal(6, terminal.EntryInstructionCount);
        Assert.Equal(
            target == WasmTarget.Wasm32
                ? WasmOpcodes.I32Constant
                : WasmOpcodes.I64Constant,
            instructions[0].Opcode);
        Assert.Equal(WasmOpcodes.I32Load, instructions[1].Opcode);
        Assert.Equal(WasmOpcodes.I32EqualZero, instructions[2].Opcode);
        Assert.Equal(WasmOpcodes.If, instructions[3].Opcode);
        Assert.Equal(WasmOpcodes.Unreachable, instructions[4].Opcode);
        Assert.Equal(WasmOpcodes.End, instructions[5].Opcode);
        Assert.True(initialization.Request!.Guards.TryGetValue("guard", out var guard));
        Assert.Same(moduleData.StaticInitializerGuards["guard"], guard);
        Assert.True(initialization.Request.IsMethodInvocation);
        Assert.Equal(callback.Method.DeclaringType, initialization.Request.DeclaringType);
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue == 44);
        Assert.Equal(
            imports.Resolve(
                RuntimeImportSymbol.ManagedTerminalExceptionReport,
                selection),
            terminal.ReportFunctionIndex);
        Assert.Equal(CliValueKind.I4, terminal.ResultType);
        Assert.Equal(WasmOpcodes.Return, instructions[^2].Opcode);
        Assert.Equal(WasmOpcodes.End, instructions[^1].Opcode);
    }

    [Fact]
    public void MissingReadinessPlanRejectsBeforeEnteringBoundary()
    {
        var layouts = new RecordingLayoutProvider();
        var terminal = new RecordingTerminalBoundaryEmitter();
        var emitter = Assert.IsAssignableFrom<INativeCallbackThunkEmitter>(
            new NativeCallbackThunkEmitter(
                layouts,
                WasmRuntimeImports.CreateCatalog(),
                new AddressInstructionEmitter(layouts),
                new RecordingStaticInitializationEmitter(),
                terminal,
                new RecordingWriterFactory()));

        Assert.Throws<InvalidOperationException>(() => emitter.Emit(
            CallbackPlan(),
            ModuleDataPlan.Empty,
            new(WasmModuleProfile.CoreApplication, true),
            new FixedFunctionIndexResolver()));
        Assert.False(terminal.Entered);
    }

    [Fact]
    public void VoidCallbackDoesNotAllocateAResultLocal()
    {
        var layouts = new RecordingLayoutProvider();
        var terminal = new RecordingTerminalBoundaryEmitter();
        var emitter = new NativeCallbackThunkEmitter(
            layouts,
            WasmRuntimeImports.CreateCatalog(),
            new AddressInstructionEmitter(layouts),
            new RecordingStaticInitializationEmitter(),
            terminal,
            new RecordingWriterFactory());

        emitter.Emit(
            CallbackPlan(CliValueKind.Void),
            ModuleDataPlan.Empty with { NativeCallbackReadinessAddress = 256 },
            new(WasmModuleProfile.CoreApplication, true),
            new FixedFunctionIndexResolver());

        Assert.Equal(CliValueKind.Void, terminal.ResultType);
    }

    // Struct values the C ABI passes by address, as a scalar, or not at all, and vectors, reach the managed method as
    // addresses: a scalar struct and an empty one are first written to a value frame the call holds. A struct result
    // returned through the C caller's buffer is written there; one returned as a scalar, or a vector, goes through the
    // frame, and is read back after the call.
    [Theory]
    [InlineData(NativeAbiValueKind.IndirectAggregate, false)]
    [InlineData(NativeAbiValueKind.ScalarizedAggregate, false)]
    [InlineData(NativeAbiValueKind.IndirectAggregate, true)]
    public void StructsAndVectorsPassThroughAddressesAndTheFrame(NativeAbiValueKind resultKind, bool vectorResult)
    {
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var writers = new RecordingWriterFactory();
        var selection = new RuntimeImportSelection(WasmModuleProfile.CoreApplication, IncludeTerminalExceptionReporter: true);
        var emitter = new NativeCallbackThunkEmitter(layouts, imports, new AddressInstructionEmitter(layouts),
            new RecordingStaticInitializationEmitter(), new RecordingTerminalBoundaryEmitter(), writers);

        emitter.Emit(AggregatePlan(resultKind, vectorResult), ModuleDataPlan.Empty with { NativeCallbackReadinessAddress = 256 },
            selection, new FixedFunctionIndexResolver());

        var instructions = writers.Instructions.ToInstructions();
        var calls = instructions.Where(instruction => instruction.Opcode == WasmOpcodes.Call)
            .Select(instruction => (int)instruction.Operand.UnsignedValue).ToArray();
        var enter = imports.Resolve(RuntimeImportSymbol.ValueFrameEnter, selection);
        var leave = imports.Resolve(RuntimeImportSymbol.ValueFrameLeave, selection);
        Assert.Equal(1, calls.Count(index => index == enter));
        Assert.Equal(1, calls.Count(index => index == leave));
        Assert.Contains(44, calls);
        // A SIMD memory instruction carries its opcode first, beside its memory argument.
        var simd = instructions.Where(instruction => instruction.Opcode == WasmOpcodes.SimdPrefixed &&
                instruction.Operand.Shape == WasmInstructionOperandShape.PrefixedUnsignedLeb128Triple)
            .Select(instruction => instruction.Operand.Alignment).ToArray();
        // The two vector parameters load their values (the 8-byte one into the low lanes); a vector result is stored
        // after the call.
        Assert.Equal(1, simd.Count(opcode => opcode == WasmSimdOpcodes.V128Load));
        Assert.Equal(1, simd.Count(opcode => opcode == WasmSimdOpcodes.V128Load64Zero));
        Assert.Equal(vectorResult ? 1 : 0, simd.Count(opcode => opcode == WasmSimdOpcodes.V128Store));
        // A scalar struct result is read from the frame after the call.
        var afterCall = instructions.SkipWhile(instruction => instruction.Opcode != WasmOpcodes.Call || instruction.Operand.UnsignedValue != 44)
            .Skip(1).ToArray();
        Assert.Equal(resultKind == NativeAbiValueKind.ScalarizedAggregate, afterCall.Any(instruction => instruction.Opcode == WasmOpcodes.I64Load));
    }

    private static NativeCallbackMethodPlan AggregatePlan(NativeAbiValueKind resultKind, bool vectorResult)
    {
        var pair = CliTypeIdentity.Named(EmitterTestSupport.Assembly, "Test", "Pair", true);
        var vector = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(EmitterTestSupport.Assembly, "System.Runtime.Intrinsics", "Vector128`1", true),
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var i4 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var i8 = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
        var pointer = CliTypeIdentity.Primitive("nativeint", CliValueKind.NativeInt);
        var indirectResult = resultKind == NativeAbiValueKind.IndirectAggregate;
        var resultType = vectorResult ? vector : pair;
        var result = indirectResult
            ? new NativeAbiValuePlan(NativeAbiValueKind.IndirectAggregate, resultType, null, 16, 16)
            : new NativeAbiValuePlan(NativeAbiValueKind.ScalarizedAggregate, resultType, i8, 8, 8, i8);
        var hidden = indirectResult ? 1 : 0;
        ImmutableArray<NativeAbiParameterPlan> parameters =
        [
            new(0, hidden, new(NativeAbiValueKind.IndirectAggregate, pair, pointer, 8, 4)),
            new(1, hidden + 1, new(NativeAbiValueKind.ScalarizedAggregate, pair, i4, 4, 4, i4)),
            new(2, null, new(NativeAbiValueKind.IgnoredAggregate, pair, null, 0, 1)),
            new(3, hidden + 2, new(NativeAbiValueKind.IndirectAggregate, vector, pointer, 16, 16)),
            new(4, hidden + 3, new(NativeAbiValueKind.ScalarizedAggregate, vector, i8, 8, 8, i8)),
            new(5, hidden + 4, new(NativeAbiValueKind.Scalar, i4, i4)),
        ];
        CliValueKind[] physical = [.. indirectResult ? [CliValueKind.NativeInt] : Array.Empty<CliValueKind>(),
            CliValueKind.NativeInt, CliValueKind.I4, CliValueKind.NativeInt, CliValueKind.I8, CliValueKind.I4];
        var logical = MethodSignatureModel.Create(resultType.StackKind, CliValueKind.ValueType, CliValueKind.ValueType,
            CliValueKind.ValueType, CliValueKind.V128, CliValueKind.V128, CliValueKind.I4);
        var abi = new NativeAbiSignaturePlan(logical,
            MethodSignatureModel.Create(indirectResult ? CliValueKind.Void : CliValueKind.I8, physical), parameters, result,
            indirectResult ? 0 : null);
        var definition = new FakeProgram().GetMethod(EmitterTestSupport.EntryKey) with
        {
            Signature = logical,
            NativeCallback = new([], null, false, false),
        };
        var method = new MethodInstanceModel(definition,
            CliTypeIdentity.Named(EmitterTestSupport.Assembly, "Test", "Callbacks", false), [], logical);
        return new(method, abi, "native", "native", "thunk", "getter", new(3));
    }

    private static NativeCallbackMethodPlan CallbackPlan(
        CliValueKind returnType = CliValueKind.I4)
    {
        var program = new FakeProgram();
        var signature = MethodSignatureModel.Create(
            returnType,
            CliValueKind.I4,
            CliValueKind.NativeInt);
        var definition = program.GetMethod(EmitterTestSupport.EntryKey) with
        {
            Signature = signature,
            NativeCallback = new([], null, false, false),
        };
        var method = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(
                EmitterTestSupport.Assembly,
                "Test",
                "Callbacks",
                false),
            [],
            signature);
        var abi = NativeAbiTestSupport.ScalarSignaturePlanner().Plan(
            signature,
            NativeAbiSignatureKind.Callback,
            method.CanonicalName);
        return new(method, abi, "native", "native", "thunk", "getter", new(3));
    }

    private sealed class RecordingWriterFactory : IGeneratedFunctionWriterFactory
    {
        public EmitterTestSupport.RecordingInstructionWriter Instructions { get; } = new();

        public GeneratedFunctionWriterLease Create()
        {
            var buffer = new WasmBinaryBuffer();
            return new(
                new WasmBinaryWriter(buffer),
                new WasmBinarySnapshotReader(buffer),
                Instructions);
        }
    }

    private sealed class RecordingStaticInitializationEmitter :
        IStaticInitializationEmitter
    {
        public StaticInitializationEmissionRequest? Request { get; private set; }

        public void Emit(
            StaticInitializationEmissionRequest request,
            IWasmInstructionWriter code,
            IFunctionIndexResolver functionIndices)
        {
            Request = request;
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned(91)));
        }
    }

    private sealed class RecordingTerminalBoundaryEmitter :
        IManagedTerminalExceptionBoundaryEmitter
    {
        public bool Entered { get; private set; }
        public int EntryInstructionCount { get; private set; }
        public int ReportFunctionIndex { get; private set; }
        public CliValueKind ResultType { get; private set; }

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
            Action emitBody)
        {
            Entered = true;
            EntryInstructionCount = ((EmitterTestSupport.RecordingInstructionWriter)code)
                .ToInstructions().Length;
            ReportFunctionIndex = reportFunctionIndex;
            ResultType = resultType;
            emitBody();
        }
    }

    private sealed class FixedFunctionIndexResolver : IFunctionIndexResolver
    {
        public OptionalFunctionIndex ManagedThrowFunction => OptionalFunctionIndex.At(7);

        public int Resolve(InteropImportKey import) => Resolve(import.Method);

        public int Resolve(EntityKey method) => 44;
        public int Resolve(string method) => 44;
        public int Resolve(MethodInstanceModel method) => 44;
    }
}
