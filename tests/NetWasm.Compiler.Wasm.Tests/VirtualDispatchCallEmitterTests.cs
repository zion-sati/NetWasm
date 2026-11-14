using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
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
    public void InitializerGuardUsesTheSelectedImplementationNotTheDeclaration()
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4);
        var declaration = Method(Key(0x06000040), ReferenceType(), signature);
        var implementation = Method(Key(0x06000041),
            CliTypeIdentity.Named(Assembly, "Test", "ValueImplementation", true), signature);
        var request = CreateDispatchRequest(declaration,
            [new(implementation.DeclaringType, implementation)], [CliValueKind.ManagedReference], 1, out var indices);
        var guards = new SelectedInitialization(implementation.Definition);
        var emitter = CreateEmitter(new RecordingLayoutProvider(), initialization: guards, initializationTriggers: guards);

        Emit((ICallEmitter)emitter, request, indices);

        Assert.Equal(implementation.Definition, Assert.Single(guards.Classified));
        var emitted = Assert.Single(guards.Requests);
        Assert.Equal(implementation.DeclaringType, emitted.DeclaringType);
        Assert.True(emitted.IsMethodInvocation);
        Assert.Equal([CliValueKind.I4], request.Instruction.Stack);
    }

    private sealed class SelectedInitialization(MethodDefinitionModel selected) :
        IMethodInitializationTriggerClassifier, IStaticInitializationEmitter
    {
        public List<MethodDefinitionModel> Classified { get; } = [];
        public List<StaticInitializationEmissionRequest> Requests { get; } = [];
        public bool Classify(MethodDefinitionModel method)
        {
            Classified.Add(method);
            return method == selected;
        }

        public void Emit(StaticInitializationEmissionRequest request, IWasmInstructionWriter code,
            IFunctionIndexResolver functionIndices) => Requests.Add(request);
    }

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
                    Lookups.Of(ImmutableDictionary<string, DispatchCallSiteModel>.Empty.Add(site.Key, site)),
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
        Assert.True(ContainsManagedThrow(code.ToArray()));
    }

    [Fact]
    public void EmitsOverrideCheckAndConcreteObjectFallbackOnce()
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4);
        var declaration = Method(
            Key(0x06000040),
            ObjectType(),
            signature);
        var overriddenMethod = Method(
            Key(0x06000041),
            CliTypeIdentity.Named(Assembly, "Test", "Override", isValueType: false),
            signature);
        var request = CreateDispatchRequest(
            declaration,
            [
                new DispatchTargetModel(
                    CliTypeIdentity.Named(Assembly, "Test", "First", isValueType: false),
                    declaration),
                new DispatchTargetModel(overriddenMethod.DeclaringType, overriddenMethod),
                new DispatchTargetModel(
                    CliTypeIdentity.Named(Assembly, "Test", "Second", isValueType: false),
                    declaration),
            ],
            [CliValueKind.ManagedReference],
            consumed: 1,
            out var indices);

        Emit((ICallEmitter)CreateEmitter(new RecordingLayoutProvider()), request, indices);

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request.Instruction))
            .ToInstructions();
        Assert.Single(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I32Equal);
        Assert.Equal(2, instructions.Count(instruction =>
            instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue >= 80));
    }

    [Fact]
    public void EmitsConcreteObjectFallbackWithoutTypeChecks()
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4);
        var declaration = Method(
            Key(0x06000048),
            ObjectType(),
            signature);
        var request = CreateDispatchRequest(
            declaration,
            [
                new DispatchTargetModel(
                    CliTypeIdentity.Named(
                        Assembly,
                        "Test",
                        "FirstInherited",
                        isValueType: false),
                    declaration),
                new DispatchTargetModel(
                    CliTypeIdentity.Named(
                        Assembly,
                        "Test",
                        "SecondInherited",
                        isValueType: false),
                    declaration),
            ],
            [CliValueKind.ManagedReference],
            consumed: 1,
            out var indices);

        Emit((ICallEmitter)CreateEmitter(new RecordingLayoutProvider()), request, indices);

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request.Instruction))
            .ToInstructions();
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Opcode is WasmOpcodes.I32Load or WasmOpcodes.I32Equal);
        Assert.Single(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue >= 80);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void LoadsReceiverTypeOnceAndBalancesLargerTargetSets(
        int targetCount,
        bool expectBalanced)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4);
        var declaration = Method(
            Key(0x06000050),
            ReferenceType(),
            signature);
        var targets = Enumerable.Range(0, targetCount)
            .Select(index =>
            {
                var receiver = CliTypeIdentity.Named(
                    Assembly,
                    "Test",
                    $"Receiver{index}",
                    isValueType: false);
                return new DispatchTargetModel(
                    receiver,
                    Method(Key(0x06000051 + index), receiver, signature));
            })
            .ToImmutableArray();
        var request = CreateDispatchRequest(
            declaration,
            targets,
            [CliValueKind.ManagedReference],
            consumed: 1,
            out var indices);

        Emit((ICallEmitter)CreateEmitter(new RecordingLayoutProvider()), request, indices);

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request.Instruction))
            .ToInstructions();
        Assert.Single(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I32Load);
        Assert.Equal(
            expectBalanced,
            instructions.Any(instruction =>
                instruction.Opcode == WasmOpcodes.I32LessThanUnsigned));
        Assert.Equal(
            expectBalanced,
            instructions.Any(instruction =>
                instruction.Opcode == WasmOpcodes.Branch));
    }

    // A virtual call on object with SharedDispatchPolicy.MinimumTargets targets or more calls the shared body for
    // it instead of dispatching itself; the shared body's own call, a call with fewer targets, a call constrained to
    // a value type and a non-virtual call dispatch. The kind resolver chooses the emitter.
    [Theory]
    [InlineData(SharedDispatchPolicy.MinimumTargets, SharedBody.Separate, CilOperation.CallVirtual, false, true)]
    [InlineData(SharedDispatchPolicy.MinimumTargets - 1, SharedBody.Separate, CilOperation.CallVirtual, false, false)]
    [InlineData(SharedDispatchPolicy.MinimumTargets, SharedBody.Caller, CilOperation.CallVirtual, false, false)]
    [InlineData(SharedDispatchPolicy.MinimumTargets, SharedBody.None, CilOperation.CallVirtual, false, false)]
    [InlineData(SharedDispatchPolicy.MinimumTargets, SharedBody.Separate, CilOperation.Call, false, false)]
    [InlineData(SharedDispatchPolicy.MinimumTargets, SharedBody.Separate, CilOperation.CallVirtual, true, false)]
    public void ManyTargetCallsOnObjectCallTheSharedBody(
        int targetCount, SharedBody shared, CilOperation operation, bool constrainedToValue, bool redirected)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4);
        var declaration = Method(Key(0x06000050), ReferenceType(), signature);
        var targets = Enumerable.Range(0, targetCount)
            .Select(index =>
            {
                var receiver = CliTypeIdentity.Named(Assembly, "Test", $"Receiver{index}", isValueType: false);
                return new DispatchTargetModel(receiver, Method(Key(0x06000100 + index), receiver, signature));
            })
            .ToImmutableArray();
        var request = CreateDispatchRequest(
            declaration, targets, [CliValueKind.ManagedReference], consumed: 1, out var indices, operation: operation,
            shared: shared);
        if (constrainedToValue)
            request = request with { ConstrainedType = CliTypeIdentity.Named(Assembly, "Test", "Value", isValueType: true) };
        var layouts = new RecordingLayoutProvider();

        var kind = new CallEmissionKindResolver(new FakeProgram(), new FakeIntrinsics())
            .Resolve(request, GetCodeWriter(request.Instruction));
        Assert.Equal(redirected ? CallEmissionKind.SharedObjectDispatch : CallEmissionKind.VirtualDispatch, kind);
        if (constrainedToValue)
            return;
        Emit(redirected
            ? new SharedObjectDispatchCallEmitter(layouts, new ImplicitExceptionEmitter(layouts),
                new ConstrainedReferenceReceiverEmitter(layouts))
            : (ICallEmitter)CreateEmitter(layouts), request, indices);

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request.Instruction)).ToInstructions();
        Assert.Equal(redirected, instructions.Any(instruction =>
            instruction.Opcode == WasmOpcodes.Call && instruction.Operand?.UnsignedValue == 200));
        // A call that dispatches loads the receiver's type id; the shared call does not.
        Assert.Equal(!redirected, instructions.Any(instruction => instruction.Opcode == WasmOpcodes.I32Load));
        Assert.Equal([CliValueKind.I4], request.Instruction.Stack);
    }

    // The shared body takes the receiver and the call's arguments; a receiver the analysis proved non-null needs no
    // check, and a wasm64 receiver tests with i64.eqz.
    [Theory]
    [InlineData(WasmTarget.Wasm32, false, WasmOpcodes.I32EqualZero)]
    [InlineData(WasmTarget.Wasm64, false, WasmOpcodes.I64EqualZero)]
    [InlineData(WasmTarget.Wasm64, true, WasmOpcodes.I64EqualZero)]
    public void TheSharedBodyCallPassesTheArgumentsAndChecksAnUnprovenReceiver(
        WasmTarget target, bool proven, byte equalZero)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.ManagedReference);
        var declaration = Method(Key(0x06000051), ReferenceType(), signature);
        var targets = Enumerable.Range(0, SharedDispatchPolicy.MinimumTargets)
            .Select(index =>
            {
                var receiver = CliTypeIdentity.Named(Assembly, "Test", $"Shared{index}", isValueType: false);
                return new DispatchTargetModel(receiver, Method(Key(0x06000200 + index), receiver, signature));
            })
            .ToImmutableArray();
        var request = CreateDispatchRequest(declaration, targets,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference], consumed: 2, out var indices,
            shared: SharedBody.Separate);
        if (proven)
            request = request with { Instruction = WithNonNullProof(request.Instruction, stale: false) };
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));

        Emit(new SharedObjectDispatchCallEmitter(layouts, new ImplicitExceptionEmitter(layouts),
            new ConstrainedReferenceReceiverEmitter(layouts)), request, indices);

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request.Instruction)).ToInstructions();
        Assert.Equal(!proven, instructions.Any(instruction => instruction.Opcode == equalZero));
        // The receiver for its check, then the receiver and the argument for the call.
        Assert.Equal(proven ? 2 : 3, instructions.Count(instruction => instruction.Opcode == WasmOpcodes.LocalGet));
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.Call && instruction.Operand?.UnsignedValue == 200);
        Assert.Equal([CliValueKind.I4], request.Instruction.Stack);
    }

    [Fact]
    public void TheSharedBodyCallNeedsItsCollaboratorsAndRequest()
    {
        var layouts = new RecordingLayoutProvider();
        var exceptions = new ImplicitExceptionEmitter(layouts);
        var receivers = new ConstrainedReferenceReceiverEmitter(layouts);
        var emitter = new SharedObjectDispatchCallEmitter(layouts, exceptions, receivers);
        var request = CreateDispatchRequest(Method(Key(0x06000052), ReferenceType(), MethodSignatureModel.Create(
            CliValueKind.Void)), [], [CliValueKind.ManagedReference], consumed: 1, out var indices);
        var code = GetCodeWriter(request.Instruction);

        Assert.Throws<ArgumentNullException>(() => new SharedObjectDispatchCallEmitter(null!, exceptions, receivers));
        Assert.Throws<ArgumentNullException>(() => new SharedObjectDispatchCallEmitter(layouts, null!, receivers));
        Assert.Throws<ArgumentNullException>(() => new SharedObjectDispatchCallEmitter(layouts, exceptions, null!));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, code, indices));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(request, null!, indices));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(request, code, null!));
    }

    // A case serves a run of receiver type ids: its target's code is emitted once, behind one
    // unsigned comparison of the type id's distance from the run's start (issue 222).
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void ARunOfReceiversSharesOneTargetBehindOneComparison(int caseCount)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.ManagedReference);
        var convert = new MethodInstanceModel(
            new MethodDefinitionModel(Key(0x06000060), TypeKey, "System.IConvertible.ToInt32", false, signature, 1),
            ReferenceType(),
            [],
            signature);
        var targets = Enumerable.Range(0, caseCount * 3)
            .Select(index => new DispatchTargetModel(
                CliTypeIdentity.Named(Assembly, "Test", $"E{index}", isValueType: true), convert))
            .ToImmutableArray();
        var runs = Enumerable.Range(0, caseCount)
            .Select(index => new VirtualDispatchCase(targets[index * 3], 10 * (index + 1), 10 * (index + 1) + 2))
            .ToImmutableArray();
        var enumConvert = new RecordingEnumConvertEmitter();
        var request = CreateDispatchRequest(convert, targets, [CliValueKind.ManagedReference], consumed: 1, out var indices);

        Emit(
            (ICallEmitter)CreateEmitter(
                new RecordingLayoutProvider(),
                new IntrinsicRegistry(new Dictionary<EntityKey, RuntimeIntrinsic>
                {
                    [convert.Definition.Key] = RuntimeIntrinsic.EnumConvert,
                }),
                enumConvert: enumConvert,
                cases: new FixedCases(runs)),
            request,
            indices);

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request.Instruction)).ToInstructions();
        Assert.Equal(caseCount, enumConvert.Calls);
        var tests = instructions.Select((instruction, index) => (instruction, index))
            .Where(item => item.instruction.Opcode == WasmOpcodes.I32Subtract)
            .Select(item => instructions[item.index + 1])
            .ToArray();
        Assert.Equal(caseCount, tests.Length);
        Assert.All(tests, run => Assert.Equal(WasmInstructionOperand.Signed(3), run.Operand));
        Assert.Equal(caseCount > 1, instructions.Any(instruction =>
            instruction.Opcode == WasmOpcodes.I32Constant && instruction.Operand == WasmInstructionOperand.Signed(30)));
    }

    private sealed class FixedCases(ImmutableArray<VirtualDispatchCase> cases) : IVirtualDispatchCasePlanner
    {
        public ImmutableArray<VirtualDispatchCase> Plan(
            ImmutableArray<DispatchTargetModel> checkedTargets,
            ImmutableArray<DispatchTargetModel> receivers) => cases;
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

    // A call that names a method depending on its receiver's exact type (a struct's base.Equals)
    // dispatches on its receiver too; no other instruction dispatches as a call.
    [Theory]
    [InlineData(CilOperation.Call, true)]
    [InlineData(CilOperation.NewObject, false)]
    public void DispatchesACallOrCallvirtWhereAPlanExists(CilOperation operation, bool dispatches)
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
            operation: operation);
        var emitter = (ICallEmitter)CreateEmitter(new RecordingLayoutProvider());

        if (dispatches)
        {
            Emit(emitter, request, indices);
            Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request.Instruction));
            return;
        }
        var exception = Assert.Throws<InvalidOperationException>(() => Emit(emitter, request, indices));
        Assert.Contains("call or callvirt instruction", exception.Message);
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

    // A dispatch whose receiver analysis proved non-null, as newobj produced it, skips
    // its null check, unless the proof describes another snapshot of the body.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DispatchSkipsOnlyTheNullCheckAProofCovers(bool stale)
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
        request = request with { Instruction = WithNonNullProof(request.Instruction, stale) };

        Emit(
            (ICallEmitter)CreateEmitter(new RecordingLayoutProvider()),
            request,
            indices);

        Assert.Equal(stale, ((RecordingInstructionWriter)GetCodeWriter(request.Instruction)).ToInstructions()
            .Any(code => code.Opcode == WasmOpcodes.I32EqualZero));
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
        IValueTypeHashCodeEmitter? valueTypeHashCode = null,
        IStaticInitializationEmitter? initialization = null,
        IMethodInitializationTriggerClassifier? initializationTriggers = null,
        IVirtualDispatchCasePlanner? cases = null) => new(
        layouts,
        layouts,
        new VirtualDispatchEmissionPlanBuilder(),
        cases ?? new VirtualDispatchCasePlanner(layouts, intrinsics ?? new FakeIntrinsics()),
        intrinsics ?? new FakeIntrinsics(),
        new ImplicitExceptionEmitter(layouts),
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
        new ConstrainedReferenceReceiverEmitter(layouts),
        initialization ?? new StaticInitializationEmitter(new StaticInitializerGuardResolver(new FakeProgram(), new FakeProgram()),
            new NetWasm.Compiler.Wasm.Emission.Support.AddressInstructionEmitter(layouts)),
        initializationTriggers ?? new MethodInitializationTriggerClassifier(new FakeProgram()));

    private static CallEmissionRequest CreateDispatchRequest(
        MethodInstanceModel method,
        ImmutableArray<DispatchTargetModel> targets,
        IEnumerable<CliValueKind> stack,
        int consumed,
        out IFunctionIndexResolver functionIndices,
        CilOperation operation = CilOperation.CallVirtual,
        bool includeCaller = true,
        bool includeSite = true,
        MethodEmissionContext? context = null,
        SharedBody shared = SharedBody.None)
    {
        var program = new FakeProgram();
        var callerDefinition = program.GetMethod(EntryKey);
        var callerType = ReferenceType();
        var caller = new MethodInstanceModel(
            callerDefinition,
            callerType,
            [],
            callerDefinition.Signature);
        var sharedBody = shared switch
        {
            SharedBody.Separate => new MethodInstanceModel(
                new MethodDefinitionModel(Key(0x06000900), TypeKey, "DispatchGetHashCode", true,
                    MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.ManagedReference), 1),
                ReferenceType(), [], MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.ManagedReference)),
            SharedBody.Caller => caller,
            _ => null,
        };
        // As the program builder marks a site: many targets, and a shared body that is not the caller.
        var site = new DispatchCallSiteModel(
            caller.CanonicalName,
            0,
            method,
            targets)
        {
            SharesObjectDispatch = shared == SharedBody.Separate &&
                targets.Length >= SharedDispatchPolicy.MinimumTargets,
        };
        var target = CreateInstructionModuleTarget(program) with
        {
            DispatchCallSites = Lookups.Of(includeSite
                ? ImmutableDictionary<string, DispatchCallSiteModel>.Empty.Add(
                    site.Key,
                    site)
                : ImmutableDictionary<string, DispatchCallSiteModel>.Empty),
            SharedObjectDispatch = sharedBody is null
                ? ImmutableDictionary<string, MethodInstanceModel>.Empty
                : ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(method.CanonicalName, sharedBody),
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
            .Append(sharedBody is null ? default : (sharedBody.Definition.Key, new WasmFunctionIndex(200)))
            .Where(pair => pair.Item2 != default)
            .DistinctBy(pair => pair.Key)
            .ToImmutableDictionary(pair => pair.Key, pair => pair.Item2);
        var indices = new FunctionIndexMap(directMethods, [], [], []);
        functionIndices = CreateFunctionIndexResolver(program, indices);
        return new CallEmissionRequest(instruction, method, 0, consumed);
    }

    public enum SharedBody
    {
        None,
        Separate,
        Caller,
    }

    private static CliTypeIdentity ReferenceType() =>
        CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false);

    private static CliTypeIdentity ObjectType() =>
        CliTypeIdentity.Named(Assembly, "System", "Object", isValueType: false);

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
            int temporaryI4,
            OptionalFunctionIndex throwFunction)
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
            int temporaryI8,
            OptionalFunctionIndex throwFunction)
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
            int temporaryI4,
            OptionalFunctionIndex throwFunction)
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
            OptionalFunctionIndex throwFunction,
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
            OptionalFunctionIndex throwFunction,
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
