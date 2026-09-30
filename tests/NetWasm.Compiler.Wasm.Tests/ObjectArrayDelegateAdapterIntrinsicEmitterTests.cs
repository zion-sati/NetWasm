using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;
using static NetWasm.Compiler.Wasm.Tests.ExpressionIntrinsicEmissionFixture;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ObjectArrayDelegateAdapterIntrinsicEmitterTests
{
    [Theory]
    [InlineData("layouts")]
    [InlineData("addresses")]
    [InlineData("types")]
    [InlineData("objects")]
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
            fixture.Request(RuntimeIntrinsic.ObjectArrayDelegateAdapterCreate), null!));
        Assert.Equal(0, fixture.RootCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EmitRejectsMissingOrContradictoryPlanBeforeOutput(int defect)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var factory = Method("Factory", defect == 3 ? CliValueKind.I4 : CliValueKind.ManagedReference,
            parameters: [defect == 2 ? CliValueKind.I4 : CliValueKind.ManagedReference]);
        var plan = new ObjectArrayDelegateAdapterPlan(
            defect == 1 ? Method("Other", CliValueKind.ManagedReference, Type("Other")) : factory,
            Type("Delegate"), Method("Invoke", CliValueKind.I4), Method("Adapter", CliValueKind.I4), true);
        var request = fixture.Request(RuntimeIntrinsic.ObjectArrayDelegateAdapterCreate,
            factory, defect == 0 ? null : plan);

        var error = Assert.Throws<CompilerException>(() => Create(fixture).Emit(request, fixture.Writer));

        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
        Assert.Empty(fixture.Code);
        Assert.Empty(fixture.Functions.Methods);
        Assert.Equal(0, fixture.RootCount);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void EmitPublishesRootsAndCreatesDelegateOrCallsRejectionTarget(
        bool memory64,
        bool supported,
        bool scalarFactoryResult)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(memory64);
        var factory = Method(
            "Factory",
            scalarFactoryResult
                ? CliValueKind.I4
                : CliValueKind.ManagedReference,
            parameters: [CliValueKind.ManagedReference]);
        var plan = new ObjectArrayDelegateAdapterPlan(factory, Type("Delegate"),
            Method("Invoke", CliValueKind.I4), Method("Adapter", CliValueKind.I4), supported);
        var request = fixture.Request(RuntimeIntrinsic.ObjectArrayDelegateAdapterCreate, factory, plan);

        Create(fixture).Emit(request, fixture.Writer);

        Assert.Equal(1, fixture.RootCount);
        Assert.Same(request.Instruction, fixture.PublishedRequest);
        Assert.Equal([plan.Target], fixture.Functions.Methods);
        if (!supported)
        {
            Assert.Equal<WasmInstruction>(
                [
                    Local(WasmOpcodes.Call, 71),
                    WasmInstruction.NoOperand(WasmOpcodes.Unreachable),
                ],
                fixture.Code);
            Assert.Empty(fixture.Exceptions.Kinds);
            Assert.Null(fixture.Layouts.ObjectIdentityRequest);
            return;
        }

        Assert.Equal(
            Local(
                WasmOpcodes.LocalSet,
                request.Local(0, CliValueKind.ManagedReference)),
            fixture.Code[^1]);
        Assert.Equal(plan.DelegateType, fixture.Layouts.ObjectIdentityRequest);
        Assert.Equal([ManagedExceptionKind.OutOfMemory], fixture.Exceptions.Kinds);
        Assert.Equal(memory64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant, fixture.Code[0].Opcode);
        Assert.Equal(16, memory64 ? fixture.Code[0].Operand!.Signed64Value : fixture.Code[0].Operand!.SignedValue);
        Assert.Equal(Local(WasmOpcodes.Call, fixture.Imports.Resolve(RuntimeImportSymbol.Allocate)), fixture.Code[2]);
        Assert.Equal(Local(WasmOpcodes.LocalGet, request.Instruction.Context.ObjectTemporary), fixture.Code[4]);
        Assert.Equal(memory64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero, fixture.Code[5].Opcode);
        Assert.Equal(WasmOpcodes.If, fixture.Code[6].Opcode);
        Assert.Equal(WasmOpcodes.Throw, fixture.Code[7].Opcode);
        Assert.Equal(WasmOpcodes.End, fixture.Code[8].Opcode);
        var targetStore = fixture.Code.Single(i => i.Operand?.Offset == fixture.Layouts.DelegateTargetOffset &&
            i.Opcode == (memory64 ? WasmOpcodes.I64Store : WasmOpcodes.I32Store));
        Assert.Equal((uint)(memory64 ? 3 : 2), targetStore.Operand!.Alignment);
        Assert.Equal(Local(WasmOpcodes.LocalGet, request.Local(0, CliValueKind.ManagedReference)), fixture.Code[10]);
        Assert.Equal(WasmOpcodes.I32Store, fixture.Code[14].Opcode);
        Assert.Equal((uint)fixture.Layouts.DelegateMethodIdOffset, fixture.Code[14].Operand!.Offset);
        Assert.Equal(71, fixture.Code[13].Operand!.SignedValue);
    }

    private static ObjectArrayDelegateAdapterIntrinsicEmitter Create(ExpressionIntrinsicEmissionFixture fixture, string? missing = null) => new(
        missing == "layouts" ? null! : fixture.Layouts,
        missing == "addresses" ? null! : new AddressInstructionEmitter(fixture.Layouts),
        missing == "types" ? null! : fixture.Layouts,
        missing == "objects" ? null! : fixture.Layouts,
        missing == "runtimeImports" ? null! : fixture.Imports,
        missing == "exceptions" ? null! : fixture.Exceptions,
        missing == "roots" ? null! : fixture.Roots);
}
