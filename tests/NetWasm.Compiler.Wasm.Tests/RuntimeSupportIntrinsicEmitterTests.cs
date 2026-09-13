using NetWasm.Compiler.Core;
using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

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
            RuntimeIntrinsic.ComponentResourceHandleCreate)
        {
            stack[0] = CliValueKind.ManagedReference;
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
    [MemberData(nameof(ReferenceInspectionTypes))]
    public void ReferenceInspectionHandlesEveryTypeShape(CliTypeIdentity inspectedType)
    {
        var layouts = new RecordingLayoutProvider();
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.IsReferenceOrContainsReferences,
            [CliValueKind.I4],
            [inspectedType]);

        EmitThroughCapability(
            new IsReferenceOrContainsReferencesIntrinsicEmitter(layouts),
            request);

        Assert.NotEmpty(EmitterTestSupport.GetCodeBytes(request.Instruction));
    }

    public static TheoryData<CliTypeIdentity> ReferenceInspectionTypes => new()
    {
        CliTypeIdentity.Named(
            new AssemblyIdentity("Test"),
            "Test",
            "Reference",
            isValueType: false),
        CliTypeIdentity.ManagedByReference(
            CliTypeIdentity.FromStackKind(CliValueKind.I4)),
        CliTypeIdentity.Named(
            new AssemblyIdentity("Test"),
            "Test",
            "ContainsReferences",
            isValueType: true),
        CliTypeIdentity.FromStackKind(CliValueKind.I4),
    };

    private static IRuntimeIntrinsicEmitter CreateEmitter(
        RuntimeIntrinsic intrinsic,
        RecordingLayoutProvider layouts)
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var exceptions = new ImplicitExceptionEmitter(layouts, layouts, 7);
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
}
