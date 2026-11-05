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
    [Fact]
    public void ReferenceConstructionAllocatesCallsConstructorAndProducesReference()
    {
        var safepoints = new List<bool>();
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var emitter = CreateEmitter(layouts, (_, _, constructorCall) =>
            safepoints.Add(constructorCall));
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            operand: new CilOperand.Entity(ConstructorKey));

        Emit((IInstructionCommandProvider)emitter, request, CreateFunctionIndexResolver(program));

        Assert.Equal([false, false, true], safepoints);
        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Equal(TypeKey, layouts.ObjectRequest);
        var calls = Assert.IsType<RecordingInstructionWriter>(GetCodeWriter(request))
            .ToInstructions()
            .Where(static instruction => instruction.Opcode == WasmOpcodes.Call)
            .ToArray();
        Assert.Contains(
            calls,
            instruction => instruction.Operand.UnsignedValue ==
                (uint)WasmRuntimeImports.CreateCatalog().Resolve(
                    RuntimeImportSymbol.Allocate,
                    request.Target.RuntimeImportSelection));
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
                Finalization = new FinalizationPlan(ImmutableHashSet.Create(9), false),
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

    [Fact]
    public void ConstructedReferenceUsesItsInstantiatedObjectLayout()
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

        Emit(
            (IInstructionCommandProvider)CreateEmitter(layouts, (_, _, _) => { }),
            request,
            CreateConstructedResolver(program, instance));

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
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
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ValidDelegateConstructorStoresTargetAndMethod(
        WasmTarget target)
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

        Emit(
            (IInstructionCommandProvider)CreateEmitter(
                layouts,
                (_, _, _) => { },
                new DelegateClassifier()),
            request,
            CreateFunctionIndexResolver(program));

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.I32Store, GetCodeBytes(request));
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
    [Theory]
    [InlineData(4, false)]
    [InlineData(80, true)]
    public void ValueTypeConstructionUsesAnEmptyValueFrameAtZeroOffset(int size, bool fills)
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

        Emit(
            (IInstructionCommandProvider)CreateEmitter(
                layouts,
                (_, _, _) => { },
                types: new ValueTypeResolver(valueType)),
            request,
            CreateFunctionIndexResolver(new FakeProgram()));

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

    private static ObjectConstructionEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        Action<InstructionEmissionRequest, IWasmInstructionWriter, bool> safepoint,
        ITypeClassifier? typeClassifier = null,
        ICilTypeIdentityResolver? types = null)
    {
        var program = new FakeProgram();
        IAddressInstructionEmitter addresses = new AddressInstructionEmitter(layouts);
        IImplicitExceptionEmitter exceptions = new ImplicitExceptionEmitter(layouts);
        IRootPublicationEmitter roots = new RecordingRootPublicationEmitter(safepoint);
        var conversions = new NativeIntegerConversionEmitter(layouts);
        return new ObjectConstructionEmitter(program, program, typeClassifier ?? program, layouts, addresses, layouts, layouts, layouts, types ?? CreateTypeIdentities(program),
            WasmRuntimeImports.CreateCatalog(),
            exceptions,
            roots,
            new NativeIntegerConstructionEmitter(layouts, conversions),
            new StaticInitializationEmitter(program, program, addresses));
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
