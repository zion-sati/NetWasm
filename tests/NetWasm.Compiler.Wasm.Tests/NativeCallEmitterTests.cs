using System.Collections.Immutable;
using System.Reflection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class NativeCallEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void IndirectArgumentsAreCopiedBeforeMixedPhysicalArgumentsAndHiddenResultStorage(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var pair = CliTypeIdentity.Named(Assembly, "Test", "Pair", true);
        var empty = CliTypeIdentity.Named(Assembly, "Test", "Empty", true);
        var single = CliTypeIdentity.Named(Assembly, "Test", "Short", true);
        var shortType = CliTypeIdentity.Primitive("i2", CliValueKind.I4);
        var pointer = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
        var doubleType = CliTypeIdentity.FromStackKind(CliValueKind.F8);
        var logical = MethodSignatureModel.Create(pair, empty, single, pair, doubleType, pair);
        var method = Method(logical);
        var indirect = new NativeAbiValuePlan(NativeAbiValueKind.IndirectAggregate, pair, pointer, 16, 8);
        var plan = new NativeAbiPlan(method.Definition.NativeImport!, new(logical,
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.NativeInt, CliValueKind.I4,
                CliValueKind.NativeInt, CliValueKind.F8, CliValueKind.NativeInt),
            [
                new(0, null, new(NativeAbiValueKind.IgnoredAggregate, empty, null, 1)),
                new(1, 1, new(NativeAbiValueKind.ScalarizedAggregate, single, shortType, 2, 2, shortType)),
                new(2, 2, indirect),
                new(3, 3, new(NativeAbiValueKind.Scalar, doubleType, doubleType)),
                new(4, 4, indirect),
            ], indirect, 0));
        var context = CreateMethodEmissionContext(7) with
        {
            ValueLayout = new(64, [], [], ImmutableDictionary<int, int>.Empty.Add(0, 48), [])
            {
                NativeArgumentOffsets = ImmutableDictionary<(int, int), int>.Empty
                    .Add((0, 2), 16).Add((0, 4), 32),
            },
        };
        var instruction = Publish(CreateInstructionRequest(CilOperation.Call,
            new[] { CliValueKind.I4 }.Concat(logical.ParameterTypes), context: context), method, plan);
        var initialization = new RecordingInitialization();
        var writer = new RecordingInstructionWriter();

        new NativeCallEmitter(layouts, initialization, CreateAddressInstructions(layouts))
            .Emit(new(instruction, method, 1, 5), writer, new RecordingIndices());

        var expected = new List<WasmInstruction>();
        Address(16); Get(3, CliValueKind.ValueType); Constant(16); Copy();
        Address(32); Get(5, CliValueKind.ValueType); Constant(16); Copy();
        Address(48);
        Get(2, CliValueKind.ValueType);
        expected.Add(WasmInstruction.WithOperand(WasmOpcodes.I32Load16Signed, WasmInstructionOperand.Memory(1, 0)));
        Address(16); Get(4, CliValueKind.F8); Address(32);
        expected.Add(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned(37)));
        Address(48);
        expected.Add(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)WasmLocalLayoutPlanner.GetEvaluationStackLocal(
                context.StackLocals, 1, CliValueKind.ValueType, layouts.Target))));
        Assert.Equal(expected, writer.ToInstructions());
        Assert.Equal([CliValueKind.I4, CliValueKind.ValueType], instruction.Stack);
        Assert.NotNull(initialization.Request);

        void Address(int offset)
        {
            expected.Add(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)context.ValueFrame)));
            Constant(offset);
            expected.Add(WasmInstruction.NoOperand(target == WasmTarget.Wasm64 ? WasmOpcodes.I64Add : WasmOpcodes.I32Add));
        }
        void Constant(int value) => expected.Add(WasmInstruction.WithOperand(
            target == WasmTarget.Wasm64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant,
            target == WasmTarget.Wasm64 ? WasmInstructionOperand.Signed64(value) : WasmInstructionOperand.Signed(value)));
        void Get(int slot, CliValueKind kind) => expected.Add(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)WasmLocalLayoutPlanner.GetEvaluationStackLocal(context.StackLocals,
                slot, kind, layouts.Target))));
        void Copy() => expected.Add(WasmInstruction.WithOperand(WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
    }

    public static TheoryData<WasmTarget, string, CliValueKind, int, byte, byte, uint> SingletonCalls
    {
        get
        {
            var data = new TheoryData<WasmTarget, string, CliValueKind, int, byte, byte, uint>();
            foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
            {
                data.Add(target, "i1", CliValueKind.I4, 1, WasmOpcodes.I32Load8Signed, WasmOpcodes.I32Store8, 0);
                data.Add(target, "u2", CliValueKind.I4, 2, WasmOpcodes.I32Load16Unsigned, WasmOpcodes.I32Store16, 1);
                data.Add(target, "i4", CliValueKind.I4, 4, WasmOpcodes.I32Load, WasmOpcodes.I32Store, 2);
                data.Add(target, "i8", CliValueKind.I8, 8, WasmOpcodes.I64Load, WasmOpcodes.I64Store, 3);
                data.Add(target, "f4", CliValueKind.F4, 4, WasmOpcodes.F32Load, WasmOpcodes.F32Store, 2);
                data.Add(target, "f8", CliValueKind.F8, 8, WasmOpcodes.F64Load, WasmOpcodes.F64Store, 3);
                data.Add(target, "nativeint", CliValueKind.NativeInt, target == WasmTarget.Wasm64 ? 8 : 4,
                    target == WasmTarget.Wasm64 ? WasmOpcodes.I64Load : WasmOpcodes.I32Load,
                    target == WasmTarget.Wasm64 ? WasmOpcodes.I64Store : WasmOpcodes.I32Store,
                    target == WasmTarget.Wasm64 ? 3u : 2u);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(SingletonCalls))]
    public void SingletonsProjectTypedStorageAndReconstructManagedResults(WasmTarget target,
        string primitive, CliValueKind kind, int size, byte load, byte store, uint alignment)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var aggregate = CliTypeIdentity.Named(Assembly, "Test", "Singleton", true);
        var scalar = CliTypeIdentity.Primitive(primitive, kind);
        var logical = MethodSignatureModel.Create(aggregate, aggregate);
        var method = Method(logical);
        var value = new NativeAbiValuePlan(NativeAbiValueKind.ScalarizedAggregate, aggregate, scalar,
            size, size, scalar);
        var plan = new NativeAbiPlan(method.Definition.NativeImport!, new(logical,
            MethodSignatureModel.Create(scalar, scalar), [new(0, 0, value)], value, null));
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new(size, [], [], ImmutableDictionary<int, int>.Empty.Add(0, 0), []),
        };
        var instruction = Publish(CreateInstructionRequest(CilOperation.Call,
            [CliValueKind.ValueType], context: context), method, plan);
        var writer = new RecordingInstructionWriter();

        new NativeCallEmitter(layouts, new RecordingInitialization(), CreateAddressInstructions(layouts))
            .Emit(new(instruction, method, 0, 1), writer, new RecordingIndices());

        var stackLocal = (uint)WasmLocalLayoutPlanner.GetEvaluationStackLocal(context.StackLocals,
            0, CliValueKind.ValueType, layouts.Target);
        Assert.Equal(new[]
        {
            WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)context.ValueFrame)),
            WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(stackLocal)),
            WasmInstruction.WithOperand(load, WasmInstructionOperand.Memory(alignment, 0)),
            WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned(37)),
            WasmInstruction.WithOperand(store, WasmInstructionOperand.Memory(alignment, 0)),
            WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)context.ValueFrame)),
            WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned(stackLocal)),
        }, writer.ToInstructions());
        Assert.Equal([CliValueKind.ValueType], instruction.Stack);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EmptyAggregatesOmitPhysicalOperandsAndResetManagedResultStorage(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var empty = CliTypeIdentity.Named(Assembly, "Test", "Empty", true);
        var logical = MethodSignatureModel.Create(empty, empty);
        var method = Method(logical);
        var value = new NativeAbiValuePlan(NativeAbiValueKind.IgnoredAggregate, empty, null, 1);
        var plan = new NativeAbiPlan(method.Definition.NativeImport!, new(logical,
            MethodSignatureModel.Create(CliValueKind.Void), [new(0, null, value)], value, null));
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new(1, [], [], ImmutableDictionary<int, int>.Empty.Add(0, 0), []),
        };
        var instruction = Publish(CreateInstructionRequest(CilOperation.Call,
            [CliValueKind.ValueType], context: context), method, plan);
        var writer = new RecordingInstructionWriter();

        new NativeCallEmitter(layouts, new RecordingInitialization(), CreateAddressInstructions(layouts))
            .Emit(new(instruction, method, 0, 1), writer, new RecordingIndices());

        var emitted = writer.ToInstructions();
        Assert.Equal(new[] { WasmOpcodes.Call, WasmOpcodes.LocalGet, WasmOpcodes.I32Constant,
            target == WasmTarget.Wasm64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant,
            WasmOpcodes.Prefixed, WasmOpcodes.LocalGet, WasmOpcodes.LocalSet }, emitted.Select(i => i.Opcode));
        Assert.Equal(WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0), emitted[4].Operand);
        Assert.Equal([CliValueKind.ValueType], instruction.Stack);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingAggregateStorageRejectsBeforeIndexResolutionOrWrites(bool result)
    {
        var layouts = new RecordingLayoutProvider();
        var pair = CliTypeIdentity.Named(Assembly, "Test", "Pair", true);
        var voidType = CliTypeIdentity.FromStackKind(CliValueKind.Void);
        var pointer = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
        var logical = result ? MethodSignatureModel.Create(pair) : MethodSignatureModel.Create(voidType, pair);
        var method = Method(logical);
        var value = new NativeAbiValuePlan(NativeAbiValueKind.IndirectAggregate, pair, pointer, 8, 4);
        var plan = new NativeAbiPlan(method.Definition.NativeImport!, new(logical,
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.NativeInt),
            result ? [] : [new(0, 0, value)],
            result ? value : new(NativeAbiValueKind.Void, voidType, null), result ? 0 : null));
        var instruction = Publish(CreateInstructionRequest(CilOperation.Call,
            result ? [] : [CliValueKind.ValueType]), method, plan);
        var writer = new RecordingInstructionWriter();
        var indices = new RecordingIndices();
        var initialization = new RecordingInitialization();

        var error = Assert.Throws<CompilerException>(() => new NativeCallEmitter(layouts, initialization,
            CreateAddressInstructions(layouts)).Emit(new(instruction, method, 0, result ? 0 : 1), writer, indices));

        Assert.Equal(DiagnosticCode.CompilerInvariant, error.Diagnostic.Code);
        Assert.Empty(writer.ToInstructions());
        Assert.Null(indices.LastMethod);
        Assert.Null(initialization.Request);
        Assert.Equal(result ? [] : new[] { CliValueKind.ValueType }, instruction.Stack);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(4, 0, 0)]
    [InlineData(32, 32, 0)]
    [InlineData(4, 3, 0)]
    [InlineData(4, 4, -4)]
    [InlineData(4, 4, 2)]
    [InlineData(4, 4, 16)]
    public void InvalidPlannedStorageRejectsWithoutAnyEmission(int size, int alignment, int offset)
    {
        var layouts = new RecordingLayoutProvider();
        var pair = CliTypeIdentity.Named(Assembly, "Test", "Pair", true);
        var logical = MethodSignatureModel.Create(pair);
        var method = Method(logical);
        var value = new NativeAbiValuePlan(NativeAbiValueKind.IndirectAggregate, pair,
            CliTypeIdentity.FromStackKind(CliValueKind.NativeInt), size, alignment);
        var plan = new NativeAbiPlan(method.Definition.NativeImport!, new(logical,
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.NativeInt), [], value, 0));
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new(16, [], [], ImmutableDictionary<int, int>.Empty.Add(0, offset), []),
        };
        var instruction = Publish(CreateInstructionRequest(CilOperation.Call, context: context), method, plan);
        var writer = new RecordingInstructionWriter();
        var indices = new RecordingIndices();
        var initialization = new RecordingInitialization();

        var error = Assert.Throws<CompilerException>(() => new NativeCallEmitter(layouts, initialization,
            CreateAddressInstructions(layouts)).Emit(new(instruction, method, 0, 0), writer, indices));

        Assert.Equal(DiagnosticCode.CompilerInvariant, error.Diagnostic.Code);
        Assert.Empty(writer.ToInstructions());
        Assert.Null(indices.LastMethod);
        Assert.Null(initialization.Request);
        Assert.Empty(instruction.Stack);
    }

    [Fact]
    public void ContradictoryDescriptorAndLogicalStackRepresentationRejectBeforeEmission()
    {
        var layouts = new RecordingLayoutProvider();
        var signature = MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I8);
        var method = Method(signature);
        var plan = NativeAbiTestSupport.Plan(method.Definition.NativeImport!, signature, signature);
        var original = Publish(CreateInstructionRequest(CilOperation.Call, [CliValueKind.I8]), method, plan);
        var initialization = new RecordingInitialization();
        var indices = new RecordingIndices();
        var writer = new RecordingInstructionWriter();
        var emitter = new NativeCallEmitter(layouts, initialization, CreateAddressInstructions(layouts));
        var changed = method with { Definition = method.Definition with { Name = "Contradictory" } };

        Assert.Equal(DiagnosticCode.CompilerInvariant, Assert.Throws<CompilerException>(() =>
            emitter.Emit(new(original, changed, 0, 1), writer, indices)).Diagnostic.Code);
        var mismatched = original with { Stack = [CliValueKind.I4] };
        Assert.Equal(DiagnosticCode.CompilerInvariant, Assert.Throws<CompilerException>(() =>
            emitter.Emit(new(mismatched, method, 0, 1), writer, indices)).Diagnostic.Code);
        Assert.Empty(writer.ToInstructions());
        Assert.Null(indices.LastMethod);
        Assert.Null(initialization.Request);
        Assert.Equal([CliValueKind.I8], original.Stack);
        Assert.Equal([CliValueKind.I4], mismatched.Stack);
    }

    public static TheoryData<WasmTarget, CliValueKind> ScalarCalls
    {
        get
        {
            var data = new TheoryData<WasmTarget, CliValueKind>();
            foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
                foreach (var kind in new[] { CliValueKind.Void, CliValueKind.I4, CliValueKind.I8,
                    CliValueKind.F4, CliValueKind.F8, CliValueKind.NativeInt })
                    data.Add(target, kind);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ScalarCalls))]
    public void EmitsRawMixedArgumentsAndReturnsWithoutAHostStatusSlot(WasmTarget target, CliValueKind result)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var signature = MethodSignatureModel.Create(result,
            CliValueKind.I4, CliValueKind.I8, CliValueKind.F4, CliValueKind.F8,
            CliValueKind.NativeInt, CliValueKind.ManagedAddress);
        var method = Method(signature);
        var plan = NativeAbiTestSupport.Plan(method.Definition.NativeImport!, signature, new(
            signature.ReturnSignatureType,
            [.. signature.ParameterSignatureTypes.Take(5), CliTypeIdentity.FromStackKind(CliValueKind.NativeInt)]));
        var initialization = new RecordingInitialization();
        var indices = new RecordingIndices();
        var emitter = Assert.IsAssignableFrom<ICallEmitter>(new NativeCallEmitter(layouts, initialization,
            CreateAddressInstructions(layouts)));
        var instruction = CreateInstructionRequest(CilOperation.Call,
            new[] { CliValueKind.I4 }.Concat(signature.ParameterTypes), maxStack: 8);
        instruction = Publish(instruction, method, plan);
        var writer = new RecordingInstructionWriter();

        emitter.Emit(new(instruction, method, 1, 6), writer, indices);

        var emitted = writer.ToInstructions();
        Assert.Equal(result == CliValueKind.Void ? 7 : 8, emitted.Length);
        for (var index = 0; index < 6; index++)
        {
            Assert.Equal(WasmOpcodes.LocalGet, emitted[index].Opcode);
            Assert.Equal((uint)WasmLocalLayoutPlanner.GetEvaluationStackLocal(instruction.Context.StackLocals,
                index + 1, signature.ParameterTypes[index], layouts.Target), emitted[index].Operand.UnsignedValue);
        }
        Assert.Equal(WasmOpcodes.Call, emitted[6].Opcode);
        Assert.Equal(37u, emitted[6].Operand.UnsignedValue);
        if (result != CliValueKind.Void)
        {
            Assert.Equal(WasmOpcodes.LocalSet, emitted[7].Opcode);
            Assert.Equal((uint)WasmLocalLayoutPlanner.GetEvaluationStackLocal(instruction.Context.StackLocals,
                1, result, layouts.Target), emitted[7].Operand.UnsignedValue);
            Assert.Equal([CliValueKind.I4, result], instruction.Stack);
        }
        else
            Assert.Equal([CliValueKind.I4], instruction.Stack);
        Assert.Same(plan, instruction.Target.NativeImports.ByMethod[method.Definition.Key].Abi);
        Assert.Same(method, indices.LastMethod);
        Assert.Equal(method.Definition.DeclaringType, initialization.Request!.TypeDefinition);
        Assert.Equal(method.DeclaringType, initialization.Request.DeclaringType);
        Assert.True(initialization.Request.IsStaticMethodCall);
    }

    [Theory]
    [InlineData(CilOperation.CallVirtual, 0, 0)]
    [InlineData(CilOperation.Call, -1, 0)]
    [InlineData(CilOperation.Call, 0, 1)]
    [InlineData(CilOperation.Call, 1, 0)]
    public void InvalidArgumentTailRejectsBeforeIndexResolutionInitializationOrWrites(
        CilOperation operation, int argumentBase, int consumed)
    {
        var method = Method(MethodSignatureModel.Create(CliValueKind.Void));
        var initialization = new RecordingInitialization();
        var indices = new RecordingIndices();
        var writer = new RecordingInstructionWriter();
        var instruction = CreateInstructionRequest(operation);
        var layouts = new RecordingLayoutProvider();
        var emitter = Assert.IsAssignableFrom<ICallEmitter>(new NativeCallEmitter(
            layouts, initialization, CreateAddressInstructions(layouts)));

        var error = Assert.Throws<CompilerException>(() => emitter.Emit(
            new(instruction, method, argumentBase, consumed), writer, indices));

        Assert.Equal(DiagnosticCode.CompilerInvariant, error.Diagnostic.Code);
        Assert.Empty(writer.ToInstructions());
        Assert.Empty(instruction.Stack);
        Assert.Null(initialization.Request);
        Assert.Null(indices.LastMethod);
    }

    [Fact]
    public void MissingPlanAndIndexFailuresDoNotPublishPartialInstructions()
    {
        var method = Method(MethodSignatureModel.Create(CliValueKind.Void));
        var instruction = CreateInstructionRequest(CilOperation.Call);
        var error = new CompilerException(new(DiagnosticCode.NativeInterop, "Unsupported ABI."));
        var initialization = new RecordingInitialization();
        var writer = new RecordingInstructionWriter();
        var indices = new RecordingIndices();
        var layouts = new RecordingLayoutProvider();
        var emitter = Assert.IsAssignableFrom<ICallEmitter>(new NativeCallEmitter(
            layouts, initialization, CreateAddressInstructions(layouts)));

        Assert.Equal(DiagnosticCode.CompilerInvariant, Assert.Throws<CompilerException>(() =>
            emitter.Emit(new(instruction, method, 0, 0), writer, indices)).Diagnostic.Code);
        Assert.Null(indices.LastMethod);
        Assert.Null(initialization.Request);
        Assert.Empty(writer.ToInstructions());
        var missing = new RecordingIndices(error);
        instruction = Publish(instruction, method,
            NativeAbiTestSupport.Plan(method.Definition.NativeImport!, method.Signature, method.Signature));
        Assert.Same(error, Assert.Throws<CompilerException>(() =>
            emitter.Emit(new(instruction, method, 0, 0), writer, missing)));
        Assert.Null(initialization.Request);
        Assert.Empty(writer.ToInstructions());
    }

    [Fact]
    public void RequiredDependenciesAndEmissionArgumentsReject()
    {
        var method = Method(MethodSignatureModel.Create(CliValueKind.Void));
        var layouts = new RecordingLayoutProvider();
        var initialization = new RecordingInitialization();
        var addresses = CreateAddressInstructions(layouts);
        Assert.Throws<ArgumentNullException>(() => new NativeCallEmitter(null!, initialization, addresses));
        Assert.Throws<ArgumentNullException>(() => new NativeCallEmitter(layouts, null!, addresses));
        Assert.Throws<ArgumentNullException>(() => new NativeCallEmitter(layouts, initialization, null!));
        var emitter = Assert.IsAssignableFrom<ICallEmitter>(new NativeCallEmitter(layouts, initialization, addresses));
        var request = new CallEmissionRequest(CreateInstructionRequest(CilOperation.Call), method, 0, 0);
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, new RecordingInstructionWriter(), new RecordingIndices()));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(request, null!, new RecordingIndices()));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(request, new RecordingInstructionWriter(), null!));
    }

    private static MethodInstanceModel Method(MethodSignatureModel signature)
    {
        var definition = new MethodDefinitionModel(EntryKey, TypeKey, "Call", true, signature, 0)
        {
            NativeImport = new("mule", "call", MethodImportAttributes.CallingConventionCDecl,
                false, false, false, false),
        };
        return new(definition, CliTypeIdentity.Named(Assembly, "Test", "Native", false), [], signature);
    }

    private static InstructionEmissionRequest Publish(InstructionEmissionRequest instruction,
        MethodInstanceModel method, NativeAbiPlan plan) => instruction with
        {
            Target = instruction.Target with
            {
                NativeImports = new([new(method, plan, new(RuntimeAbi.RuntimeModule, plan.Import.EntryPoint,
                new(plan.Signature.ParameterTypes, plan.Signature.ReturnType)))]),
            },
        };

    private sealed class RecordingInitialization : IStaticInitializationEmitter
    {
        public StaticInitializationEmissionRequest? Request { get; private set; }

        public void Emit(StaticInitializationEmissionRequest request, IWasmInstructionWriter code,
            IFunctionIndexResolver functionIndices) => Request = request;
    }

    private sealed class RecordingIndices(CompilerException? error = null) : IFunctionIndexResolver
    {
        public MethodInstanceModel? LastMethod { get; private set; }
        public int Resolve(EntityKey method) => throw new InvalidOperationException();
        public int Resolve(string method) => throw new InvalidOperationException();

        public int Resolve(MethodInstanceModel method)
        {
            LastMethod = method;
            if (error is not null)
                throw error;
            return 37;
        }
    }
}
