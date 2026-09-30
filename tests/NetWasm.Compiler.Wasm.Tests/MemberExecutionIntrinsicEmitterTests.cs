using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;
using static NetWasm.Compiler.Wasm.Tests.ExpressionIntrinsicEmissionFixture;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class MemberExecutionIntrinsicEmitterTests
{
    public static TheoryData<bool, bool, CliValueKind> Results
    {
        get
        {
            var data = new TheoryData<bool, bool, CliValueKind>();
            foreach (var memory64 in new[] { false, true })
            foreach (var isField in new[] { false, true })
            foreach (var kind in new[] { CliValueKind.ManagedReference, CliValueKind.I4,
                CliValueKind.I8, CliValueKind.F4, CliValueKind.F8 })
                data.Add(memory64, isField, kind);
            return data;
        }
    }

    [Theory]
    [InlineData("layouts")]
    [InlineData("addresses")]
    [InlineData("descriptors")]
    [InlineData("fields")]
    [InlineData("types")]
    [InlineData("values")]
    [InlineData("objects")]
    [InlineData("typeValidator")]
    [InlineData("initialization")]
    [InlineData("runtimeImports")]
    [InlineData("exceptions")]
    [InlineData("roots")]
    public void ConstructorRejectsMissingDependency(string dependency)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var error = Assert.Throws<ArgumentNullException>(() => Create(fixture, dependency));
        Assert.Equal(dependency, error.ParamName);
    }

    [Fact]
    public void EmitRejectsNullArgumentsBeforePublishingRoots()
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var emitter = Create(fixture);
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, fixture.Writer));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(
            fixture.Request(RuntimeIntrinsic.MemberExecuteMethod), null!));
        Assert.Equal(0, fixture.RootCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EmitRejectsMissingPlanOrContradictorySignatureBeforeOutput(int defect)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var method = Method("Factory", defect == 3 ? CliValueKind.I4 : CliValueKind.ManagedReference,
            isStatic: true,
            parameters: defect == 1
                ? [CliValueKind.ManagedReference, CliValueKind.ManagedReference]
                : [
                    CliValueKind.ManagedReference,
                    CliValueKind.ManagedReference,
                    defect == 2
                        ? CliValueKind.I4
                        : CliValueKind.ManagedReference,
                ]);
        var request = fixture.Request(RuntimeIntrinsic.MemberExecuteMethod, method,
            members: defect == 0 ? null : Members());

        var error = Assert.Throws<CompilerException>(() => Create(fixture).Emit(request, fixture.Writer));

        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
        Assert.Empty(fixture.Code);
        Assert.Empty(fixture.Functions.Methods);
        Assert.Equal(0, fixture.RootCount);
    }

    [Theory]
    [MemberData(nameof(Results))]
    public void EmitPreservesMethodAndFieldResultsAcrossBoxing(bool memory64, bool readField, CliValueKind kind)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(memory64);
        var method = Method("Read", kind);
        var field = new FieldInstanceModel(new FieldDefinitionModel(EmitterTestSupport.InstanceFieldKey,
            EmitterTestSupport.TypeKey, "Value", kind, false), Type(), CliTypeIdentity.FromStackKind(kind));
        var request = fixture.Request(readField ? RuntimeIntrinsic.MemberReadField : RuntimeIntrinsic.MemberExecuteMethod,
            members: readField ? Members(field: field) : Members(method));

        Create(fixture).Emit(request, fixture.Writer);

        Assert.Equal(1, fixture.RootCount);
        Assert.Same(request.Instruction, fixture.PublishedRequest);
        AssertNullReceiverBranch(fixture, request);
        Assert.Contains(fixture.Code, instruction =>
            instruction.Opcode == (memory64 ? WasmOpcodes.I64Equal : WasmOpcodes.I32Equal));
        Assert.Contains(fixture.Code, instruction =>
            memory64
                ? instruction.Operand?.Signed64Value == (readField ? 228 : 224)
                : instruction.Operand?.SignedValue == (readField ? 228 : 224));
        Assert.Equal(readField ? null : method, fixture.Layouts.MethodDescriptorRequest);
        Assert.Equal(readField ? field : null, fixture.Layouts.FieldDescriptorRequest);
        var producer = readField
            ? fixture.Code.Single(i => i.Operand?.Offset == 52)
            : fixture.Code.Single(i => i.Opcode == WasmOpcodes.Call && i.Operand!.UnsignedValue == 71);
        if (readField)
            Assert.Equal(LoadOpcode(kind, memory64), producer.Opcode);
        var producerIndex = fixture.Code.IndexOf(producer);
        var temporary = kind switch
        {
            CliValueKind.ManagedReference => request.Local(0, kind),
            CliValueKind.I4 or CliValueKind.F4 => request.Instruction.Context.NumericTemporaryI4,
            _ => request.Instruction.Context.NumericTemporaryI8,
        };
        var floating = kind is CliValueKind.F4 or CliValueKind.F8;
        Assert.Equal(Local(WasmOpcodes.LocalSet, temporary), fixture.Code[producerIndex + (floating ? 2 : 1)]);
        if (floating)
            Assert.Equal(kind == CliValueKind.F4 ? WasmOpcodes.I32ReinterpretF32 : WasmOpcodes.I64ReinterpretF64,
                fixture.Code[producerIndex + 1].Opcode);
        Assert.Equal(WasmOpcodes.Else, fixture.Code[^4].Opcode);
        Assert.Equal(Local(WasmOpcodes.Call, 72), fixture.Code[^3]);
        Assert.Equal(Local(WasmOpcodes.LocalSet, request.Local(0, CliValueKind.ManagedReference)), fixture.Code[^2]);
        Assert.Equal(WasmOpcodes.End, fixture.Code[^1].Opcode);
        if (kind == CliValueKind.ManagedReference)
        {
            Assert.Equal(
                readField
                    ? [ManagedExceptionKind.NullReference]
                    : [ManagedExceptionKind.InvalidOperation,
                        ManagedExceptionKind.NullReference],
                fixture.Exceptions.Kinds);
            Assert.DoesNotContain(fixture.Code, i => i.Opcode == WasmOpcodes.Call &&
                i.Operand!.UnsignedValue == fixture.Imports.Resolve(RuntimeImportSymbol.Allocate));
            Assert.Equal(Local(WasmOpcodes.LocalGet, temporary), fixture.Code[producerIndex + 2]);
            Assert.Equal(Local(WasmOpcodes.LocalSet, temporary), fixture.Code[producerIndex + 3]);
            return;
        }

        Assert.Equal(
            readField
                ? [ManagedExceptionKind.NullReference,
                    ManagedExceptionKind.OutOfMemory]
                : [ManagedExceptionKind.InvalidOperation,
                    ManagedExceptionKind.NullReference,
                    ManagedExceptionKind.OutOfMemory],
            fixture.Exceptions.Kinds);
        var allocation = fixture.Code.IndexOf(fixture.Code.Single(i => i.Opcode == WasmOpcodes.Call &&
            i.Operand!.UnsignedValue == fixture.Imports.Resolve(RuntimeImportSymbol.Allocate)));
        Assert.True(allocation > producerIndex + 1);
        Assert.Equal(Local(WasmOpcodes.LocalSet, request.Local(0, CliValueKind.ManagedReference)), fixture.Code[allocation + 1]);
        Assert.Equal(memory64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero, fixture.Code[allocation + 3].Opcode);
        Assert.Equal(WasmOpcodes.If, fixture.Code[allocation + 4].Opcode);
        Assert.Equal(WasmOpcodes.Throw, fixture.Code[allocation + 5].Opcode);
        Assert.Equal(WasmOpcodes.End, fixture.Code[allocation + 6].Opcode);
        Assert.Equal(WasmTargetLayout.Align(fixture.Target.ObjectHeaderSize,
            fixture.Scalars.GetValueLayout(field.FieldType).Alignment),
            memory64 ? fixture.Code[allocation + 8].Operand!.Signed64Value : fixture.Code[allocation + 8].Operand!.SignedValue);
        Assert.Equal(Local(WasmOpcodes.LocalGet, temporary), fixture.Code[allocation + 10]);
        if (floating)
            Assert.Equal(kind == CliValueKind.F4 ? WasmOpcodes.F32ReinterpretI32 : WasmOpcodes.F64ReinterpretI64,
                fixture.Code[allocation + 11].Opcode);
        var store = fixture.Code[^5];
        Assert.Equal(StoreOpcode(kind), store.Opcode);
        Assert.Equal(0u, store.Operand!.Offset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmitStaticZeroArgumentMethodAcceptsNullReceiverAndInitializesBeforeCall(
        bool memory64)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(memory64);
        var method = Method(
            "StaticRead",
            CliValueKind.ManagedReference,
            Type("StaticOwner", valueType: true),
            isStatic: true);
        var request = fixture.Request(
            RuntimeIntrinsic.MemberExecuteMethod,
            members: Members(method));

        Create(fixture).Emit(request, fixture.Writer);

        Assert.DoesNotContain(
            ManagedExceptionKind.NullReference,
            fixture.Exceptions.Kinds);
        var initialization = Assert.Single(fixture.Initialization.Requests);
        Assert.Equal(method.Definition.DeclaringType, initialization.TypeDefinition);
        Assert.Equal(method.DeclaringType, initialization.DeclaringType);
        Assert.True(initialization.IsStaticMethodCall);
        Assert.Same(
            request.FunctionIndices,
            Assert.Single(fixture.Initialization.FunctionResolvers));
        var call = fixture.Code.IndexOf(Local(WasmOpcodes.Call, 71));
        Assert.True(Assert.Single(fixture.Initialization.InstructionCounts) <= call);
        Assert.Contains(
            Local(
                WasmOpcodes.LocalGet,
                request.Local(2, CliValueKind.ManagedReference)),
            fixture.Code);
    }

    [Theory]
    [InlineData(false, 0, 1)]
    [InlineData(true, 0, 1)]
    [InlineData(false, 1, 2)]
    [InlineData(true, 1, 2)]
    public void EmitTreatsArgumentArrayArityMismatchAsInternalInvariantFailure(
        bool memory64,
        int parameterCount,
        int expectedFailures)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(memory64);
        var reference = CliTypeIdentity.FromStackKind(
            CliValueKind.ManagedReference);
        var method = TypedMethod(
            "Map",
            reference,
            Type("StaticOwner"),
            isStatic: true,
            parameters: Enumerable.Repeat(reference, parameterCount).ToArray());
        var request = fixture.Request(
            RuntimeIntrinsic.MemberExecuteMethod,
            members: Members(method));

        Create(fixture).Emit(request, fixture.Writer);

        Assert.Equal(
            expectedFailures,
            fixture.Exceptions.Kinds.Count(kind =>
                kind == ManagedExceptionKind.InvalidOperation));
        Assert.Single(
            fixture.Functions.Methods,
            candidate => candidate.Definition.Name == "Unsupported");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmitLoadsMixedArgumentsInDeclaredOrder(bool memory64)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(memory64);
        var reference = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);
        var i4 = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var f8 = CliTypeIdentity.FromStackKind(CliValueKind.F8);
        var method = TypedMethod(
            "Mix",
            reference,
            Type("StaticOwner"),
            isStatic: true,
            parameters: [reference, i4, f8]);
        var request = fixture.Request(
            RuntimeIntrinsic.MemberExecuteMethod,
            members: Members(method));

        Create(fixture).Emit(request, fixture.Writer);

        Assert.Equal(
            [i4, f8],
            fixture.TypeValidator.Requests.Select(entry => entry.Type));
        Assert.All(
            fixture.TypeValidator.Requests,
            entry => Assert.Equal(
                request.Instruction.Context.ObjectTemporary,
                entry.ObjectLocal));
        Assert.Equal(
            [method],
            fixture.Functions.Methods.Where(candidate =>
                candidate.Definition.Name != "Unsupported"));
        var firstArgumentLoad = fixture.Code.IndexOf(fixture.Code.First(instruction =>
            instruction.Opcode ==
                (memory64 ? WasmOpcodes.I64Load : WasmOpcodes.I32Load) &&
            instruction.Operand?.Offset ==
                (uint)fixture.Layouts.ArrayDataPointerOffset));
        Assert.True(
            Assert.Single(fixture.Initialization.InstructionCounts) <=
            firstArgumentLoad);
        Assert.Contains(fixture.Code, instruction =>
            AddressConstantEquals(
                instruction,
                fixture.Target,
                fixture.Target.ObjectReferenceSize));
        Assert.Contains(fixture.Code, instruction =>
            AddressConstantEquals(
                instruction,
                fixture.Target,
                2 * fixture.Target.ObjectReferenceSize));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmitUnboxesNarrowAndFloatingArgumentsWithExactLoads(bool memory64)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(memory64);
        var parameters = new[]
        {
            CliTypeIdentity.Primitive("i1", CliValueKind.I4),
            CliTypeIdentity.Primitive("u1", CliValueKind.I4),
            CliTypeIdentity.Primitive("i2", CliValueKind.I4),
            CliTypeIdentity.Primitive("u2", CliValueKind.I4),
            CliTypeIdentity.Primitive("f4", CliValueKind.F4),
            CliTypeIdentity.Primitive("f8", CliValueKind.F8),
        };
        var method = TypedMethod(
            "Scalars",
            CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference),
            Type("StaticOwner"),
            isStatic: true,
            parameters: parameters);
        var request = fixture.Request(
            RuntimeIntrinsic.MemberExecuteMethod,
            members: Members(method));

        Create(fixture).Emit(request, fixture.Writer);

        Assert.Equal(
            parameters,
            fixture.TypeValidator.Requests.Select(entry => entry.Type));
        Assert.Contains(fixture.Code, instruction =>
            instruction.Opcode == WasmOpcodes.I32Load8Signed);
        Assert.Contains(fixture.Code, instruction =>
            instruction.Opcode == WasmOpcodes.I32Load8Unsigned);
        Assert.Contains(fixture.Code, instruction =>
            instruction.Opcode == WasmOpcodes.I32Load16Signed);
        Assert.Contains(fixture.Code, instruction =>
            instruction.Opcode == WasmOpcodes.I32Load16Unsigned);
        Assert.Contains(fixture.Code, instruction =>
            instruction.Opcode == WasmOpcodes.F32Load);
        Assert.Contains(fixture.Code, instruction =>
            instruction.Opcode == WasmOpcodes.F64Load);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void EmitDispatchesVirtualAndInterfaceTargetsAndAdjustsBoxedReceivers(bool memory64, bool interfaceCall, bool boxed)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(memory64);
        var descriptor = Method(
            "Read",
            CliValueKind.ManagedReference,
            Type(interfaceCall ? "IReader" : "Base"),
            isVirtual: true);
        var receiver = Type("Concrete", boxed);
        var implementation = Method("Implementation", CliValueKind.ManagedReference, receiver);
        var request = fixture.Request(RuntimeIntrinsic.MemberExecuteMethod,
            members: Members(descriptor, dispatch: true, targets: [new(receiver, implementation)]));

        Create(fixture).Emit(request, fixture.Writer);

        Assert.Equal(
            [implementation],
            fixture.Functions.Methods.Where(method =>
                method.Definition.Name != "Unsupported"));
        Assert.Equal(1, fixture.Functions.Methods.Count(method =>
            method.Definition.Name == "Unsupported"));
        Assert.Equal(
            [ManagedExceptionKind.InvalidOperation,
                ManagedExceptionKind.NullReference,
                ManagedExceptionKind.InvalidCast],
            fixture.Exceptions.Kinds);
        Assert.Equal(receiver, fixture.Layouts.ObjectIdentityRequest);
        Assert.Contains(fixture.Code, instruction =>
            instruction.Opcode == WasmOpcodes.I32Load &&
            instruction.Operand?.Offset == 0);
        Assert.Contains(fixture.Code, instruction =>
            instruction.Opcode == WasmOpcodes.I32Constant &&
            instruction.Operand?.SignedValue == 9);
        var call = fixture.Code.IndexOf(Local(WasmOpcodes.Call, 71));
        if (boxed)
        {
            Assert.Equal(memory64 ? WasmOpcodes.I64Add : WasmOpcodes.I32Add, fixture.Code[call - 1].Opcode);
            Assert.Equal(fixture.Target.ObjectHeaderSize,
                memory64 ? fixture.Code[call - 2].Operand!.Signed64Value : fixture.Code[call - 2].Operand!.SignedValue);
            Assert.Equal(Local(WasmOpcodes.LocalGet, request.Local(1, CliValueKind.ManagedReference)), fixture.Code[call - 3]);
        }
        else
            Assert.Equal(Local(WasmOpcodes.LocalGet, request.Local(1, CliValueKind.ManagedReference)), fixture.Code[call - 1]);
        var invalidCast = fixture.Code.IndexOf(fixture.Code.Single(i => i.Opcode == WasmOpcodes.Throw &&
            i.Operand!.UnsignedValue == (uint)ManagedExceptionKind.InvalidCast));
        Assert.Equal(WasmOpcodes.Else, fixture.Code[invalidCast - 1].Opcode);
        Assert.Equal(WasmOpcodes.End, fixture.Code[invalidCast + 1].Opcode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EmitEmptyPlansCallUnsupportedTargetWithoutInspectingReceiver(bool memory64, bool readField)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(memory64);
        var request = fixture.Request(readField ? RuntimeIntrinsic.MemberReadField : RuntimeIntrinsic.MemberExecuteMethod,
            members: Members());
        Create(fixture).Emit(request, fixture.Writer);
        Assert.Equal(2, fixture.Code.Length);
        Assert.Equal(Local(WasmOpcodes.Call, 72), fixture.Code[0]);
        Assert.Equal(Local(WasmOpcodes.LocalSet, request.Local(0, CliValueKind.ManagedReference)), fixture.Code[1]);
        Assert.Empty(fixture.Exceptions.Kinds);
        Assert.Equal(1, fixture.RootCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmitOrdersMultipleDescriptorCasesDeterministically(bool readField)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var firstMethod = Method("First", CliValueKind.ManagedReference, Type("First"));
        var secondMethod = Method("Second", CliValueKind.ManagedReference, Type("Second"));
        var firstField = new FieldInstanceModel(
            new FieldDefinitionModel(
                new(EmitterTestSupport.Assembly, 0x04000011),
                EmitterTestSupport.TypeKey,
                "First",
                CliValueKind.ManagedReference,
                false),
            Type("First"),
            CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference));
        var secondField = new FieldInstanceModel(
            new FieldDefinitionModel(
                new(EmitterTestSupport.Assembly, 0x04000012),
                EmitterTestSupport.TypeKey,
                "Second",
                CliValueKind.ManagedReference,
                false),
            Type("Second"),
            CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference));
        var methods = ImmutableDictionary<string, MemberMethodExecutionPlan>.Empty
            .Add(secondMethod.CanonicalName, new(secondMethod, false, []))
            .Add(firstMethod.CanonicalName, new(firstMethod, false, []));
        var fields = ImmutableDictionary<string, FieldInstanceModel>.Empty
            .Add(secondField.CanonicalName, secondField)
            .Add(firstField.CanonicalName, firstField);
        var plan = new MemberExecutionPlan(
            readField
                ? ImmutableDictionary<string, MemberMethodExecutionPlan>.Empty
                : methods,
            readField
                ? fields
                : ImmutableDictionary<string, FieldInstanceModel>.Empty,
            Method("Unsupported", CliValueKind.ManagedReference, isStatic: true));
        var request = fixture.Request(
            readField
                ? RuntimeIntrinsic.MemberReadField
                : RuntimeIntrinsic.MemberExecuteMethod,
            members: plan);

        Create(fixture).Emit(request, fixture.Writer);

        Assert.Equal(2, fixture.Code.Count(instruction =>
            instruction.Opcode == WasmOpcodes.Else));
        Assert.Equal(1, fixture.RootCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmitDispatchWithoutTargetsEmitsInvalidCastAndDescriptorFallback(bool memory64)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(memory64);
        var request = fixture.Request(RuntimeIntrinsic.MemberExecuteMethod,
            members: Members(
                Method("Read", CliValueKind.I4, isVirtual: true),
                dispatch: true));
        Create(fixture).Emit(request, fixture.Writer);
        Assert.Equal(
            [ManagedExceptionKind.InvalidOperation,
                ManagedExceptionKind.NullReference,
                ManagedExceptionKind.InvalidCast],
            fixture.Exceptions.Kinds);
        Assert.DoesNotContain(
            fixture.Functions.Methods,
            method => method.Definition.Name != "Unsupported");
        Assert.Equal(1, fixture.Functions.Methods.Count(method =>
            method.Definition.Name == "Unsupported"));
        Assert.Contains(fixture.Code, instruction =>
            instruction.Opcode == WasmOpcodes.Throw &&
            instruction.Operand?.UnsignedValue ==
                (uint)ManagedExceptionKind.InvalidCast);
    }

    [Theory]
    [InlineData(CliValueKind.Void)]
    [InlineData(CliValueKind.NativeInt)]
    [InlineData(CliValueKind.ValueType)]
    public void EmitRejectsUnpreservableResultWithRuntimeContract(CliValueKind kind)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var request = fixture.Request(RuntimeIntrinsic.MemberExecuteMethod, members: Members(Method("Read", kind)));
        var error = Assert.Throws<CompilerException>(() => Create(fixture).Emit(request, fixture.Writer));
        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
        Assert.Empty(fixture.Code);
        Assert.Empty(fixture.Functions.Methods);
        Assert.Equal(0, fixture.RootCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EmitRejectsContradictoryMethodPlansBeforePublishingRoots(int defect)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var reference = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);
        var int32 = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var boolean = CliTypeIdentity.Primitive("bool", CliValueKind.I4);
        var owner = Type("Owner");
        var descriptor = defect switch
        {
            0 => TypedMethod(
                "InvalidParameter",
                reference,
                owner,
                isStatic: false,
                parameters: [
                    CliTypeIdentity.FromStackKind(CliValueKind.NativeInt),
                ]),
            1 => TypedMethod(
                "StaticVirtual",
                reference,
                owner,
                isStatic: true,
                isVirtual: true),
            3 or 4 => TypedMethod(
                "Virtual",
                reference,
                owner,
                isStatic: false,
                isVirtual: true,
                parameters: [int32]),
            5 => TypedMethod(
                "Virtual",
                int32,
                owner,
                isStatic: false,
                isVirtual: true),
            _ => TypedMethod(
                "Direct",
                reference,
                owner,
                isStatic: false),
        };
        var target = defect switch
        {
            3 => TypedMethod(
                "Target",
                reference,
                Type("Concrete"),
                isStatic: false,
                parameters: [CliTypeIdentity.FromStackKind(CliValueKind.I8)]),
            4 => TypedMethod(
                "Target",
                reference,
                Type("Concrete"),
                isStatic: false,
                parameters: [boolean]),
            5 => TypedMethod(
                "Target",
                boolean,
                Type("Concrete"),
                isStatic: false),
            _ => TypedMethod(
                "Target",
                reference,
                Type("Concrete"),
                isStatic: false),
        };
        var requiresDispatch = defect is 1 or >= 3;
        var targets = defect is 2 or >= 3
            ? ImmutableArray.Create(new DispatchTargetModel(target.DeclaringType, target))
            : [];
        var plan = new MemberExecutionPlan(
            ImmutableDictionary<string, MemberMethodExecutionPlan>.Empty.Add(
                descriptor.CanonicalName,
                new(descriptor, requiresDispatch, targets)),
            ImmutableDictionary<string, FieldInstanceModel>.Empty,
            Method(
                "Unsupported",
                CliValueKind.ManagedReference,
                isStatic: true));
        var request = fixture.Request(
            RuntimeIntrinsic.MemberExecuteMethod,
            members: plan);

        var error = Assert.Throws<CompilerException>(() =>
            Create(fixture).Emit(request, fixture.Writer));

        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
        Assert.Empty(fixture.Code);
        Assert.Empty(fixture.Functions.Methods);
        Assert.Equal(0, fixture.RootCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EmitRejectsContradictoryFieldPlansBeforePublishingRoots(int defect)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var fieldType = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var field = new FieldInstanceModel(
            new FieldDefinitionModel(
                new(EmitterTestSupport.Assembly, 0x04000021),
                EmitterTestSupport.TypeKey,
                "Value",
                fieldType,
                defect == 1),
            Type("Owner", valueType: defect == 2),
            fieldType);
        var fields = ImmutableDictionary<string, FieldInstanceModel>.Empty.Add(
            defect == 0 ? "contradictory-key" : field.CanonicalName,
            field);
        var plan = new MemberExecutionPlan(
            ImmutableDictionary<string, MemberMethodExecutionPlan>.Empty,
            fields,
            Method(
                "Unsupported",
                CliValueKind.ManagedReference,
                isStatic: true));
        var request = fixture.Request(
            RuntimeIntrinsic.MemberReadField,
            members: plan);

        var error = Assert.Throws<CompilerException>(() =>
            Create(fixture).Emit(request, fixture.Writer));

        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
        Assert.Empty(fixture.Code);
        Assert.Empty(fixture.Functions.Methods);
        Assert.Equal(0, fixture.RootCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmitBoxedReceiverCallingReferenceBaseMethodKeepsObjectHeader(bool memory64)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(memory64);
        var descriptor = Method(
            "Read",
            CliValueKind.ManagedReference,
            isVirtual: true);
        var implementation = Method("Implementation", CliValueKind.ManagedReference, Type("Object"));
        var request = fixture.Request(RuntimeIntrinsic.MemberExecuteMethod,
            members: Members(descriptor, dispatch: true, targets: [new(Type("Struct", true), implementation)]));

        Create(fixture).Emit(request, fixture.Writer);

        var call = fixture.Code.IndexOf(Local(WasmOpcodes.Call, 71));
        Assert.Equal(Local(WasmOpcodes.LocalGet, request.Local(1, CliValueKind.ManagedReference)), fixture.Code[call - 1]);
        Assert.DoesNotContain(fixture.Code, instruction => instruction.Opcode is WasmOpcodes.I32Add or WasmOpcodes.I64Add);
        Assert.Equal(
            implementation,
            Assert.Single(
                fixture.Functions.Methods,
                method => method.Definition.Name != "Unsupported"));
    }

    [Fact]
    public void EmitRejectsUnrelatedIntrinsicWithRuntimeContract()
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var request = fixture.Request(RuntimeIntrinsic.StringLength, members: Members());

        var error = Assert.Throws<CompilerException>(() => Create(fixture).Emit(request, fixture.Writer));

        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
        Assert.Empty(fixture.Code);
        Assert.Empty(fixture.Functions.Methods);
        Assert.Equal(0, fixture.RootCount);
    }

    private static void AssertNullReceiverBranch(ExpressionIntrinsicEmissionFixture fixture, RuntimeIntrinsicEmissionRequest request)
    {
        var receiver = Local(
            WasmOpcodes.LocalGet,
            request.Local(1, CliValueKind.ManagedReference));
        var start = -1;
        for (var index = 0; index <= fixture.Code.Length - 5; index++)
        {
            if (fixture.Code[index].Equals(receiver) &&
                fixture.Code[index + 1].Opcode ==
                    (fixture.Target.UsesMemory64
                        ? WasmOpcodes.I64EqualZero
                        : WasmOpcodes.I32EqualZero) &&
                fixture.Code[index + 2].Opcode == WasmOpcodes.If &&
                fixture.Code[index + 3].Opcode == WasmOpcodes.Throw &&
                fixture.Code[index + 3].Operand!.UnsignedValue ==
                    (uint)ManagedExceptionKind.NullReference &&
                fixture.Code[index + 4].Opcode == WasmOpcodes.End)
            {
                start = index;
                break;
            }
        }
        Assert.True(start >= 0);
    }

    private static byte LoadOpcode(CliValueKind kind, bool memory64) => kind switch
    {
        CliValueKind.ManagedReference => memory64 ? WasmOpcodes.I64Load : WasmOpcodes.I32Load,
        CliValueKind.I8 => WasmOpcodes.I64Load,
        CliValueKind.F4 => WasmOpcodes.F32Load,
        CliValueKind.F8 => WasmOpcodes.F64Load,
        _ => WasmOpcodes.I32Load,
    };

    private static byte StoreOpcode(CliValueKind kind) => kind switch
    {
        CliValueKind.I8 => WasmOpcodes.I64Store,
        CliValueKind.F4 => WasmOpcodes.F32Store,
        CliValueKind.F8 => WasmOpcodes.F64Store,
        _ => WasmOpcodes.I32Store,
    };

    private static bool AddressConstantEquals(
        WasmInstruction instruction,
        WasmTargetLayout target,
        int value) => target.UsesMemory64
        ? instruction.Opcode == WasmOpcodes.I64Constant &&
          instruction.Operand?.Signed64Value == value
        : instruction.Opcode == WasmOpcodes.I32Constant &&
          instruction.Operand?.SignedValue == value;

    private static MethodInstanceModel TypedMethod(
        string name,
        CliTypeIdentity result,
        CliTypeIdentity owner,
        bool isStatic,
        bool isVirtual = false,
        params CliTypeIdentity[] parameters)
    {
        var signature = MethodSignatureModel.Create(result, parameters);
        var definition = new MethodDefinitionModel(
            EmitterTestSupport.EntryKey,
            EmitterTestSupport.TypeKey,
            name,
            isStatic,
            signature,
            1)
        {
            IsVirtual = isVirtual,
        };
        return new(definition, owner, [], signature);
    }

    private static MemberExecutionIntrinsicEmitter Create(ExpressionIntrinsicEmissionFixture fixture, string? missing = null) => new(
        missing == "layouts" ? null! : fixture.Layouts,
        missing == "addresses" ? null! : new AddressInstructionEmitter(fixture.Layouts),
        missing == "descriptors" ? null! : fixture.Layouts,
        missing == "fields" ? null! : fixture.Scalars,
        missing == "types" ? null! : fixture.Layouts,
        missing == "values" ? null! : fixture.Scalars,
        missing == "objects" ? null! : fixture.Layouts,
        missing == "typeValidator" ? null! : fixture.TypeValidator,
        missing == "initialization" ? null! : fixture.Initialization,
        missing == "runtimeImports" ? null! : fixture.Imports,
        missing == "exceptions" ? null! : fixture.Exceptions,
        missing == "roots" ? null! : fixture.Roots);
}
