using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class TypeTestInstructionEmitterTests
{
    [Fact]
    public void CastClassEmitsManagedInvalidCastPath()
    {
        var request = CreateRequest(CilOperation.CastClass);

        Emit(AsProvider(CreateEmitter()), request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.Throw, GetCodeBytes(request));
    }

    [Fact]
    public void IsInstanceWritesNullOnFailureWithoutThrowing()
    {
        var request = CreateRequest(CilOperation.IsInstance);

        Emit(AsProvider(CreateEmitter()), request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.DoesNotContain(WasmOpcodes.Throw, GetCodeBytes(request));
    }

    [Fact]
    public void RuntimeTypeOperandUsesResolvedTypeIdentity()
    {
        var request = CreateRequest(
            CilOperation.CastClass,
            new CilOperand.TypeIdentity(ValueType()));

        AsTypeTest(CreateEmitter()).Test(
            request,
            GetCodeWriter(request),
            returnNullOnFailure: false);

        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
    }

    [Fact]
    public void KnownCallerWithoutPlannedSiteFallsBackToRuntimeTest()
    {
        var caller = CreateCaller();
        var request = WithCaller(
            CreateRequest(
                CilOperation.CastClass,
                new CilOperand.TypeIdentity(ValueType())),
            caller);

        AsTypeTest(CreateEmitter()).Test(
            request,
            GetCodeWriter(request),
            returnNullOnFailure: false);

        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.Throw, GetCodeBytes(request));
    }

    [Fact]
    public void PlannedSiteWithNoMatchingTypesThrowsOnFailure()
    {
        var caller = CreateCaller();
        var request = WithSite(
            CreateRequest(CilOperation.CastClass),
            caller,
            []);

        AsTypeTest(CreateEmitter()).Test(
            request,
            GetCodeWriter(request),
            returnNullOnFailure: false);

        Assert.Contains(WasmOpcodes.Throw, GetCodeBytes(request));
    }

    [Fact]
    public void PlannedSiteReturnsNullForOneMatchingType()
    {
        var caller = CreateCaller();
        var request = WithSite(
            CreateRequest(CilOperation.IsInstance),
            caller,
            [ValueType()]);

        AsTypeTest(CreateEmitter()).Test(
            request,
            GetCodeWriter(request),
            returnNullOnFailure: true);

        Assert.Contains(WasmOpcodes.I32Constant, GetCodeBytes(request));
        Assert.DoesNotContain(WasmOpcodes.Throw, GetCodeBytes(request));
    }

    [Fact]
    public void PlannedSiteCombinesMultipleMatchingTypes()
    {
        var caller = CreateCaller();
        var request = WithSite(
            CreateRequest(CilOperation.CastClass),
            caller,
            [ValueType(), ReferenceType()]);

        AsTypeTest(CreateEmitter()).Test(
            request,
            GetCodeWriter(request),
            returnNullOnFailure: false);

        Assert.Contains(WasmOpcodes.I32Or, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.Throw, GetCodeBytes(request));
    }

    private static TypeTestInstructionEmitter CreateEmitter()
    {
        var layouts = new RecordingLayoutProvider();
        return new TypeTestInstructionEmitter(layouts, new AddressInstructionEmitter(layouts), layouts, CreateTypeOperands(new FakeProgram()),
            WasmRuntimeImports.CreateCatalog(),
            new ImplicitExceptionEmitter(layouts, layouts, 7));
    }

    private static ITypeTestEmitter AsTypeTest(TypeTestInstructionEmitter emitter) =>
        new[] { emitter }.Cast<ITypeTestEmitter>().Single();

    private static IInstructionCommandProvider AsProvider(
        TypeTestInstructionEmitter emitter) =>
        new[] { emitter }.Cast<IInstructionCommandProvider>().Single();

    private static void Emit(
        IInstructionCommandProvider provider,
        InstructionEmissionRequest request)
    {
        var command = provider.Commands.Single(candidate =>
            candidate.Operation == request.Instruction.Operation);
        command.Emit(
            request,
            GetCodeWriter(request),
            CreateFunctionIndexResolver());
    }

    private static InstructionEmissionRequest CreateRequest(
        CilOperation operation,
        CilOperand? operand = null) => CreateInstructionRequest(
            operation,
            [CliValueKind.ManagedReference],
            operand ?? new CilOperand.Entity(TypeKey));

    private static MethodInstanceModel CreateCaller()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey);
        return new(
            method,
            ReferenceType(),
            [],
            method.Signature);
    }

    private static InstructionEmissionRequest WithCaller(
        InstructionEmissionRequest request,
        MethodInstanceModel caller)
    {
        var updated = request with
        {
            Header = request.Header with { MethodInstance = caller },
        };
        RegisterInstructionWriter(updated, new RecordingInstructionWriter());
        return updated;
    }

    private static InstructionEmissionRequest WithSite(
        InstructionEmissionRequest request,
        MethodInstanceModel caller,
        ImmutableArray<CliTypeIdentity> matchingTypes)
    {
        var site = new TypeTestSiteModel(
            caller.CanonicalName,
            request.Instruction.Offset,
            ValueType(),
            matchingTypes);
        var updated = request with
        {
            Header = request.Header with { MethodInstance = caller },
            Target = request.Target with
            {
                TypeTestSites = new Dictionary<string, TypeTestSiteModel>
                {
                    [site.Key] = site,
                },
            },
        };
        RegisterInstructionWriter(updated, new RecordingInstructionWriter());
        return updated;
    }

    private static CliTypeIdentity ValueType() =>
        CliTypeIdentity.Named(Assembly, "Test", "Value", isValueType: true);

    private static CliTypeIdentity ReferenceType() =>
        CliTypeIdentity.Named(Assembly, "Test", "Reference", isValueType: false);
}
