using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

internal sealed class ExpressionIntrinsicEmissionFixture : INullableTypeResolver,
    INullableBoxEmitter, IValueFrameAddressEmitter
{
    public ExpressionIntrinsicEmissionFixture(bool memory64)
    {
        Target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32;
        Layouts = new(Target);
        Scalars = new(memory64);
        Initialization = new(() => Code.Length);
    }

    public WasmTargetLayout Target { get; }
    public RecordingLayoutProvider Layouts { get; }
    public EmitterTestSupport.RecordingInstructionWriter Writer { get; } = new();
    public RecordingExceptions Exceptions { get; } = new();
    public RecordingFunctions Functions { get; } = new();
    public IRuntimeImportResolver Imports { get; } = WasmRuntimeImports.CreateCatalog();
    public ScalarLayouts Scalars { get; }
    public RecordingBoxedValueTypeValidator TypeValidator { get; } = new();
    public RecordingStaticInitializationEmitter Initialization { get; }
    public int RootCount { get; private set; }
    public InstructionEmissionRequest? PublishedRequest { get; private set; }
    public ImmutableArray<WasmInstruction> Code => Writer.ToInstructions();
    public Dictionary<CliTypeIdentity, CliTypeIdentity> NullableTypes { get; } = [];
    public List<(CliTypeIdentity Underlying, int Source, int Result, int CodeIndex)> NullableBoxes { get; } = [];
    public List<(MethodEmissionContext Context, int Offset, int CodeIndex)> ValueAddresses { get; } = [];

    public CliTypeIdentity? Resolve(CliTypeIdentity type) => NullableTypes.GetValueOrDefault(type);

    public void Emit(IWasmInstructionWriter code, CliTypeIdentity underlyingType,
        int sourceAddressLocal, int targetLocal) =>
        NullableBoxes.Add((underlyingType, sourceAddressLocal, targetLocal, Code.Length));

    public void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code,
        CliTypeIdentity underlyingType) => throw new NotSupportedException();

    public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, int offset)
    {
        ValueAddresses.Add((context, offset, Code.Length));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(99)));
    }

    public void Emit(IWasmInstructionWriter code, MethodEmissionContext context,
        FilterCapture capture) => throw new NotSupportedException();

    public IRootPublicationEmitter Roots => new RecordingRootPublicationEmitter((request, code, _) =>
    {
        Assert.Same(Writer, code);
        Assert.Empty(Code);
        PublishedRequest = request;
        RootCount++;
    });

    public RuntimeIntrinsicEmissionRequest Request(
        RuntimeIntrinsic intrinsic,
        MethodInstanceModel? factory = null,
        ObjectArrayDelegateAdapterPlan? adapter = null,
        MemberExecutionPlan? members = null)
    {
        var count = intrinsic switch
        {
            RuntimeIntrinsic.ObjectArrayDelegateAdapterCreate => 1,
            RuntimeIntrinsic.MemberExecuteMethod or
            RuntimeIntrinsic.DelegateDynamicInvoke => 3,
            _ => 2,
        };
        factory ??= Method("Factory", CliValueKind.ManagedReference,
            isStatic: true,
            parameters: Enumerable.Repeat(CliValueKind.ManagedReference, count).ToArray());
        var original = EmitterTestSupport.CreateInstructionRequest(CilOperation.Call,
            Enumerable.Repeat(CliValueKind.ManagedReference, count), maxStack: count);
        var instruction = original with
        {
            Target = original.Target with
            {
                ObjectArrayDelegateAdapters = adapter is null
                    ? ImmutableDictionary<string, ObjectArrayDelegateAdapterPlan>.Empty
                    : ImmutableDictionary<string, ObjectArrayDelegateAdapterPlan>.Empty.Add(factory.CanonicalName, adapter),
                MemberExecution = members ?? MemberExecutionPlan.Empty,
            },
        };
        return new(new CallEmissionRequest(instruction, factory, 0, count), intrinsic, null, Target, Functions);
    }

    public static CliTypeIdentity Type(string name = "Receiver", bool valueType = false) =>
        CliTypeIdentity.Named(EmitterTestSupport.Assembly, "Tests", name, valueType);

    public static MethodInstanceModel Method(
        string name,
        CliValueKind result,
        CliTypeIdentity? owner = null,
        bool isStatic = false,
        bool isVirtual = false,
        params CliValueKind[] parameters)
    {
        var signature = MethodSignatureModel.Create(result, parameters);
        var definition = new MethodDefinitionModel(EmitterTestSupport.EntryKey,
            EmitterTestSupport.TypeKey, name, isStatic, signature, 1)
        {
            IsVirtual = isVirtual,
        };
        return new(definition, owner ?? Type(), [], signature);
    }

    public static MemberExecutionPlan Members(MethodInstanceModel? method = null,
        bool dispatch = false, ImmutableArray<DispatchTargetModel> targets = default,
        FieldInstanceModel? field = null) => new(
            method is null ? ImmutableDictionary<string, MemberMethodExecutionPlan>.Empty
                : ImmutableDictionary<string, MemberMethodExecutionPlan>.Empty.Add(method.CanonicalName,
                    new(method, dispatch, targets.IsDefault ? [] : targets)),
            field is null ? ImmutableDictionary<string, FieldInstanceModel>.Empty
                : ImmutableDictionary<string, FieldInstanceModel>.Empty.Add(field.CanonicalName, field),
            Method(
                "Unsupported",
                CliValueKind.ManagedReference,
                isStatic: true));

    public static WasmInstruction Local(byte opcode, int local) =>
        WasmInstruction.WithOperand(opcode, WasmInstructionOperand.Unsigned((uint)local));

    public sealed class RecordingExceptions : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];
        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind)
        {
            Kinds.Add(kind);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Throw,
                WasmInstructionOperand.Unsigned((uint)kind)));
        }
    }

    public sealed class RecordingFunctions : IFunctionIndexResolver
    {
        public List<MethodInstanceModel> Methods { get; } = [];
        public int Resolve(EntityKey method) => throw new NotSupportedException();
        public int Resolve(string method) => throw new NotSupportedException();
        public int Resolve(MethodInstanceModel method)
        {
            Methods.Add(method);
            return method.Definition.Name == "Unsupported" ? 72 : 71;
        }
    }

    public sealed class RecordingBoxedValueTypeValidator : IBoxedValueTypeValidator
    {
        public List<(int ObjectLocal, CliTypeIdentity Type)> Requests { get; } = [];

        public void Validate(
            IWasmInstructionWriter code,
            int objectLocal,
            CliTypeIdentity targetType,
            ManagedExceptionKind mismatchException = ManagedExceptionKind.InvalidCast) =>
            Requests.Add((objectLocal, targetType));
    }

    public sealed class RecordingStaticInitializationEmitter(
        Func<int> instructionCount) : IStaticInitializationEmitter
    {
        public List<StaticInitializationEmissionRequest> Requests { get; } = [];
        public List<int> InstructionCounts { get; } = [];
        public List<IFunctionIndexResolver> FunctionResolvers { get; } = [];

        public void Emit(
            StaticInitializationEmissionRequest request,
            IWasmInstructionWriter code,
            IFunctionIndexResolver functionIndices)
        {
            Requests.Add(request);
            InstructionCounts.Add(instructionCount());
            FunctionResolvers.Add(functionIndices);
        }
    }

    public sealed class ScalarLayouts(bool memory64) : IValueLayoutProvider, IInstanceFieldLayoutProvider
    {
        public ValueLayout GetValueLayout(CliTypeIdentity type)
        {
            var size = type.CanonicalName switch
            {
                "primitive:bool" or "primitive:i1" or "primitive:u1" => 1,
                "primitive:char" or "primitive:i2" or "primitive:u2" => 2,
                "primitive:i8" or "primitive:u8" or "primitive:f8" => 8,
                _ when type.StackKind == CliValueKind.ManagedReference && memory64 => 8,
                _ => 4,
            };
            return new(type, size, size, []);
        }

        public FieldLayout GetFieldLayout(FieldInstanceModel field) =>
            new(52) { Size = GetValueLayout(field.FieldType).Size, Type = field.FieldType };
        public FieldLayout GetFieldLayout(EntityKey field) => throw new NotSupportedException();
    }
}
