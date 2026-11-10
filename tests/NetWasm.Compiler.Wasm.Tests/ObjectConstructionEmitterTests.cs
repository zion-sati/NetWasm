using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Instructions.NativeIntegers;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ObjectConstructionEmitterTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReferenceConstructionAllocatesCallsConstructorAndProducesReference(
        bool containsReferences, bool inlineObjectAllocation)
    {
        var publications = new List<(bool ConstructorCall, int At)>();
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider { ObjectReferenceOffsets = containsReferences ? [4] : [] };
        var emitter = CreateEmitter(layouts, (_, code, constructorCall) =>
            publications.Add((constructorCall, ((RecordingInstructionWriter)code).ToInstructions().Length)));
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            operand: new CilOperand.Entity(ConstructorKey));
        if (inlineObjectAllocation)
            request = WithInlineObjectAllocation(request);

        Emit((IInstructionCommandProvider)emitter, request, CreateFunctionIndexResolver(program));

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Equal(TypeKey, layouts.ObjectRequest);
        // newobj publishes the allocation's roots itself, then the constructor call's.
        Assert.Equal([false, true], publications.Select(static publication => publication.ConstructorCall));
        var instructions = CodeOf(request);
        // allocate(size, type id), or the inline bump's fallback, which can collect.
        var allocate = CallTo(instructions, request,
            inlineObjectAllocation ? RuntimeImportSymbol.AllocateObjectFallback : RuntimeImportSymbol.Allocate);
        Assert.Equal([(WasmOpcodes.I32Constant, 16L), (WasmOpcodes.I32Constant, 9L)], ArgumentsOf(instructions, allocate, 2));
        var run = CallTo(instructions, request, RuntimeImportSymbol.AllocationRun);
        if (inlineObjectAllocation)
        {
            // Where the collector keeps runs (allocation_runs_enabled),
            // allocation_run(size, type id, whether the layout has references) and
            // allocation_slot(size) bump the run inline; only when it is exhausted
            // does the code leave the inner block to publish the roots and fall back.
            Assert.True(CallTo(instructions, request, RuntimeImportSymbol.AllocationRunsEnabled) < run);
            Assert.Equal(
                [(WasmOpcodes.I32Constant, 16L), (WasmOpcodes.I32Constant, 9L), (WasmOpcodes.I32Constant, containsReferences ? 1L : 0L)],
                ArgumentsOf(instructions, run, 3));
            var slot = CallTo(instructions, request, RuntimeImportSymbol.AllocationSlot);
            Assert.Equal([(WasmOpcodes.I32Constant, 16L)], ArgumentsOf(instructions, slot, 1));
            var bound = Array.FindIndex(instructions, instruction => instruction.Opcode == WasmOpcodes.I32LessThanUnsigned);
            Assert.Equal(WasmInstruction.WithOperand(WasmOpcodes.BranchIf, WasmInstructionOperand.Unsigned(0)), instructions[bound + 1]);
            var slowPath = Array.FindIndex(instructions, bound, instruction => instruction.Opcode == WasmOpcodes.End) + 1;
            Assert.Equal(WasmInstruction.WithOperand(WasmOpcodes.Branch, WasmInstructionOperand.Unsigned(1)), instructions[slowPath - 2]);
            Assert.InRange(publications[0].At, slowPath, allocate - 2);
        }
        else
        {
            Assert.Equal(-1, run);
            Assert.Equal(0, publications[0].At);
        }
    }

    // An inline allocation publishes the instruction's roots first, as an ordinary
    // one does, where something besides its fallback could collect or see them.
    [Theory]
    [InlineData("guard")]
    [InlineData("handler")]
    [InlineData("filter-funclet")]
    [InlineData("filter-environment")]
    [InlineData("finalizable")]
    public void InlineAllocationPublishesRootsFirstWhereElseTheyCouldBeNeeded(string reason)
    {
        var publications = new List<(bool ConstructorCall, int At)>();
        var request = WithInlineObjectAllocation(CreateInstructionRequest(
            CilOperation.NewObject,
            operand: new CilOperand.Entity(ConstructorKey)));
        var captured = new CapturedSlot(false, 3);
        request = reason switch
        {
            "handler" => Changed(request with
            {
                Context = request.Context with { ActiveExceptionGroup = new(new(0), null, 0, 0, [], [], [], null, null) },
            }),
            "filter-funclet" => Changed(request with { Context = request.Context with { IsFilterFunclet = true } }),
            "filter-environment" => Changed(request with
            {
                Context = request.Context with
                {
                    FilterEnvironment = new(16, 1, 0, ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(captured,
                        new(captured, CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference), 0, []))),
                },
            }),
            "finalizable" => Changed(request with
            {
                Target = request.Target with
                {
                    RuntimeImportSelection = request.Target.RuntimeImportSelection with { IncludeFinalization = true },
                    FinalizableTypeIds = Lookups.Of(ImmutableHashSet.Create(9)),
                },
            }),
            _ => request,
        };

        Emit(
            (IInstructionCommandProvider)CreateEmitter(
                new RecordingLayoutProvider(),
                (_, code, constructorCall) =>
                    publications.Add((constructorCall, ((RecordingInstructionWriter)code).ToInstructions().Length)),
                initialization: reason == "guard" ? new SilentInitialization() : null,
                guards: reason == "guard" ? new GuardingResolver() : null),
            request,
            CreateFunctionIndexResolver(new FakeProgram()));

        Assert.Equal((false, 0), publications[0]);
        Assert.Equal(-1, CallTo(CodeOf(request), request, RuntimeImportSymbol.AllocationRun));
    }

    [Fact]
    public void FinalizableReferenceUsesTheDedicatedAllocationCapability()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var original = CreateInstructionRequest(
            CilOperation.NewObject,
            operand: new CilOperand.Entity(ConstructorKey));
        var selection = original.Target.RuntimeImportSelection with
        {
            IncludeFinalization = true,
        };
        var request = original with
        {
            Target = original.Target with
            {
                RuntimeImportSelection = selection,
                FinalizableTypeIds = Lookups.Of(ImmutableHashSet.Create(9)),
            },
        };
        var writer = new RecordingInstructionWriter();
        RegisterInstructionWriter(request, writer);
        var imports = WasmRuntimeImports.CreateCatalog();

        Emit(
            (IInstructionCommandProvider)CreateEmitter(layouts, (_, _, _) => { }),
            request,
            CreateFunctionIndexResolver(program));

        Assert.Contains(
            writer.ToInstructions(),
            instruction => instruction.Opcode == WasmOpcodes.Call &&
                instruction.Operand.UnsignedValue == (uint)imports.Resolve(
                    RuntimeImportSymbol.AllocateFinalizable,
                    selection));
        Assert.DoesNotContain(
            writer.ToInstructions(),
            instruction => instruction.Opcode == WasmOpcodes.Call &&
                instruction.Operand.UnsignedValue == (uint)imports.Resolve(
                    RuntimeImportSymbol.Allocate,
                    selection));
    }

    [Fact]
    public void UnconstructedMethodInstanceUsesItsDefinitionFunctionIndex()
    {
        var program = new FakeProgram();
        var constructor = program.GetMethod(ConstructorKey);
        var instance = new MethodInstanceModel(
            constructor,
            CliTypeIdentity.Named(
                Assembly,
                "Test",
                "Reference",
                isValueType: false),
            [],
            constructor.Signature);
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            operand: new CilOperand.MethodInstance(instance));
        var functionIndices = new RecordingFunctionIndexResolver();

        Emit(
            (IInstructionCommandProvider)CreateEmitter(
                new RecordingLayoutProvider(),
                (_, _, _) => { }),
            request,
            functionIndices);

        Assert.False(instance.IsConstructed);
        Assert.Equal(ConstructorKey, functionIndices.Definition);
        Assert.Null(functionIndices.Constructed);
    }

    // An inline allocation of a constructed type bumps the run of its own layout.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructedReferenceUsesItsInstantiatedObjectLayout(bool inlineObjectAllocation)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var constructor = program.GetMethod(ConstructorKey);
        var genericType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Reference`1",
            isValueType: false);
        var declaringType = CliTypeIdentity.GenericInstantiation(
            genericType,
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var instance = new MethodInstanceModel(
            constructor,
            declaringType,
            [],
            constructor.Signature);
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            operand: new CilOperand.MethodInstance(instance));
        if (inlineObjectAllocation)
            request = WithInlineObjectAllocation(request);

        Emit(
            (IInstructionCommandProvider)CreateEmitter(layouts, (_, _, _) => { }),
            request,
            CreateConstructedResolver(program, instance));

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Equal(declaringType, layouts.ObjectIdentityRequest);
        Assert.Equal(inlineObjectAllocation, CallTo(CodeOf(request), request, RuntimeImportSymbol.AllocationRun) >= 0);
    }

    [Fact]
    public void DelegateConstructorRequiresTheRuntimeObjectAndMethodShape()
    {
        var layouts = new RecordingLayoutProvider();
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            operand: new CilOperand.Entity(ConstructorKey));

        var exception = Assert.Throws<CompilerException>(() =>
            Emit(
                (IInstructionCommandProvider)CreateEmitter(
                    layouts,
                    (_, _, _) => { },
                    new DelegateClassifier()),
                request,
                CreateFunctionIndexResolver(new FakeProgram())));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("runtime (object, native int) shape", exception.Message);
    }

    [Fact]
    public void NewObjectRequiresAnEntityOrMethodInstanceOperand()
    {
        var layouts = new RecordingLayoutProvider();
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            operand: new CilOperand.None());

        var exception = Assert.Throws<InvalidOperationException>(() => Emit(
            (IInstructionCommandProvider)CreateEmitter(layouts, (_, _, _) => { }),
            request,
            CreateFunctionIndexResolver(new FakeProgram())));

        Assert.Contains("has no entity operand", exception.Message);
    }

    // StringConstructorRewriter turns every string newobj into a call to String.Ctor, so
    // one that reaches emission is a compiler fault, not an object to allocate.
    [Fact]
    public void StringConstructorThatReachesEmissionIsRejected()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var signature = MethodSignatureModel.Create(
            CliValueKind.Void,
            CliValueKind.I4,
            CliValueKind.I4);
        var constructor = new MethodDefinitionModel(
            ConstructorKey,
            StringTypeKey,
            ".ctor",
            false,
            signature,
            1);
        var instance = new MethodInstanceModel(
            constructor,
            CliTypeIdentity.Named(Assembly, "System", "String", isValueType: false),
            [],
            signature);
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            [CliValueKind.I4, CliValueKind.I4],
            new CilOperand.MethodInstance(instance));

        var exception = Assert.Throws<InvalidOperationException>(() => Emit(
            (IInstructionCommandProvider)CreateEmitter(layouts, (_, _, _) => { }),
            request,
            CreateFunctionIndexResolver(program)));

        Assert.Contains("StringConstructorRewriter", exception.Message);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void ValidDelegateConstructorStoresTargetAndMethod(
        WasmTarget target, bool inlineObjectAllocation)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var signature = MethodSignatureModel.Create(
            CliValueKind.Void,
            CliValueKind.ManagedReference,
            CliValueKind.NativeInt);
        var delegateType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Delegate",
            isValueType: false);
        var constructor = new MethodDefinitionModel(
            ConstructorKey,
            TypeKey,
            ".ctor",
            false,
            signature,
            1);
        var instance = new MethodInstanceModel(
            constructor,
            delegateType,
            [],
            signature);
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            [CliValueKind.ManagedReference, CliValueKind.NativeInt],
            new CilOperand.MethodInstance(instance));
        if (inlineObjectAllocation)
            request = WithInlineObjectAllocation(request);

        Emit(
            (IInstructionCommandProvider)CreateEmitter(
                layouts,
                (_, _, _) => { },
                new DelegateClassifier()),
            request,
            CreateFunctionIndexResolver(program));

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.I32Store, GetCodeBytes(request));
        // Where the runtime inlines its allocation, a delegate bumps its run and
        // falls back to allocate; elsewhere it calls allocate.
        Assert.Equal(inlineObjectAllocation, CallTo(CodeOf(request), request, RuntimeImportSymbol.AllocationRun) >= 0);
        Assert.True(CallTo(CodeOf(request), request,
            inlineObjectAllocation ? RuntimeImportSymbol.AllocateObjectFallback : RuntimeImportSymbol.Allocate) >= 0);
        if (target == WasmTarget.Wasm64)
        {
            Assert.Contains(WasmOpcodes.I32WrapI64, GetCodeBytes(request));
        }
        else
        {
            Assert.DoesNotContain(WasmOpcodes.I32WrapI64, GetCodeBytes(request));
        }
    }

    [Theory]
    [InlineData(CliValueKind.I4)]
    [InlineData(CliValueKind.ManagedReference)]
    public void DelegateConstructorRejectsEachInvalidParameterShape(
        CliValueKind secondParameter)
    {
        var layouts = new RecordingLayoutProvider();
        var signature = MethodSignatureModel.Create(
            CliValueKind.Void,
            CliValueKind.I4,
            secondParameter);
        var constructor = new MethodDefinitionModel(
            ConstructorKey,
            TypeKey,
            ".ctor",
            false,
            signature,
            1);
        var delegateType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Delegate",
            isValueType: false);
        var instance = new MethodInstanceModel(
            constructor,
            delegateType,
            [],
            signature);
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            [CliValueKind.ManagedReference, secondParameter],
            new CilOperand.MethodInstance(instance));

        var exception = Assert.Throws<CompilerException>(() => Emit(
            (IInstructionCommandProvider)CreateEmitter(
                layouts,
                (_, _, _) => { },
                new DelegateClassifier()),
            request,
            CreateFunctionIndexResolver(new FakeProgram())));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
    }

    // A value of up to 64 bytes zeroes with stores; a larger one fills.
    // A value type's construction allocates nothing, so it publishes its roots
    // first whether or not the runtime inlines allocation.
    [Theory]
    [InlineData(4, false, false)]
    [InlineData(80, true, true)]
    public void ValueTypeConstructionUsesAnEmptyValueFrameAtZeroOffset(int size, bool fills, bool inlineObjectAllocation)
    {
        var layouts = new RecordingLayoutProvider(valueSize: size);
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true);
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            operand: new CilOperand.Entity(ConstructorKey),
            context: CreateValueContext(0));
        if (inlineObjectAllocation)
            request = WithInlineObjectAllocation(request);
        var publishedAt = new List<int>();

        Emit(
            (IInstructionCommandProvider)CreateEmitter(
                layouts,
                (_, code, _) => publishedAt.Add(((RecordingInstructionWriter)code).ToInstructions().Length),
                types: new ValueTypeResolver(valueType)),
            request,
            CreateFunctionIndexResolver(new FakeProgram()));

        Assert.Equal(0, publishedAt[0]);
        Assert.Equal([CliValueKind.ValueType], request.Stack);
        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions();
        Assert.Equal(fills, instructions.Any(instruction => instruction.Opcode == WasmOpcodes.Prefixed &&
            instruction.Operand == WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
    }

    [Fact]
    public void ConstructedValueTypeUsesItsOffsetAndConstructorInstance()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true);
        var signature = MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4);
        var constructor = new MethodDefinitionModel(
            ConstructorKey,
            TypeKey,
            ".ctor",
            false,
            signature,
            1);
        var instance = new MethodInstanceModel(
            constructor,
            valueType,
            [valueType],
            signature);
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            [CliValueKind.I4],
            new CilOperand.MethodInstance(instance),
            CreateValueContext(8));

        Emit(
            (IInstructionCommandProvider)CreateEmitter(layouts, (_, _, _) => { }),
            request,
            CreateConstructedResolver(program, instance));

        Assert.Equal([CliValueKind.ValueType], request.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
    }

    [Fact]
    public void NativeIntegerConstructionUsesTheScalarStrategy()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        var signature = MethodSignatureModel.Create(
            CliValueKind.Void,
            CliValueKind.I4);
        var constructor = new MethodDefinitionModel(
            ConstructorKey,
            TypeKey,
            ".ctor",
            false,
            signature,
            1);
        var instance = new MethodInstanceModel(
            constructor,
            CliTypeIdentity.Primitive("nativeint", CliValueKind.NativeInt),
            [],
            signature);
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            [CliValueKind.I4],
            new CilOperand.MethodInstance(instance));

        Emit(
            (IInstructionCommandProvider)CreateEmitter(layouts, (_, _, _) => { }),
            request,
            CreateConstructedResolver(program, instance));

        Assert.Equal([CliValueKind.NativeInt], request.Stack);
        Assert.Contains(WasmOpcodes.I64ExtendI32Signed, GetCodeBytes(request));
        Assert.DoesNotContain(WasmOpcodes.Call, GetCodeBytes(request));
    }

    private static WasmInstruction[] CodeOf(InstructionEmissionRequest request) =>
        [.. Assert.IsType<RecordingInstructionWriter>(GetCodeWriter(request)).ToInstructions()];

    // The index of the call to a runtime import in the module's selection, or -1.
    private static int CallTo(WasmInstruction[] instructions, InstructionEmissionRequest request, RuntimeImportSymbol symbol) =>
        Array.FindIndex(instructions, instruction => instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue ==
                (uint)WasmRuntimeImports.CreateCatalog().Resolve(symbol, request.Target.RuntimeImportSelection));

    private static (byte, long)[] ArgumentsOf(WasmInstruction[] instructions, int call, int count)
    {
        Assert.True(call >= count);
        return [.. instructions[(call - count)..call].Select(static instruction =>
            (instruction.Opcode, (long)instruction.Operand.SignedValue))];
    }

    private static InstructionEmissionRequest Changed(InstructionEmissionRequest changed)
    {
        RegisterInstructionWriter(changed, new RecordingInstructionWriter());
        return changed;
    }

    // A type whose static initializer guard runs before its allocation. The guard's code is the
    // initialization emitter's, which writes nothing here.
    private sealed class GuardingResolver : IStaticInitializerGuardResolver
    {
        public bool TryResolve(
            StaticInitializationEmissionRequest request,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out StaticInitializerGuard? guard)
        {
            guard = new(240, null, "guarded") { FunctionIndex = OptionalFunctionIndex.At(42) };
            return true;
        }
    }

    private sealed class SilentInitialization : IStaticInitializationEmitter
    {
        public void Emit(StaticInitializationEmissionRequest request,
            IWasmInstructionWriter code, IFunctionIndexResolver functionIndices)
        {
        }
    }

    private static ObjectConstructionEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        Action<InstructionEmissionRequest, IWasmInstructionWriter, bool> safepoint,
        ITypeClassifier? typeClassifier = null,
        ICilTypeIdentityResolver? types = null,
        IStaticInitializationEmitter? initialization = null,
        IStaticInitializerGuardResolver? guards = null)
    {
        var program = new FakeProgram();
        var guardResolver = guards ?? new StaticInitializerGuardResolver(program, program);
        IAddressInstructionEmitter addresses = new AddressInstructionEmitter(layouts);
        IImplicitExceptionEmitter exceptions = new ImplicitExceptionEmitter(layouts);
        IRootPublicationEmitter roots = new RecordingRootPublicationEmitter(safepoint);
        var conversions = new NativeIntegerConversionEmitter(layouts);
        return new ObjectConstructionEmitter(program, program, typeClassifier ?? program, layouts, addresses, layouts, layouts, layouts, types ?? CreateTypeIdentities(program),
            WasmRuntimeImports.CreateCatalog(),
            exceptions,
            roots,
            new NativeIntegerConstructionEmitter(layouts, conversions),
            initialization ?? new StaticInitializationEmitter(guardResolver, addresses),
            guardResolver);
    }

    private static void Emit<TProvider>(
        TProvider provider,
        InstructionEmissionRequest request,
        IFunctionIndexResolver functionIndices)
        where TProvider : IInstructionCommandProvider
    {
        var command = provider.Commands.Single(candidate =>
            candidate.Operation == request.Instruction.Operation);
        command.Emit(request, GetCodeWriter(request), functionIndices);
    }

    private static MethodEmissionContext CreateValueContext(int offset) =>
        CreateMethodEmissionContext() with
        {
            ValueLayout = new ValueFrameLayout(
                offset,
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, offset),
                [])
        };

    private static IFunctionIndexResolver CreateConstructedResolver(
        FakeProgram program,
        MethodInstanceModel instance)
    {
        var indices = CreateInstructionModuleTarget(program).FunctionIndices with
        {
            ConstructedMethods = ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add(
                instance.CanonicalName,
                new WasmFunctionIndex(77))
        };
        return CreateFunctionIndexResolver(program, indices);
    }

    private sealed class DelegateClassifier : ITypeClassifier
    {
        public bool IsDelegateType(EntityKey type) => true;
    }

    private sealed class ValueTypeResolver(CliTypeIdentity type) : ICilTypeIdentityResolver
    {
        public CliTypeIdentity Resolve(EntityKey key) => type;
    }

    private sealed class RecordingFunctionIndexResolver : IFunctionIndexResolver
    {
        public OptionalFunctionIndex ManagedThrowFunction => OptionalFunctionIndex.At(7);

        public int Resolve(InteropImportKey import) => Resolve(import.Method);

        public EntityKey? Definition { get; private set; }

        public string? Constructed { get; private set; }

        public int Resolve(EntityKey method)
        {
            Definition = method;
            return 1;
        }

        public int Resolve(string method)
        {
            Constructed = method;
            return 1;
        }

        public int Resolve(MethodInstanceModel method) => method.IsConstructed
            ? Resolve(method.CanonicalName)
            : Resolve(method.Definition.Key);
    }
}
