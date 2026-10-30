using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class WasmLocalLayoutPlannerTests
{
    [Fact]
    public void EvaluationStackUsesOneLocalBankPerPhysicalValueKind()
    {
        var layout = WasmLocalLayoutPlanner.CreateEvaluationStack(7, 3);

        Assert.Equal(7, layout.I4Base);
        Assert.Equal(10, layout.I8Base);
        Assert.Equal(13, layout.F4Base);
        Assert.Equal(16, layout.F8Base);
        Assert.Equal(19, layout.V128Base);
        Assert.Equal(22, layout.End);
        Assert.Equal(8, layout.GetLocal(1, CliValueKind.I4));
        Assert.Equal(17, layout.GetLocal(1, CliValueKind.F8));
        Assert.Equal(20, layout.GetLocal(1, CliValueKind.V128));
    }

    [Fact]
    public void TargetWidthSelectsThePhysicalReferenceAndNativeIntegerBank()
    {
        var layout = WasmLocalLayoutPlanner.CreateEvaluationStack(5, 2);

        Assert.Equal(6, WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            layout, 1, CliValueKind.ManagedReference, WasmTargetLayout.Wasm32));
        Assert.Equal(6, WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            layout, 1, CliValueKind.NativeInt, WasmTargetLayout.Wasm32));
        // Every pointer-sized kind shares the slot's i64 local on Wasm64, as it
        // shares the i32 local on Wasm32.
        Assert.Equal(8, WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            layout, 1, CliValueKind.ManagedReference, WasmTargetLayout.Wasm64));
        Assert.Equal(8, WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            layout, 1, CliValueKind.NativeInt, WasmTargetLayout.Wasm64));
        Assert.Equal(8, WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            layout, 1, CliValueKind.ValueType, WasmTargetLayout.Wasm64));
    }

    [Fact]
    public void MethodLocalsSpillAddressTakenScalarsAndReserveExceptionState()
    {
        var body = CreateBody(
            2,
            CliValueKind.I4,
            CliValueKind.ValueType,
            CliValueKind.ManagedReference);

        var locals = WasmLocalLayoutPlanner.CreateMethodLocals(
            new StructuredMethodHeader(
                body.Method,
                body.MethodInstance,
                body.MaxStack,
                body.Locals,
                body.LocalSignatureTypes,
                body.Instructions),
            2,
            ImmutableHashSet.Create(0));

        Assert.Equal(30, locals.Length);
        Assert.Equal(
            [
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedReference,
                CliValueKind.I4, CliValueKind.I4,
                CliValueKind.I8, CliValueKind.I8,
                CliValueKind.F4, CliValueKind.F4,
                CliValueKind.F8, CliValueKind.F8,
                CliValueKind.V128, CliValueKind.V128,
            ],
            locals.Take(13));
        Assert.Equal(
            [
                CliValueKind.ManagedReference,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedReference,
                CliValueKind.I4, CliValueKind.I4, CliValueKind.I4, CliValueKind.I4,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress,
                .. WasmLocalLayoutPlanner.TemporaryLocals,
            ],
            locals.Skip(13));
    }

    [Fact]
    public void FilterLocalsUseAddressesOnlyForValueTypeLocals()
    {
        var body = CreateBody(
            1,
            CliValueKind.I4,
            CliValueKind.ValueType,
            CliValueKind.ManagedReference);

        var locals = WasmLocalLayoutPlanner.CreateFilterLocals(new StructuredMethodHeader(
            body.Method,
            body.MethodInstance,
            body.MaxStack,
            body.Locals,
            body.LocalSignatureTypes,
            body.Instructions));

        Assert.Equal(CliValueKind.I4, locals[0]);
        Assert.Equal(CliValueKind.ManagedAddress, locals[1]);
        Assert.Equal(CliValueKind.ManagedReference, locals[2]);
        Assert.Equal(20, locals.Length);
        Assert.Equal(WasmLocalLayoutPlanner.TemporaryLocals, locals.TakeLast(8));
    }

    [Fact]
    public void InvalidLayoutRequestsFailBeforeEmission()
    {
        var layout = WasmLocalLayoutPlanner.CreateEvaluationStack(0, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WasmLocalLayoutPlanner.CreateEvaluationStack(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WasmLocalLayoutPlanner.CreateEvaluationStack(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            layout.GetLocal(1, CliValueKind.I4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            layout.GetLocal(0, CliValueKind.Void));
        Assert.Throws<OverflowException>(() =>
            WasmLocalLayoutPlanner.CreateEvaluationStack(int.MaxValue, 1));
    }

    private static CilMethodBody CreateBody(
        int maxStack,
        params CliValueKind[] locals)
    {
        var assembly = new AssemblyIdentity("LayoutTests");
        var type = new EntityKey(assembly, 0x02000001);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 0x06000001),
            type,
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        return new CilMethodBody(method, maxStack, [.. locals], [])
        {
            LocalSignatureTypes = [.. locals.Select(CliTypeIdentity.FromStackKind)],
        };
    }
}
