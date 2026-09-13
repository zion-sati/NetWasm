using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class VirtualDispatchCallEmitterTests
{
    [Fact]
    public void RejectsEmissionWithoutAPlannedDispatchSite()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var methodDefinition = program.GetMethod(EntryKey);
        var declaringType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Type",
            isValueType: false);
        var method = new MethodInstanceModel(
            methodDefinition,
            declaringType,
            [],
            methodDefinition.Signature);
        var emitter = CreateEmitter(layouts);

        var instruction = CreateInstructionRequest(CilOperation.CallVirtual);
        var exception = Assert.Throws<InvalidOperationException>(() => Emit(
            (ICallEmitter)emitter,
            new CallEmissionRequest(instruction, method, 0, 1),
            CreateFunctionIndexResolver(program)));

        Assert.Contains("caller method identity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitsTypeCheckedTargetAndManagedFailurePath()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var type = CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false);
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4);
        var definition = new MethodDefinitionModel(
            EntryKey,
            TypeKey,
            "Read",
            false,
            signature,
            1)
        {
            IsVirtual = true,
        };
        var method = new MethodInstanceModel(definition, type, [], signature);
        var callerDefinition = program.GetMethod(EntryKey);
        var caller = new MethodInstanceModel(
            callerDefinition,
            type,
            [],
            callerDefinition.Signature);
        var site = new DispatchCallSiteModel(
            caller.CanonicalName,
            0,
            method,
            [new DispatchTargetModel(type, method)]);
        var body = new CilMethodBody(callerDefinition, 3, [], [])
        {
            MethodInstance = caller,
        };
        var code = new RecordingInstructionWriter();
        var instruction = new InstructionEmissionRequest(
            Header(body),
            I(0, CilOperation.CallVirtual, new CilOperand.MethodInstance(method)),
            [CliValueKind.ManagedReference, CliValueKind.I4],
            CreateMethodEmissionContext(),
            CreateInstructionModuleTarget(program) with
            {
                DispatchCallSites =
                    ImmutableDictionary<string, DispatchCallSiteModel>.Empty.Add(site.Key, site),
            });
        RegisterInstructionWriter(instruction, code);
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(
                EntryKey,
                new WasmFunctionIndex(30)),
            [],
            [],
            []);
        var emitter = CreateEmitter(layouts);

        var request = new CallEmissionRequest(instruction, method, 0, 2);
        Assert.Equal(
            CallEmissionKind.VirtualDispatch,
            new CallEmissionKindResolver(program, new FakeIntrinsics()).Resolve(request, code));
        Emit((ICallEmitter)emitter, request, CreateFunctionIndexResolver(program, indices));

        Assert.Equal([CliValueKind.I4], instruction.Stack);
        Assert.Contains(WasmOpcodes.If, code.ToArray());
        Assert.Contains(WasmOpcodes.Throw, code.ToArray());
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Load)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Load)]
    public void ConstrainedReferenceDispatchLoadsReceiverForEveryMemoryWidth(
        WasmTarget target,
        byte expectedLoad)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var type = ReferenceType();
        var method = Method(
            Key(0x06000015),
            type,
            MethodSignatureModel.Create(CliValueKind.Void));
        var request = CreateDispatchRequest(
            method,
            [new DispatchTargetModel(type, method)],
            [CliValueKind.ManagedAddress],
            consumed: 1,
            out var functionIndices) with
        {
            ConstrainedType = type,
        };

        Emit(
            (ICallEmitter)CreateEmitter(layouts),
            request,
            functionIndices);

        Assert.Empty(request.Instruction.Stack);
        Assert.Contains(expectedLoad, GetCodeBytes(request.Instruction));
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request.Instruction));
    }

    [Fact]
    public void RejectsNonVirtualOperationEvenWhenAPlanExists()
    {
        var type = ReferenceType();
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4);
        var method = Method(Key(0x06000010), type, signature);
        var request = CreateDispatchRequest(
            method,
            [new DispatchTargetModel(type, method)],
            [CliValueKind.ManagedReference, CliValueKind.I4],
            consumed: 2,
            out var indices,
            operation: CilOperation.Call);

        var exception = Assert.Throws<InvalidOperationException>(() => Emit(
            (ICallEmitter)CreateEmitter(new RecordingLayoutProvider()),
            request,
            indices));

        Assert.Contains("callvirt instruction", exception.Message);
    }

    [Fact]
    public void RejectsVirtualOperationWithoutMatchingCallerSite()
    {
        var type = ReferenceType();
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4);
        var method = Method(Key(0x06000011), type, signature);
        var request = CreateDispatchRequest(
            method,
            [],
            [CliValueKind.ManagedReference, CliValueKind.I4],
            consumed: 2,
            out var indices,
            includeSite: false);

        var exception = Assert.Throws<InvalidOperationException>(() => Emit(
            (ICallEmitter)CreateEmitter(new RecordingLayoutProvider()),
            request,
            indices));

        Assert.Contains("planned call site", exception.Message);
    }

    [Fact]
    public void EmitsAllEnumIntrinsicDispatchTargets()
    {
        var type = ReferenceType();
        var signature = MethodSignatureModel.Create(
            CliValueKind.I4,
            CliValueKind.ManagedReference,
            CliValueKind.ManagedReference);
        var equals = Method(Key(0x06000012), type, signature);
        var hashCode = Method(Key(0x06000013), type, signature);
        var compareTo = Method(Key(0x06000014), type, signature);
        var typeCode = Method(Key(0x0600001a), type, signature);
        var hasFlag = Method(Key(0x0600001b), type, signature);
        var normal = Method(Key(0x06000015), type, signature);
        var intrinsics = new IntrinsicRegistry(
            new Dictionary<EntityKey, RuntimeIntrinsic>
            {
                [equals.Definition.Key] = RuntimeIntrinsic.EnumEquals,
                [hashCode.Definition.Key] = RuntimeIntrinsic.EnumGetHashCode,
                [compareTo.Definition.Key] = RuntimeIntrinsic.EnumCompareTo,
                [typeCode.Definition.Key] = RuntimeIntrinsic.EnumGetTypeCode,
                [hasFlag.Definition.Key] = RuntimeIntrinsic.EnumHasFlag,
            });
        var enumEquals = new RecordingEnumEqualsEmitter();
        var enumHashCode = new RecordingEnumHashCodeEmitter();
        var enumCompareTo = new RecordingEnumCompareToEmitter();
        var enumTypeCode = new RecordingEnumTypeCodeEmitter();
        var enumHasFlag = new RecordingEnumHasFlagEmitter();
        var request = CreateDispatchRequest(
            equals,
            [
                new DispatchTargetModel(type, equals),
                new DispatchTargetModel(type, hashCode),
                new DispatchTargetModel(type, compareTo),
                new DispatchTargetModel(type, typeCode),
                new DispatchTargetModel(type, hasFlag),
                new DispatchTargetModel(type, normal),
            ],
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            consumed: 2,
            out var indices);

        Emit(
            (ICallEmitter)CreateEmitter(
                new RecordingLayoutProvider(),
                intrinsics,
                enumEquals,
                enumHashCode,
                enumCompareTo,
                enumTypeCode,
                enumHasFlag: enumHasFlag),
            request,
            indices);

        Assert.Equal([CliValueKind.I4], request.Instruction.Stack);
        Assert.Equal(1, enumEquals.Calls);
        Assert.Equal(1, enumHashCode.Calls);
        Assert.Equal(1, enumCompareTo.Calls);
        Assert.Equal(1, enumTypeCode.Calls);
        Assert.Equal(1, enumHasFlag.Calls);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request.Instruction));
    }

    [Fact]
    public void EmitsValueTypeObjectIntrinsicsThroughSpecializedEmitters()
    {
        var type = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true);
        var signature = MethodSignatureModel.Create(
            CliValueKind.I4,
            CliValueKind.ManagedReference,
            CliValueKind.ManagedReference);
        var equals = Method(Key(0x06000022), type, signature);
        var hashCode = Method(Key(0x06000023), type, signature);
        var intrinsics = new IntrinsicRegistry(
            new Dictionary<EntityKey, RuntimeIntrinsic>
            {
                [equals.Definition.Key] = RuntimeIntrinsic.ValueTypeEquals,
                [hashCode.Definition.Key] = RuntimeIntrinsic.ValueTypeGetHashCode,
            });
        var valueTypeEquals = new RecordingValueTypeEqualsEmitter();
        var valueTypeHashCode = new RecordingValueTypeHashCodeEmitter();
        var request = CreateDispatchRequest(
            equals,
            [
                new DispatchTargetModel(type, equals),
                new DispatchTargetModel(type, hashCode),
            ],
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            consumed: 2,
            out var indices);

        Emit(
            (ICallEmitter)CreateEmitter(
                new RecordingLayoutProvider(),
                intrinsics,
                valueTypeEquals: valueTypeEquals,
                valueTypeHashCode: valueTypeHashCode),
            request,
            indices);

        Assert.Equal(1, valueTypeEquals.Calls);
        Assert.Equal(1, valueTypeHashCode.Calls);
        Assert.Equal([CliValueKind.I4], request.Instruction.Stack);
    }

    [Fact]
    public void EmitsEnumToStringIntrinsicWithoutResolvingAFunctionIndex()
    {
        var type = ReferenceType();
        var signature = MethodSignatureModel.Create(CliValueKind.ManagedReference);
        var definition = new MethodDefinitionModel(
            Key(0x06000018),
            TypeKey,
            "ToString",
            false,
            signature,
            1);
        var method = new MethodInstanceModel(definition, type, [], signature);
        var intrinsics = new IntrinsicRegistry(
            new Dictionary<EntityKey, RuntimeIntrinsic>
            {
                [definition.Key] = RuntimeIntrinsic.EnumToString,
            });
        var toString = new RecordingEnumToStringEmitter();
        var request = CreateDispatchRequest(
            method,
            [new DispatchTargetModel(type, method)],
            [CliValueKind.ManagedReference],
            consumed: 1,
            out var indices);

        Emit(
            (ICallEmitter)CreateEmitter(
                new RecordingLayoutProvider(),
                intrinsics,
                enumToString: toString),
            request,
            indices);

        Assert.Equal(1, toString.Calls);
        Assert.Equal([CliValueKind.ManagedReference], request.Instruction.Stack);
    }

    [Fact]
    public void EmitsEnumConversionIntrinsicFromTheBoxedDispatchReceiver()
    {
        var type = ReferenceType();
        var signature = MethodSignatureModel.Create(
            CliValueKind.I4,
            CliValueKind.ManagedReference);
        var definition = new MethodDefinitionModel(
            Key(0x06000019),
            TypeKey,
            "System.IConvertible.ToChar",
            false,
            signature,
            1);
        var method = new MethodInstanceModel(definition, type, [], signature);
        var intrinsics = new IntrinsicRegistry(
            new Dictionary<EntityKey, RuntimeIntrinsic>
            {
                [definition.Key] = RuntimeIntrinsic.EnumConvert,
            });
        var convert = new RecordingEnumConvertEmitter();
        var request = CreateDispatchRequest(
            method,
            [new DispatchTargetModel(type, method)],
            [CliValueKind.ManagedReference],
            consumed: 1,
            out var indices);

        Emit(
            (ICallEmitter)CreateEmitter(
                new RecordingLayoutProvider(),
                intrinsics,
                enumConvert: convert),
            request,
            indices);

        Assert.Equal(1, convert.Calls);
        Assert.Equal([CliValueKind.I4], request.Instruction.Stack);
    }

    [Fact]
    public void PublishesEnumTypeCodeValueReturn()
    {
        var type = ReferenceType();
        var returnType = CliTypeIdentity.Named(
            Assembly,
            "System",
            "TypeCode",
            isValueType: true);
        var signature = MethodSignatureModel.Create(returnType);
        var method = Method(Key(0x06000021), type, signature);
        var intrinsics = new IntrinsicRegistry(
            new Dictionary<EntityKey, RuntimeIntrinsic>
            {
                [method.Definition.Key] = RuntimeIntrinsic.EnumGetTypeCode,
            });
        var valueReturn = new RecordingEnumValueReturnEmitter();
        var request = CreateDispatchRequest(
            method,
            [new DispatchTargetModel(type, method)],
            [CliValueKind.ManagedReference],
            consumed: 1,
            out var indices,
            context: CreateValueContext(0));

        Emit(
            (ICallEmitter)CreateEmitter(
                new RecordingLayoutProvider(),
                intrinsics,
                enumValueReturn: valueReturn),
            request,
            indices);

        Assert.Equal(1, valueReturn.Calls);
        Assert.Equal([CliValueKind.ValueType], request.Instruction.Stack);
    }

    [Fact]
    public void EmitsVoidDispatchWithoutProducingAStackResult()
    {
        var type = ReferenceType();
        var signature = MethodSignatureModel.Create(
            CliValueKind.Void,
            CliValueKind.ManagedReference);
        var method = Method(Key(0x06000016), type, signature);
        var request = CreateDispatchRequest(
            method,
            [new DispatchTargetModel(type, method)],
            [CliValueKind.ManagedReference],
            consumed: 1,
            out var indices);

        Emit(
            (ICallEmitter)CreateEmitter(new RecordingLayoutProvider()),
            request,
            indices);

        Assert.Empty(request.Instruction.Stack);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 0)]
    [InlineData(WasmTarget.Wasm64, 8)]
    public void EmitsValueReturnAndAddressOperationsForEachMemoryWidth(
        WasmTarget target,
        int returnOffset)
    {
        var receiver = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Enum",
            isValueType: true);
        var signature = MethodSignatureModel.Create(receiver);
        var method = Method(Key(0x06000017), receiver, signature);
        var request = CreateDispatchRequest(
            method,
            [new DispatchTargetModel(receiver, method)],
            [CliValueKind.ManagedReference],
            consumed: 1,
            out var indices,
            context: CreateValueContext(returnOffset));

        Emit(
            (ICallEmitter)CreateEmitter(
                new RecordingLayoutProvider(WasmTargetLayout.For(target))),
            request,
            indices);

        Assert.Equal([CliValueKind.ValueType], request.Instruction.Stack);
        var code = GetCodeBytes(request.Instruction);
        if (target == WasmTarget.Wasm64)
        {
            Assert.Contains(WasmOpcodes.I64EqualZero, code);
            Assert.Contains(WasmOpcodes.I64Constant, code);
            Assert.Contains(WasmOpcodes.I64Add, code);
        }
        else
        {
            Assert.Contains(WasmOpcodes.I32EqualZero, code);
            Assert.Contains(WasmOpcodes.I32Constant, code);
            Assert.Contains(WasmOpcodes.I32Add, code);
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    public void ShapesValueTypeDispatchReceiverForTheSelectedImplementation(
        WasmTarget target,
        bool implementationIsValueType)
    {
        var receiver = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "ConcreteValue",
            isValueType: true);
        var declaringType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            implementationIsValueType ? "ConcreteValue" : "ReferenceBase",
            implementationIsValueType);
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        var method = Method(Key(0x06000020), declaringType, signature);
        var request = CreateDispatchRequest(
            method,
            [new DispatchTargetModel(receiver, method)],
            [CliValueKind.ManagedReference],
            consumed: 1,
            out var indices);

        Emit(
            (ICallEmitter)CreateEmitter(
                new RecordingLayoutProvider(WasmTargetLayout.For(target))),
            request,
            indices);

        var code = GetCodeBytes(request.Instruction);
        var addressAdd = target == WasmTarget.Wasm64
            ? WasmOpcodes.I64Add
            : WasmOpcodes.I32Add;
        Assert.Equal(implementationIsValueType, code.Contains(addressAdd));
        if (implementationIsValueType)
        {
            var instructions = ((RecordingInstructionWriter)GetCodeWriter(
                request.Instruction)).ToInstructions();
            Assert.Contains(instructions.Zip(instructions.Skip(1)), pair =>
                pair.First.Opcode == (target == WasmTarget.Wasm64
                    ? WasmOpcodes.I64Constant
                    : WasmOpcodes.I32Constant) &&
                (target == WasmTarget.Wasm64
                    ? pair.First.Operand.Signed64Value
                    : pair.First.Operand.SignedValue) ==
                WasmTargetLayout.For(target).ObjectHeaderSize &&
                pair.Second.Opcode == addressAdd);
        }
    }

    private static void Emit<TEmitter>(
        TEmitter emitter,
        CallEmissionRequest request,
        IFunctionIndexResolver functionIndices)
        where TEmitter : ICallEmitter => emitter.Emit(
            request,
            GetCodeWriter(request.Instruction),
            functionIndices);

    private static VirtualDispatchCallEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        IRuntimeIntrinsicRegistry? intrinsics = null,
        IEnumEqualsEmitter? enumEquals = null,
        IEnumHashCodeEmitter? enumHashCode = null,
        IEnumCompareToEmitter? enumCompareTo = null,
        IEnumTypeCodeEmitter? enumTypeCode = null,
        IEnumValueReturnEmitter? enumValueReturn = null,
        IEnumHasFlagEmitter? enumHasFlag = null,
        IEnumToStringEmitter? enumToString = null,
        IEnumConvertEmitter? enumConvert = null,
        IValueTypeEqualsEmitter? valueTypeEquals = null,
        IValueTypeHashCodeEmitter? valueTypeHashCode = null) => new(
        layouts,
        layouts,
        layouts,
        intrinsics ?? new FakeIntrinsics(),
        new ImplicitExceptionEmitter(layouts, layouts, 7),
        enumEquals ?? new RecordingEnumEqualsEmitter(),
        enumHashCode ?? new RecordingEnumHashCodeEmitter(),
        enumCompareTo ?? new RecordingEnumCompareToEmitter(),
        enumTypeCode ?? new RecordingEnumTypeCodeEmitter(),
        enumValueReturn ?? new RecordingEnumValueReturnEmitter(),
        enumHasFlag ?? new RecordingEnumHasFlagEmitter(),
        enumToString ?? new RecordingEnumToStringEmitter(),
        enumConvert ?? new RecordingEnumConvertEmitter(),
        valueTypeEquals ?? new RecordingValueTypeEqualsEmitter(),
        valueTypeHashCode ?? new RecordingValueTypeHashCodeEmitter(),
        new ConstrainedReferenceReceiverEmitter(layouts));

    private static CallEmissionRequest CreateDispatchRequest(
        MethodInstanceModel method,
        ImmutableArray<DispatchTargetModel> targets,
        IEnumerable<CliValueKind> stack,
        int consumed,
        out IFunctionIndexResolver functionIndices,
        CilOperation operation = CilOperation.CallVirtual,
        bool includeCaller = true,
        bool includeSite = true,
        MethodEmissionContext? context = null)
    {
        var program = new FakeProgram();
        var callerDefinition = program.GetMethod(EntryKey);
        var callerType = ReferenceType();
        var caller = new MethodInstanceModel(
            callerDefinition,
            callerType,
            [],
            callerDefinition.Signature);
        var site = new DispatchCallSiteModel(
            caller.CanonicalName,
            0,
            method,
            targets);
        var target = CreateInstructionModuleTarget(program) with
        {
            DispatchCallSites = includeSite
                ? ImmutableDictionary<string, DispatchCallSiteModel>.Empty.Add(
                    site.Key,
                    site)
                : ImmutableDictionary<string, DispatchCallSiteModel>.Empty,
        };
        var values = stack.ToArray();
        var instruction = new InstructionEmissionRequest(
            Header(new CilMethodBody(callerDefinition, Math.Max(values.Length, 3), [], [])
            {
                MethodInstance = includeCaller ? caller : null,
            }),
            I(0, operation, new CilOperand.MethodInstance(method)),
            [.. values],
            context ?? CreateMethodEmissionContext(Math.Max(values.Length, 3)),
            target);
        RegisterInstructionWriter(instruction, new RecordingInstructionWriter());
        var directMethods = targets
            .Select((dispatchTarget, index) =>
                (dispatchTarget.Method.Definition.Key, new WasmFunctionIndex(80 + index)))
            .ToImmutableDictionary(pair => pair.Key, pair => pair.Item2);
        var indices = new FunctionIndexMap(directMethods, [], [], []);
        functionIndices = CreateFunctionIndexResolver(program, indices);
        return new CallEmissionRequest(instruction, method, 0, consumed);
    }

    private static CliTypeIdentity ReferenceType() =>
        CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false);

    private static MethodInstanceModel Method(
        EntityKey key,
        CliTypeIdentity declaringType,
        MethodSignatureModel signature) => new(
        new MethodDefinitionModel(key, TypeKey, "Virtual", false, signature, 1),
        declaringType,
        [],
        signature);

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

    private sealed class IntrinsicRegistry(
        IReadOnlyDictionary<EntityKey, RuntimeIntrinsic> intrinsics) :
        IRuntimeIntrinsicRegistry
    {
        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic) =>
            intrinsics.TryGetValue(method, out intrinsic);
    }

    private sealed class RecordingEnumEqualsEmitter : IEnumEqualsEmitter
    {
        public int Calls { get; private set; }

        public void EmitEquals(
            IWasmInstructionWriter code,
            CliValueKind leftKind,
            CliTypeIdentity? constrainedType,
            int left,
            int right,
            int result,
            int temporaryReference,
            int temporaryI4)
        {
            Calls++;
        }
    }

    private sealed class RecordingEnumHashCodeEmitter : IEnumHashCodeEmitter
    {
        public int Calls { get; private set; }

        public void EmitHashCode(
            IWasmInstructionWriter code,
            CliValueKind receiverKind,
            CliTypeIdentity? receiverType,
            int receiver,
            int result,
            int temporaryI4,
            int temporaryI8)
        {
            Calls++;
        }
    }

    private sealed class RecordingEnumCompareToEmitter : IEnumCompareToEmitter
    {
        public int Calls { get; private set; }

        public void EmitCompareTo(
            IWasmInstructionWriter code,
            CliValueKind leftKind,
            CliTypeIdentity? constrainedType,
            int left,
            int right,
            int result,
            int temporaryReference,
            int temporaryI4)
        {
            Calls++;
        }
    }

    private sealed class RecordingEnumTypeCodeEmitter : IEnumTypeCodeEmitter
    {
        public int Calls { get; private set; }

        public void EmitTypeCode(
            IWasmInstructionWriter code,
            int receiver,
            int result,
            int temporaryI4,
            CliTypeIdentity? constrainedType = null,
            CliValueKind receiverKind = CliValueKind.ManagedReference)
        {
            Calls++;
        }
    }

    private sealed class RecordingEnumValueReturnEmitter : IEnumValueReturnEmitter
    {
        public int Calls { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            RuntimeIntrinsicEmissionRequest request,
            int valueLocal) => Calls++;
    }

    private sealed class RecordingEnumHasFlagEmitter : IEnumHasFlagEmitter
    {
        public int Calls { get; private set; }

        public void EmitHasFlag(
            IWasmInstructionWriter code,
            int receiver,
            int flag,
            int result,
            int temporaryI4,
            CliValueKind receiverKind = CliValueKind.ManagedReference,
            CliTypeIdentity? constrainedType = null)
        {
            Calls++;
        }
    }

    private sealed class RecordingEnumToStringEmitter : IEnumToStringEmitter
    {
        public int Calls { get; private set; }

        public void EmitToString(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code) =>
            Calls++;
    }

    private sealed class RecordingEnumConvertEmitter : IEnumConvertEmitter
    {
        public int Calls { get; private set; }

        public void EmitConvert(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code) => Calls++;
    }

    private sealed class RecordingValueTypeEqualsEmitter : IValueTypeEqualsEmitter
    {
        public int Calls { get; private set; }

        public void Emit(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code,
            CliTypeIdentity type,
            CliValueKind receiverKind)
        {
            Calls++;
        }
    }

    private sealed class RecordingValueTypeHashCodeEmitter : IValueTypeHashCodeEmitter
    {
        public int Calls { get; private set; }

        public void Emit(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code,
            CliTypeIdentity type,
            CliValueKind receiverKind)
        {
            Calls++;
        }
    }
}
