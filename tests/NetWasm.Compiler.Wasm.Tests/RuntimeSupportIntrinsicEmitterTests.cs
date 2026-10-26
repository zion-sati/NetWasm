using NetWasm.Compiler.Core;
using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class RuntimeSupportIntrinsicEmitterTests
{
    [Theory]
    [InlineData(RuntimeIntrinsic.GcCollect, 0)]
    [InlineData(RuntimeIntrinsic.SuppressFinalize, 1)]
    [InlineData(RuntimeIntrinsic.ReRegisterForFinalize, 1)]
    [InlineData(RuntimeIntrinsic.ReportUnobservedTaskException, 0)]
    [InlineData(RuntimeIntrinsic.IsReferenceOrContainsReferences, 1)]
    [InlineData(RuntimeIntrinsic.GetArrayDataReference, 1)]
    [InlineData(RuntimeIntrinsic.StringGetRawDataReference, 1)]
    [InlineData(RuntimeIntrinsic.StringEqualsOrdinal, 2)]
    [InlineData(RuntimeIntrinsic.NativeIntegerSize, 0)]
    [InlineData(RuntimeIntrinsic.ComponentReallocate, 4)]
    [InlineData(RuntimeIntrinsic.ComponentFree, 1)]
    [InlineData(RuntimeIntrinsic.ComponentResourceHandleCreate, 1)]
    [InlineData(RuntimeIntrinsic.ComponentResourceHandleGet, 1)]
    [InlineData(RuntimeIntrinsic.ComponentResourceHandleRelease, 1)]
    public void EmitsEveryRuntimeSupportIntrinsicThroughItsCapability(
        RuntimeIntrinsic intrinsic,
        int argumentCount)
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = CreateEmitter(intrinsic, layouts);
        var stack = Enumerable.Repeat(CliValueKind.I4, argumentCount).ToArray();
        if (intrinsic is RuntimeIntrinsic.SuppressFinalize or
            RuntimeIntrinsic.ReRegisterForFinalize or
            RuntimeIntrinsic.GetArrayDataReference or
            RuntimeIntrinsic.StringGetRawDataReference or
            RuntimeIntrinsic.StringEqualsOrdinal or
            RuntimeIntrinsic.ComponentResourceHandleCreate)
        {
            Array.Fill(stack, CliValueKind.ManagedReference);
        }
        ImmutableArray<CliTypeIdentity> methodArguments = intrinsic == RuntimeIntrinsic.IsReferenceOrContainsReferences
            ? [CliTypeIdentity.FromStackKind(CliValueKind.I4)]
            : [];
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            intrinsic,
            stack,
            methodArguments);

        EmitThroughCapability(emitter, request);

        Assert.NotEmpty(EmitterTestSupport.GetCodeBytes(request.Instruction));
    }

    [Fact]
    public void ReferenceInspectionRequiresOneClosedTypeArgument()
    {
        var layouts = new RecordingLayoutProvider();
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.IsReferenceOrContainsReferences,
            [CliValueKind.I4]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EmitThroughCapability(
                new IsReferenceOrContainsReferencesIntrinsicEmitter(layouts),
                request));

        Assert.Contains("one closed type argument", exception.Message);
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.SuppressFinalize)]
    [InlineData(RuntimeIntrinsic.ReRegisterForFinalize)]
    public void ObjectFinalizationApisEmitNothingWhenFinalizationIsAbsent(
        RuntimeIntrinsic intrinsic)
    {
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            intrinsic,
            [CliValueKind.ManagedReference]);
        request = request with
        {
            Call = request.Call with
            {
                Instruction = request.Instruction with
                {
                    Target = request.Instruction.Target with
                    {
                        RuntimeImportSelection = request.Instruction.Target
                            .RuntimeImportSelection with
                        {
                            IncludeFinalization = false,
                        },
                    },
                },
            },
        };

        var writer = new EmitterTestSupport.RecordingInstructionWriter();
        EmitterTestSupport.RegisterInstructionWriter(request.Instruction, writer);
        CreateEmitter(intrinsic, new RecordingLayoutProvider()).Emit(request, writer);

        Assert.Empty(writer.ToInstructions());
    }

    [Theory]
    [MemberData(nameof(ReferenceInspectionTypes))]
    public void ReferenceInspectionHandlesEveryTypeShape(
        CliTypeIdentity inspectedType,
        int expected)
    {
        var layouts = new RecordingLayoutProvider();
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.IsReferenceOrContainsReferences,
            [CliValueKind.I4],
            [inspectedType]);
        var writer = Assert.IsType<EmitterTestSupport.RecordingInstructionWriter>(
            EmitterTestSupport.GetCodeWriter(request.Instruction));

        EmitThroughCapability(
            new IsReferenceOrContainsReferencesIntrinsicEmitter(layouts),
            request);

        var instruction = Assert.Single(writer.ToInstructions(), candidate =>
            candidate.Opcode == WasmOpcodes.I32Constant);
        Assert.Equal(expected, instruction.Operand.SignedValue);
    }

    [Fact]
    public void ReferenceInspectionIncludesManagedReferenceFields()
    {
        var inspectedType = CliTypeIdentity.Named(
            new AssemblyIdentity("Test"),
            "Test",
            "ContainsManagedReference",
            isValueType: true);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.IsReferenceOrContainsReferences,
            [CliValueKind.I4],
            [inspectedType]);
        var writer = Assert.IsType<EmitterTestSupport.RecordingInstructionWriter>(
            EmitterTestSupport.GetCodeWriter(request.Instruction));

        EmitThroughCapability(
            new IsReferenceOrContainsReferencesIntrinsicEmitter(
                new ManagedReferenceLayoutProvider()),
            request);

        var instruction = Assert.Single(writer.ToInstructions(), candidate =>
            candidate.Opcode == WasmOpcodes.I32Constant);
        Assert.Equal(1, instruction.Operand.SignedValue);
    }

    [Fact]
    public void ReferenceInspectionIncludesManagedByReferenceFields()
    {
        var inspectedType = CliTypeIdentity.Named(
            new AssemblyIdentity("Test"),
            "Test",
            "ContainsManagedByReference",
            isValueType: true);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.IsReferenceOrContainsReferences,
            [CliValueKind.I4],
            [inspectedType]);
        var writer = Assert.IsType<EmitterTestSupport.RecordingInstructionWriter>(
            EmitterTestSupport.GetCodeWriter(request.Instruction));

        EmitThroughCapability(
            new IsReferenceOrContainsReferencesIntrinsicEmitter(
                new ManagedByReferenceLayoutProvider()),
            request);

        var instruction = Assert.Single(writer.ToInstructions(), candidate =>
            candidate.Opcode == WasmOpcodes.I32Constant);
        Assert.Equal(1, instruction.Operand.SignedValue);
    }

    public static TheoryData<CliTypeIdentity, int> ReferenceInspectionTypes => new()
    {
        {
            CliTypeIdentity.Named(
                new AssemblyIdentity("Test"),
                "Test",
                "Reference",
                isValueType: false),
            1
        },
        {
            CliTypeIdentity.ManagedByReference(
                CliTypeIdentity.FromStackKind(CliValueKind.I4)),
            1
        },
        {
            CliTypeIdentity.Named(
                new AssemblyIdentity("Test"),
                "Test",
                "ValueWithoutReferences",
                isValueType: true),
            0
        },
        { CliTypeIdentity.FromStackKind(CliValueKind.I4), 0 },
    };

    private static IRuntimeIntrinsicEmitter CreateEmitter(
        RuntimeIntrinsic intrinsic,
        RecordingLayoutProvider layouts)
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var exceptions = new ImplicitExceptionEmitter(layouts, layouts);
        var addresses = EmitterTestSupport.CreateAddressInstructions(layouts);
        return intrinsic switch
        {
            RuntimeIntrinsic.GcCollect => new GcCollectIntrinsicEmitter(imports),
            RuntimeIntrinsic.SuppressFinalize => new SuppressFinalizeIntrinsicEmitter(imports),
            RuntimeIntrinsic.ReRegisterForFinalize =>
                new ReRegisterForFinalizeIntrinsicEmitter(imports),
            RuntimeIntrinsic.ReportUnobservedTaskException =>
                new ReportUnobservedTaskExceptionIntrinsicEmitter(imports),
            RuntimeIntrinsic.IsReferenceOrContainsReferences =>
                new IsReferenceOrContainsReferencesIntrinsicEmitter(layouts),
            RuntimeIntrinsic.GetArrayDataReference => new GetArrayDataReferenceIntrinsicEmitter(
                layouts, layouts, exceptions, addresses),
            RuntimeIntrinsic.StringGetRawDataReference =>
                new StringGetRawDataReferenceIntrinsicEmitter(
                    layouts,
                    addresses),
            RuntimeIntrinsic.StringEqualsOrdinal =>
                new StringEqualsOrdinalIntrinsicEmitter(
                    layouts,
                    addresses),
            RuntimeIntrinsic.NativeIntegerSize => new NativeIntegerSizeIntrinsicEmitter(layouts),
            RuntimeIntrinsic.ComponentReallocate =>
                new ComponentReallocateIntrinsicEmitter(imports),
            RuntimeIntrinsic.ComponentFree => new ComponentFreeIntrinsicEmitter(imports),
            RuntimeIntrinsic.ComponentResourceHandleCreate =>
                new ComponentResourceHandleCreateIntrinsicEmitter(imports),
            RuntimeIntrinsic.ComponentResourceHandleGet =>
                new ComponentResourceHandleGetIntrinsicEmitter(imports),
            RuntimeIntrinsic.ComponentResourceHandleRelease =>
                new ComponentResourceHandleReleaseIntrinsicEmitter(imports),
            _ => throw new ArgumentOutOfRangeException(nameof(intrinsic)),
        };

    }

    private static void EmitThroughCapability(
        IRuntimeIntrinsicEmitter emitter,
        RuntimeIntrinsicEmissionRequest request) => emitter.Emit(
            request,
            EmitterTestSupport.GetCodeWriter(request.Instruction));

    private sealed class ManagedByReferenceLayoutProvider : IValueLayoutProvider
    {
        public ValueLayout GetValueLayout(CliTypeIdentity type) =>
            new(type, 4, 4, []) { ByReferenceOffsets = [0] };
    }

    private sealed class ManagedReferenceLayoutProvider : IValueLayoutProvider
    {
        public ValueLayout GetValueLayout(CliTypeIdentity type) =>
            new(type, 4, 4, [0]);
    }
}
