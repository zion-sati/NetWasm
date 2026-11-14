using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Wasm.Emission;
using System.Reflection;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.MemberExecution;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;
using static NetWasm.Compiler.Wasm.Tests.ExpressionIntrinsicEmissionFixture;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class MemberExecutionIntrinsicEmitterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitializesTheSelectedValueDispatchTargetBeforeUnboxingAndCalling(bool memory64)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(memory64);
        var descriptor = Method("Read", CliValueKind.I4, Type("IRead"), isVirtual: true);
        var target = Method("ValueRead", CliValueKind.I4, Type("Value", valueType: true));
        var reference = Method("ReferenceRead", CliValueKind.I4, Type("Reference"));
        fixture.InitializationTriggers.Add(target.Definition);
        var request = fixture.Request(RuntimeIntrinsic.MemberExecuteMethod,
            members: Members(descriptor, true,
                [new(target.DeclaringType, target), new(reference.DeclaringType, reference)]));

        Create(fixture).Emit(request, fixture.Writer);

        var initialization = Assert.Single(fixture.Initialization.Requests);
        Assert.Equal(target.DeclaringType, initialization.DeclaringType);
        Assert.True(initialization.IsMethodInvocation);
        var before = Assert.Single(fixture.Initialization.InstructionCounts);
        Assert.Equal(WasmOpcodes.If, fixture.Code[before - 1].Opcode);
        Assert.Equal(WasmOpcodes.LocalGet, fixture.Code[before].Opcode);
        Assert.Same(request.FunctionIndices, Assert.Single(fixture.Initialization.FunctionResolvers));
    }

    public static TheoryData<bool, bool, CliValueKind> NullableResults
    {
        get
        {
            var data = new TheoryData<bool, bool, CliValueKind>();
            foreach (var memory64 in new[] { false, true })
                foreach (var dispatch in new[] { false, true })
                    foreach (var kind in new[] { CliValueKind.I4, CliValueKind.I8, CliValueKind.F4, CliValueKind.F8 })
                        data.Add(memory64, dispatch, kind);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(NullableResults))]
    public void NullableGettersReceiveReturnStorageBeforeReceiverAndBoxAfterCall(
        bool memory64, bool dispatch, CliValueKind underlyingKind)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(memory64);
        var underlying = CliTypeIdentity.FromStackKind(underlyingKind);
        var nullable = CliTypeIdentity.GenericInstantiation(Type("Nullable`1", true), [underlying]);
        fixture.NullableTypes.Add(nullable, underlying);
        var descriptor = TypedMethod("Get", nullable, Type("Owner"), false, dispatch);
        var implementation = TypedMethod("Implementation", nullable, Type("Derived"), false);
        var request = fixture.Request(RuntimeIntrinsic.MemberExecuteMethod,
            members: Members(descriptor, dispatch,
                dispatch ? [new(implementation.DeclaringType, implementation)] : []));
        var instruction = request.Instruction;
        request = request with
        {
            Call = request.Call with
            {
                Instruction = instruction with
                {
                    Context = instruction.Context with
                    {
                        ValueLayout = instruction.Context.ValueLayout with
                        {
                            MemberResultOffsets = ImmutableDictionary<int, int>.Empty.Add(instruction.Instruction.Offset, 32),
                        }
                    },
                }
            }
        };

        Create(fixture).Emit(request, fixture.Writer);

        Assert.Equal(1, fixture.RootCount);
        Assert.Equal(2, fixture.ValueAddresses.Count);
        Assert.All(fixture.ValueAddresses, address =>
        {
            Assert.Same(request.Instruction.Context, address.Context);
            Assert.Equal(32, address.Offset);
        });
        var call = fixture.Code.IndexOf(Local(WasmOpcodes.Call, 71));
        Assert.Equal(call - 2, fixture.ValueAddresses[0].CodeIndex);
        Assert.Equal(Local(WasmOpcodes.LocalGet, request.Local(1, CliValueKind.ManagedReference)), fixture.Code[call - 1]);
        Assert.Equal(call + 1, fixture.ValueAddresses[1].CodeIndex);
        var box = Assert.Single(fixture.NullableBoxes);
        Assert.Equal(underlying, box.Underlying);
        Assert.Equal(request.Instruction.Context.ObjectTemporary, box.Source);
        Assert.Equal(request.Local(0, CliValueKind.ManagedReference), box.Result);
        Assert.Equal(Local(WasmOpcodes.LocalSet, box.Source), fixture.Code[box.CodeIndex - 1]);
        Assert.Equal(call + 3, box.CodeIndex);
        Assert.Equal(dispatch ? implementation : descriptor,
            Assert.Single(fixture.Functions.Methods, method => method.Definition.Name != "Unsupported"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableFieldBoxesItsAddressWithoutLoadingAnAggregate(bool memory64)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(memory64);
        var underlying = CliTypeIdentity.FromStackKind(CliValueKind.I8);
        var nullable = CliTypeIdentity.GenericInstantiation(Type("Nullable`1", true), [underlying]);
        fixture.NullableTypes.Add(nullable, underlying);
        var field = new FieldInstanceModel(new FieldDefinitionModel(EmitterTestSupport.InstanceFieldKey,
            EmitterTestSupport.TypeKey, "Value", nullable, false), Type(), nullable);
        var request = fixture.Request(RuntimeIntrinsic.MemberReadField, members: Members(field: field));

        Create(fixture).Emit(request, fixture.Writer);

        var box = Assert.Single(fixture.NullableBoxes);
        Assert.Equal(underlying, box.Underlying);
        Assert.Equal(request.Instruction.Context.ObjectTemporary, box.Source);
        Assert.Equal(request.Local(0, CliValueKind.ManagedReference), box.Result);
        Assert.Equal(Local(WasmOpcodes.LocalGet, request.Local(1, CliValueKind.ManagedReference)), fixture.Code[box.CodeIndex - 4]);
        Assert.True(AddressConstantEquals(fixture.Code[box.CodeIndex - 3], fixture.Target, 52));
        Assert.Equal(memory64 ? WasmOpcodes.I64Add : WasmOpcodes.I32Add, fixture.Code[box.CodeIndex - 2].Opcode);
        Assert.Equal(Local(WasmOpcodes.LocalSet, box.Source), fixture.Code[box.CodeIndex - 1]);
        Assert.Empty(fixture.ValueAddresses);
        Assert.DoesNotContain(fixture.Code, instruction => instruction.Operand?.Offset == 52);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableGetterWithoutReturnStorageFailsBeforePublishingRoots(bool memory64)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(memory64);
        var underlying = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var nullable = CliTypeIdentity.GenericInstantiation(Type("Nullable`1", true), [underlying]);
        fixture.NullableTypes.Add(nullable, underlying);
        var request = fixture.Request(RuntimeIntrinsic.MemberExecuteMethod,
            members: Members(TypedMethod("Get", nullable, Type("Owner"), false)));

        var error = Assert.Throws<CompilerException>(() => Create(fixture).Emit(request, fixture.Writer));

        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
        Assert.Equal(0, fixture.RootCount);
        Assert.Empty(fixture.Code);
        Assert.Empty(fixture.NullableBoxes);
        Assert.Empty(fixture.ValueAddresses);
    }

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

    // Every collaborator of member execution rejects each missing dependency by name.
    [Fact]
    public void EachCollaboratorRejectsAMissingDependency()
    {
        var parts = new Parts(new ExpressionIntrinsicEmissionFixture(false));
        foreach (var (type, arguments) in parts.Constructions())
        {
            var constructor = Assert.Single(type.GetConstructors());
            for (var index = 0; index < arguments.Length; index++)
            {
                var missing = (object?[])arguments.Clone();
                missing[index] = null;
                var error = Assert.Throws<TargetInvocationException>(() => constructor.Invoke(missing));
                Assert.Equal(constructor.GetParameters()[index].Name,
                    Assert.IsType<ArgumentNullException>(error.InnerException).ParamName);
            }
        }
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
        Assert.True(initialization.IsMethodInvocation);
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

    // Void results produce null and structs are boxed; other kinds have no box here.
    [Theory]
    [InlineData(CliValueKind.NativeInt)]
    [InlineData(CliValueKind.ManagedAddress)]
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
        // A contradictory key, a constant (which has no storage), and a field type with no box.
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var fieldType = CliTypeIdentity.FromStackKind(defect == 2 ? CliValueKind.NativeInt : CliValueKind.I4);
        var field = new FieldInstanceModel(
            new FieldDefinitionModel(
                new(EmitterTestSupport.Assembly, 0x04000021),
                EmitterTestSupport.TypeKey,
                "Value",
                fieldType,
                defect == 1)
            {
                IsLiteral = defect == 1,
            },
            Type("Owner"),
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

    // Stage 3: New allocates the object, publishes it as the call's allocation root (as newobj does), and runs the
    // constructor on it; the object is the result, so nothing is preserved after the call.
    [Fact]
    public void ConstructorAllocatesAndRootsTheObjectBeforeRunningTheConstructor()
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var constructor = TypedMethod(".ctor", CliTypeIdentity.FromStackKind(CliValueKind.Void), Type("Created"),
            isStatic: false, parameters: [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);
        var request = fixture.Request(RuntimeIntrinsic.MemberExecuteMethod, members: Members(constructor));

        Create(fixture).Emit(request, fixture.Writer);

        var code = fixture.Code;
        var allocation = AllocationCall(fixture, request, code);
        var call = code.IndexOf(Local(WasmOpcodes.Call, 71));
        var publication = Assert.Single(fixture.AllocationRootPublications);
        Assert.InRange(publication, allocation + 1, call - 1);
        Assert.Contains(constructor, fixture.Functions.Methods);
        Assert.Equal(Local(WasmOpcodes.LocalSet, request.Local(0, CliValueKind.ManagedReference)),
            code[publication + 1]);
        // A constructor has no receiver to check; the one null check is the int argument's.
        Assert.Single(fixture.Exceptions.Kinds, kind => kind == ManagedExceptionKind.NullReference);
        // The call is followed by the case's end, not by a result's preservation.
        Assert.Equal(WasmOpcodes.Else, code[call + 1].Opcode);
    }

    // A finalizable object is allocated through the finalizable allocator, which registers it for finalization.
    [Fact]
    public void AFinalizableObjectIsAllocatedForFinalization()
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var created = Type("Finalized");
        var constructor = TypedMethod(".ctor", CliTypeIdentity.FromStackKind(CliValueKind.Void), created, isStatic: false);
        var request = fixture.Request(RuntimeIntrinsic.MemberExecuteMethod, members: Members(constructor));
        var target = request.Instruction.Target with
        {
            FinalizableTypeIds = Lookups.Of<int>(new HashSet<int> { fixture.Layouts.GetObjectLayout(created).TypeId }),
        };
        request = request with { Call = request.Call with { Instruction = request.Instruction with { Target = target } } };

        Create(fixture).Emit(request, fixture.Writer);

        var finalizable = (uint)fixture.Imports.Resolve(RuntimeImportSymbol.AllocateFinalizable,
            target.RuntimeImportSelection);
        Assert.Contains(fixture.Code, instruction =>
            instruction.Opcode == WasmOpcodes.Call && instruction.Operand?.UnsignedValue == finalizable);
    }

    // A constructor that triggers its type's initializer runs that initializer before allocating.
    [Fact]
    public void AConstructorRunsItsTypesInitializerFirst()
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var constructor = TypedMethod(".ctor", CliTypeIdentity.FromStackKind(CliValueKind.Void), Type("Initialized"),
            isStatic: false);
        fixture.InitializationTriggers.Add(constructor.Definition);
        var request = fixture.Request(RuntimeIntrinsic.MemberExecuteMethod, members: Members(constructor));

        Create(fixture).Emit(request, fixture.Writer);

        var initialization = Assert.Single(fixture.Initialization.Requests);
        Assert.True(initialization.IsMethodInvocation);
        Assert.True(fixture.Initialization.InstructionCounts[0] < AllocationCall(fixture, request, fixture.Code));
    }

    // A constant or init-only field has no write case: writing it falls to the unsupported target.
    [Fact]
    public void ConstantAndInitOnlyFieldsHaveNoWriteCase()
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var fieldType = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        FieldInstanceModel Field(int row, string name, bool literal, bool initOnly) => new(
            new FieldDefinitionModel(new(EmitterTestSupport.Assembly, 0x04000030 + row), EmitterTestSupport.TypeKey, name,
                fieldType, false)
            { IsLiteral = literal, IsInitOnly = initOnly },
            Type("Owner"), fieldType);
        var constant = Field(0, "Constant", literal: true, initOnly: false);
        var initOnly = Field(1, "Fixed", literal: false, initOnly: true);
        var plan = Members() with
        {
            Fields = ImmutableDictionary<string, FieldInstanceModel>.Empty
                .Add(constant.CanonicalName, constant).Add(initOnly.CanonicalName, initOnly),
        };
        var request = fixture.Request(RuntimeIntrinsic.MemberWriteField, members: plan);

        Create(fixture).Emit(request, fixture.Writer);

        Assert.Equal([plan.UnsupportedTarget!], fixture.Functions.Methods);
        Assert.Empty(fixture.TypeValidator.Requests);
    }

    [Fact]
    public void StructResultGoesToARootedBoxTheCalleeWrites()
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var point = Type("Point", valueType: true);
        var method = TypedMethod("Make", point, Type("Owner"), isStatic: true);
        var request = fixture.Request(RuntimeIntrinsic.MemberExecuteMethod, members: Members(method));

        Create(fixture).Emit(request, fixture.Writer);

        var code = fixture.Code;
        var allocation = AllocationCall(fixture, request, code);
        var call = code.IndexOf(Local(WasmOpcodes.Call, 71));
        Assert.InRange(Assert.Single(fixture.AllocationRootPublications), allocation + 1, call - 1);
        Assert.Equal(WasmOpcodes.Else, code[call + 1].Opcode);
    }

    [Fact]
    public void VoidResultIsNull()
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var method = TypedMethod("Reset", CliTypeIdentity.FromStackKind(CliValueKind.Void), Type("Owner"),
            isStatic: true);
        var request = fixture.Request(RuntimeIntrinsic.MemberExecuteMethod, members: Members(method));

        Create(fixture).Emit(request, fixture.Writer);

        var code = fixture.Code;
        var call = code.IndexOf(Local(WasmOpcodes.Call, 71));
        Assert.Equal(WasmOpcodes.I32Constant, code[call + 1].Opcode);
        Assert.Equal(0, code[call + 1].Operand?.SignedValue);
        Assert.Equal(Local(WasmOpcodes.LocalSet, request.Local(0, CliValueKind.ManagedReference)), code[call + 2]);
        Assert.Empty(fixture.AllocationRootPublications);
    }

    // A struct's method runs on its box's contents after the box's type is checked.
    [Fact]
    public void StructReceiverIsCheckedAndPassedAsTheBoxContents()
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var point = Type("Point", valueType: true);
        var method = TypedMethod("Length", CliTypeIdentity.FromStackKind(CliValueKind.I4), point, isStatic: false);
        var request = fixture.Request(RuntimeIntrinsic.MemberExecuteMethod, members: Members(method));

        Create(fixture).Emit(request, fixture.Writer);

        var receiver = request.Local(1, CliValueKind.ManagedReference);
        Assert.Contains((receiver, point), fixture.TypeValidator.Requests);
        var code = fixture.Code;
        var call = code.IndexOf(Local(WasmOpcodes.Call, 71));
        var load = code.LastIndexOf(Local(WasmOpcodes.LocalGet, receiver), call);
        Assert.Equal(WasmOpcodes.I32Constant, code[load + 1].Opcode);
        Assert.Equal(WasmOpcodes.I32Add, code[load + 2].Opcode);
    }

    [Fact]
    public void StaticFieldReadsItsStorageAfterInitialization()
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var fieldType = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var field = new FieldInstanceModel(new FieldDefinitionModel(EmitterTestSupport.InstanceFieldKey,
            EmitterTestSupport.TypeKey, "Counter", fieldType, true), Type("Holder"), fieldType);
        var request = fixture.Request(RuntimeIntrinsic.MemberReadField, members: Members(field: field));

        Create(fixture).Emit(request, fixture.Writer);

        Assert.Single(fixture.Initialization.Requests);
        Assert.Contains(fixture.Code, instruction => instruction.Opcode == WasmOpcodes.I32Constant &&
            instruction.Operand?.SignedValue == ExpressionIntrinsicEmissionFixture.StaticFieldAddress);
        Assert.DoesNotContain(fixture.Exceptions.Kinds, kind => kind == ManagedExceptionKind.NullReference);
    }

    // A struct-valued field is copied into a new box; nothing runs after the allocation, so it needs no root.
    [Fact]
    public void StructValuedFieldIsCopiedIntoANewBox()
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var point = Type("Point", valueType: true);
        var field = new FieldInstanceModel(new FieldDefinitionModel(EmitterTestSupport.InstanceFieldKey,
            EmitterTestSupport.TypeKey, "Origin", point, false), Type("Shape"), point);
        var request = fixture.Request(RuntimeIntrinsic.MemberReadField, members: Members(field: field));

        Create(fixture).Emit(request, fixture.Writer);

        var code = fixture.Code;
        var allocation = AllocationCall(fixture, request, code);
        Assert.Equal(Local(WasmOpcodes.LocalSet, request.Local(0, CliValueKind.ManagedReference)), code[allocation + 1]);
        Assert.Empty(fixture.AllocationRootPublications);
        Assert.Contains(fixture.Code[allocation..], instruction =>
            instruction.Opcode is WasmOpcodes.I32Store or WasmOpcodes.Prefixed);
    }

    private static int AllocationCall(
        ExpressionIntrinsicEmissionFixture fixture,
        RuntimeIntrinsicEmissionRequest request,
        ImmutableArray<WasmInstruction> code)
    {
        var allocate = (uint)fixture.Imports.Resolve(RuntimeImportSymbol.Allocate,
            request.Instruction.Target.ObjectAllocation.Selection);
        return Assert.Single(Enumerable.Range(0, code.Length), index =>
            code[index].Opcode == WasmOpcodes.Call && code[index].Operand?.UnsignedValue == allocate);
    }

    private static MemberExecutionIntrinsicEmitter Create(ExpressionIntrinsicEmissionFixture fixture) =>
        new Parts(fixture).Emitter();

    // Member execution's collaborators over the fixture, as the compiler's services compose them.
    private sealed class Parts(ExpressionIntrinsicEmissionFixture fixture)
    {
        private readonly AddressInstructionEmitter _addresses = new(fixture.Layouts);

        public IEnumerable<(Type Type, object[] Arguments)> Constructions()
        {
            var checks = new MemberReferenceCheckEmitter(_addresses, fixture.Exceptions, fixture.TypeValidator);
            var payloads = new BoxPayloadAddressEmitter(fixture.Layouts, fixture.Scalars, _addresses);
            var locations = new MemberFieldLocationEmitter(fixture.Layouts, fixture.Scalars, _addresses, fixture.Scalars,
                fixture);
            var allocator = new MemberObjectAllocator(fixture.Layouts, _addresses, fixture.Imports, fixture.Exceptions,
                fixture.Roots);
            var results = new MemberResultBoxEmitter(fixture.Layouts, fixture.Scalars, fixture.Layouts, _addresses,
                fixture.Imports, fixture.Exceptions);
            var nullable = new MemberNullableResultEmitter(fixture);
            var cases = new MemberDescriptorCaseEmitter(_addresses);
            var arguments = new MemberArgumentEmitter(fixture.Layouts, fixture.Layouts, _addresses, fixture.Scalars, checks,
                payloads);
            var counts = new MemberArgumentCountCheckEmitter(_addresses, fixture.Layouts, fixture.Exceptions);
            yield return (typeof(MemberReferenceCheckEmitter), [_addresses, fixture.Exceptions, fixture.TypeValidator]);
            yield return (typeof(BoxPayloadAddressEmitter), [fixture.Layouts, fixture.Scalars, _addresses]);
            yield return (typeof(MemberFieldLocationEmitter),
                [fixture.Layouts, fixture.Scalars, _addresses, fixture.Scalars, fixture]);
            yield return (typeof(MemberObjectAllocator),
                [fixture.Layouts, _addresses, fixture.Imports, fixture.Exceptions, fixture.Roots]);
            yield return (typeof(MemberResultBoxEmitter),
                [fixture.Layouts, fixture.Scalars, fixture.Layouts, _addresses, fixture.Imports, fixture.Exceptions]);
            yield return (typeof(MemberNullableResultEmitter), [fixture]);
            yield return (typeof(MemberDescriptorCaseEmitter), [_addresses]);
            yield return (typeof(MemberArgumentEmitter),
                [fixture.Layouts, fixture.Layouts, _addresses, fixture.Scalars, checks, payloads]);
            yield return (typeof(MemberArgumentCountCheckEmitter), [_addresses, fixture.Layouts, fixture.Exceptions]);
            yield return (typeof(MemberExecutionPlanValidator), [fixture]);
            yield return (typeof(MemberMethodCaseEmitter),
            [
                fixture.Layouts, cases, counts, arguments, checks, allocator, payloads, results, nullable, fixture,
                fixture.Initialization, fixture, fixture, fixture.Layouts, fixture.Exceptions, _addresses,
            ]);
            yield return (typeof(MemberFieldReadCaseEmitter),
            [
                fixture.Layouts, cases, fixture.Initialization, checks, fixture, locations, nullable, allocator, payloads,
                results, fixture.Layouts, fixture.Scalars, _addresses,
            ]);
            yield return (typeof(MemberFieldWriteCaseEmitter),
            [
                fixture.Layouts, cases, fixture.Initialization, checks, fixture, locations, payloads, fixture.Layouts,
                fixture.Scalars, fixture.Layouts, _addresses, fixture.Imports, fixture.Exceptions,
            ]);
        }

        public MemberExecutionIntrinsicEmitter Emitter()
        {
            var built = Constructions().ToDictionary(item => item.Type,
                item => Assert.Single(item.Type.GetConstructors()).Invoke(item.Arguments));
            return new(
                (IMemberExecutionPlanValidator)built[typeof(MemberExecutionPlanValidator)],
                fixture.Roots,
                (IMemberMethodCaseEmitter)built[typeof(MemberMethodCaseEmitter)],
                (IMemberFieldReadCaseEmitter)built[typeof(MemberFieldReadCaseEmitter)],
                (IMemberFieldWriteCaseEmitter)built[typeof(MemberFieldWriteCaseEmitter)]);
        }
    }

    [Fact]
    public void TheEmitterRejectsAMissingCollaborator()
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var built = new Parts(fixture).Constructions().ToDictionary(item => item.Type,
            item => Assert.Single(item.Type.GetConstructors()).Invoke(item.Arguments));
        object[] arguments =
        [
            built[typeof(MemberExecutionPlanValidator)], fixture.Roots, built[typeof(MemberMethodCaseEmitter)],
            built[typeof(MemberFieldReadCaseEmitter)], built[typeof(MemberFieldWriteCaseEmitter)],
        ];
        var constructor = Assert.Single(typeof(MemberExecutionIntrinsicEmitter).GetConstructors());
        for (var index = 0; index < arguments.Length; index++)
        {
            var missing = (object?[])arguments.Clone();
            missing[index] = null;
            var error = Assert.Throws<TargetInvocationException>(() => constructor.Invoke(missing));
            Assert.Equal(constructor.GetParameters()[index].Name,
                Assert.IsType<ArgumentNullException>(error.InnerException).ParamName);
        }
    }
}
