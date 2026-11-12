using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

// Issue 226: each unmanaged calli signature is lowered as a DllImport's, and each physical C ABI
// signature gets one trampoline import.
public sealed class NativeCallSitePlannerTests
{
    private static readonly CliTypeIdentity Int = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity UnsignedInt = CliTypeIdentity.Primitive("u4", CliValueKind.I4);
    private static readonly CliTypeIdentity Long = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
    private static readonly CliTypeIdentity Double = CliTypeIdentity.Primitive("f8", CliValueKind.F8);
    private static readonly CliTypeIdentity Single = CliTypeIdentity.Primitive("f4", CliValueKind.F4);
    private static readonly CliTypeIdentity Pointer = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
    private static readonly CliTypeIdentity Void = CliTypeIdentity.FromStackKind(CliValueKind.Void);

    [Fact]
    public void PlansOneTrampolinePerPhysicalSignatureNumberedFromTheFirstIndex()
    {
        var planner = Assert.IsAssignableFrom<INativeCallSitePlanner>(NativeAbiTestSupport.CallSitePlanner());
        var signedTwice = MethodSignatureModel.Create(Int, Int);
        var unsignedTwice = MethodSignatureModel.Create(UnsignedInt, UnsignedInt);
        var mix = MethodSignatureModel.Create(Double, Int, Double, Long, Single, Pointer);
        var mark = MethodSignatureModel.Create(Void, Int);

        var plan = planner.Plan(
        [
            new(mix, "B", 4),
            new(signedTwice, "A", 1),
            new(unsignedTwice, "A", 9),
            new(mark, "C", 2),
            new(signedTwice with { }, "D", 0),
        ], 17);

        Assert.Equal(
            [("netwasm_calli_didlfp", 17), ("netwasm_calli_ii", 18), ("netwasm_calli_vi", 19)],
            plan.Trampolines.Select(trampoline => (trampoline.Symbol, trampoline.Index.Value)));
        Assert.Equal([CliValueKind.F8, CliValueKind.I4, CliValueKind.F8, CliValueKind.I8, CliValueKind.F4,
                CliValueKind.NativeInt, CliValueKind.NativeInt],
            [plan.Trampolines[0].Type.Result, .. plan.Trampolines[0].Type.Parameters]);
        Assert.Equal(4, plan.BySignature.Count);
        // int(int) and uint(uint) lower differently but share their physical trampoline.
        Assert.Same(plan.For(signedTwice).Trampoline, plan.For(unsignedTwice).Trampoline);
        Assert.Equal(signedTwice.ReturnSignatureType, plan.For(signedTwice).Lowering.LogicalSignature.ReturnSignatureType);
        Assert.Same(plan.Trampolines[2], plan.For(mark).Trampoline);
    }

    [Fact]
    public void AProgramWithoutUnmanagedCallsPlansNothing()
    {
        var plan = NativeAbiTestSupport.CallSitePlanner().Plan([], 3);

        Assert.Same(NativeCallSitePlan.Empty, plan);
        var missing = Assert.Throws<CompilerException>(() => plan.For(MethodSignatureModel.Create(Int)));
        Assert.Equal(DiagnosticCode.CompilerInvariant, missing.Diagnostic.Code);
    }

    [Fact]
    public void KeysSignaturesByTheirTypesNotTheirArrays()
    {
        Assert.Equal(
            NativeCallSitePlan.Key(MethodSignatureModel.Create(Int, Long)),
            NativeCallSitePlan.Key(MethodSignatureModel.Create(Int, Long) with { }));
        Assert.NotEqual(
            NativeCallSitePlan.Key(MethodSignatureModel.Create(Int, Long)),
            NativeCallSitePlan.Key(MethodSignatureModel.Create(Long, Int)));
        Assert.Throws<ArgumentNullException>(() => NativeCallSitePlan.Key(null!));
    }

    // An unsupported parameter is reported at the first use of its signature, at its IL offset.
    [Fact]
    public void NamesTheFirstUseOfAnUnsupportedSignature()
    {
        var aggregate = CliTypeIdentity.Named(new AssemblyIdentity("App"), "App", "Pair", isValueType: true);
        var signature = MethodSignatureModel.Create(Int, aggregate);

        var error = Assert.Throws<CompilerException>(() => NativeAbiTestSupport.CallSitePlanner().Plan(
            [new(signature, "Later", 1), new(signature, "Earlier", 12)], 0));

        Assert.Equal(DiagnosticCode.NativeInterop, error.Diagnostic.Code);
        Assert.Equal(("Earlier", 12), (error.Diagnostic.Method, error.Diagnostic.IlOffset));
        var located = new NativeCallSitePlanner(new ThrowingSignatures(new(DiagnosticCode.NativeInterop, "x", "M", 5)));
        Assert.Equal(5, Assert.Throws<CompilerException>(() =>
            located.Plan([new(signature, "Other", 9)], 0)).Diagnostic.IlOffset);
    }

    [Fact]
    public void RejectsAPhysicalValueTheCAbiCannotCarry()
    {
        var planner = new NativeCallSitePlanner(new FixedSignatures(MethodSignatureModel.Create(CliValueKind.V128)));

        var error = Assert.Throws<CompilerException>(() =>
            planner.Plan([new(MethodSignatureModel.Create(Int), "M", 0)], 0));

        Assert.Equal(DiagnosticCode.CompilerInvariant, error.Diagnostic.Code);
    }

    [Fact]
    public void RejectsMissingDependenciesAndInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new NativeCallSitePlanner(null!));
        var planner = NativeAbiTestSupport.CallSitePlanner();
        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => planner.Plan([], -1));
    }

    private sealed class ThrowingSignatures(CompilerDiagnostic diagnostic) : INativeAbiSignaturePlanner
    {
        public NativeAbiSignaturePlan Plan(MethodSignatureModel signature, NativeAbiSignatureKind kind,
            string methodName) => throw new CompilerException(diagnostic);
    }

    private sealed class FixedSignatures(MethodSignatureModel physical) : INativeAbiSignaturePlanner
    {
        public NativeAbiSignaturePlan Plan(MethodSignatureModel signature, NativeAbiSignatureKind kind,
            string methodName) => new(signature, physical, [], new(NativeAbiValueKind.Void, signature.ReturnSignatureType, null), null);
    }
}
