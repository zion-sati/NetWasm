// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

// A defect met while lowering or emitting one method is reported against that method (NW1011), not as a bare message
// the command line takes for a host failure (issue #259).
public sealed class MethodAttributionTests
{
    private static readonly AssemblyIdentity Assembly = new("MethodAttributionTests");
    private static readonly MethodDefinitionModel Definition = new(
        new EntityKey(Assembly, 0x0600062b),
        new EntityKey(Assembly, 0x02000010),
        "RunCore",
        false,
        MethodSignatureModel.Create(CliValueKind.Void),
        0);

    [Fact]
    public void TheDefectNamesTheMethodItsPackageKeyAndItsCause()
    {
        var cause = new InvalidOperationException("Sequence contains more than one element");
        var type = CliTypeIdentity.Named(Assembly, "Sdcb.Backends", "GpuDetGraph", isValueType: false);

        var defect = Assert.IsAssignableFrom<IMethodDefectExceptionFactory>(new MethodDefectExceptionFactory())
            .Create(Definition, type, "lowering", cause);

        Assert.Equal(DiagnosticCode.CompilerInvariant, defect.Diagnostic.Code);
        Assert.Equal("Sdcb.Backends.GpuDetGraph.RunCore", defect.Diagnostic.Method);
        Assert.Equal(Definition.Key, defect.Diagnostic.Context!.Source);
        Assert.Same(cause, defect.InnerException);
        Assert.Equal(
            "NW1011: Sdcb.Backends.GpuDetGraph.RunCore: lowering the method failed with " +
            "System.InvalidOperationException: Sequence contains more than one element",
            defect.Diagnostic.ToString());
    }

    [Fact]
    public void WithoutItsDeclaringTypeTheDefectNamesTheTypeByKey()
    {
        var defect = new MethodDefectExceptionFactory().Create(Definition, null, "emitting", new InvalidCastException());

        Assert.Equal($"{Definition.DeclaringType}.RunCore", defect.Diagnostic.Method);
    }

    [Fact]
    public void TheDefectFactoryRejectsMissingInputs()
    {
        var factory = new MethodDefectExceptionFactory();
        var cause = new InvalidOperationException();

        Assert.Throws<ArgumentNullException>(() => factory.Create(null!, null, "lowering", cause));
        Assert.Throws<ArgumentNullException>(() => factory.Create(Definition, null, null!, cause));
        Assert.Throws<ArgumentException>(() => factory.Create(Definition, null, "", cause));
        Assert.Throws<ArgumentNullException>(() => factory.Create(Definition, null, "lowering", null!));
    }

    [Fact]
    public void LoweringPassesItsPoliciesAndResultThrough()
    {
        var managed = WasmMethodLowererTests.CreateManagedMethodBody();
        var expected = new ControlFlow.Structuring.ValidatedStructuredMethodBuilderFactory().Create()
            .Build(managed.ControlFlow);
        var inner = new LoweringStub(() => expected);
        var lowerer = Assert.IsAssignableFrom<IWasmMethodLowerer>(
            new MethodAttributingWasmMethodLowerer(inner, new MethodDefectExceptionFactory()));

        var actual = lowerer.Lower(managed, BranchPrefixDuplicationPolicy.Speed, DispatcherEliminationPolicy.Reducible);

        Assert.Same(expected, actual);
        Assert.Same(managed, inner.Method);
        Assert.Equal(BranchPrefixDuplicationPolicy.Speed, inner.BranchPrefixDuplication);
        Assert.Equal(DispatcherEliminationPolicy.Reducible, inner.DispatcherElimination);
    }

    [Fact]
    public void ALoweringDefectIsReportedAgainstTheMethod()
    {
        var managed = WasmMethodLowererTests.CreateManagedMethodBody();
        var cause = new InvalidOperationException("Sequence contains more than one element");
        var lowerer = new MethodAttributingWasmMethodLowerer(new LoweringStub(() => throw cause),
            new MethodDefectExceptionFactory());

        var defect = Assert.Throws<CompilerException>(() => lowerer.Lower(managed));

        Assert.Equal(DiagnosticCode.CompilerInvariant, defect.Diagnostic.Code);
        Assert.Equal("Tests.Program.Run", defect.Diagnostic.Method);
        Assert.StartsWith("lowering the method failed with System.InvalidOperationException", defect.Diagnostic.Message,
            StringComparison.Ordinal);
        Assert.Same(cause, defect.InnerException);
    }

    [Fact]
    public void LoweringKeepsDiagnosticsAndCancellationAsTheyAre()
    {
        var managed = WasmMethodLowererTests.CreateManagedMethodBody();
        var diagnostic = new CompilerException(new CompilerDiagnostic(DiagnosticCode.IrreducibleControlFlow, "shape"));
        var canceled = new OperationCanceledException();

        Assert.Same(diagnostic, Assert.Throws<CompilerException>(() => new MethodAttributingWasmMethodLowerer(
            new LoweringStub(() => throw diagnostic), new MethodDefectExceptionFactory()).Lower(managed)));
        Assert.Same(canceled, Assert.Throws<OperationCanceledException>(() => new MethodAttributingWasmMethodLowerer(
            new LoweringStub(() => throw canceled), new MethodDefectExceptionFactory()).Lower(managed)));
    }

    [Fact]
    public void TheLoweringDecoratorRejectsMissingInputs()
    {
        var inner = new LoweringStub(() => null!);
        var defects = new MethodDefectExceptionFactory();

        Assert.Throws<ArgumentNullException>(() => new MethodAttributingWasmMethodLowerer(null!, defects));
        Assert.Throws<ArgumentNullException>(() => new MethodAttributingWasmMethodLowerer(inner, null!));
        Assert.Throws<ArgumentNullException>(() => new MethodAttributingWasmMethodLowerer(inner, defects).Lower(null!));
    }

    [Fact]
    public void EmissionPassesItsInputsAndResultThrough()
    {
        var expected = Emission();
        var inner = new EmissionStub(() => expected);
        var instance = Instance();
        var emitter = Assert.IsAssignableFrom<IManagedMethodBodyEmitter>(
            new MethodAttributingManagedMethodBodyEmitter(inner, new MethodDefectExceptionFactory()));

        var actual = emitter.Emit(Definition, default!, default!, default!, default!, default!, instance);

        Assert.Same(expected, actual);
        Assert.Same(Definition, inner.Method);
        Assert.Same(instance, inner.MethodInstance);
    }

    [Fact]
    public void AnEmissionDefectIsReportedAgainstTheMethod()
    {
        var cause = new KeyNotFoundException("local 7");
        var emitter = new MethodAttributingManagedMethodBodyEmitter(new EmissionStub(() => throw cause),
            new MethodDefectExceptionFactory());

        var defect = Assert.Throws<CompilerException>(() =>
            emitter.Emit(Definition, default!, default!, default!, default!, default!, Instance()));

        Assert.Equal(DiagnosticCode.CompilerInvariant, defect.Diagnostic.Code);
        Assert.Equal("Sdcb.Backends.GpuDetGraph.RunCore", defect.Diagnostic.Method);
        Assert.StartsWith("emitting the method failed with System.Collections.Generic.KeyNotFoundException",
            defect.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Same(cause, defect.InnerException);
    }

    [Fact]
    public void EmissionKeepsDiagnosticsAndCancellationAsTheyAre()
    {
        var diagnostic = new CompilerException(new CompilerDiagnostic(DiagnosticCode.FunctionLimit, "too large"));
        var canceled = new OperationCanceledException();

        Assert.Same(diagnostic, Assert.Throws<CompilerException>(() => new MethodAttributingManagedMethodBodyEmitter(
            new EmissionStub(() => throw diagnostic), new MethodDefectExceptionFactory())
            .Emit(Definition, default!, default!, default!, default!, default!)));
        Assert.Same(canceled, Assert.Throws<OperationCanceledException>(() =>
            new MethodAttributingManagedMethodBodyEmitter(
                new EmissionStub(() => throw canceled), new MethodDefectExceptionFactory())
            .Emit(Definition, default!, default!, default!, default!, default!)));
    }

    [Fact]
    public void TheEmissionDecoratorRejectsMissingInputs()
    {
        var inner = new EmissionStub(Emission);
        var defects = new MethodDefectExceptionFactory();

        Assert.Throws<ArgumentNullException>(() => new MethodAttributingManagedMethodBodyEmitter(null!, defects));
        Assert.Throws<ArgumentNullException>(() => new MethodAttributingManagedMethodBodyEmitter(inner, null!));
        Assert.Throws<ArgumentNullException>(() => new MethodAttributingManagedMethodBodyEmitter(inner, defects)
            .Emit(null!, default!, default!, default!, default!, default!));
    }

    private static MethodInstanceModel Instance() => new(
        Definition,
        CliTypeIdentity.Named(Assembly, "Sdcb.Backends", "GpuDetGraph", isValueType: false),
        [],
        Definition.Signature);

    private static ManagedMethodBodyEmission Emission() =>
        new([], 0, 0, null, "RunCore", FilterEnvironmentLayout.Empty, ImmutableDictionary<int, int>.Empty);

    private sealed class LoweringStub(Func<StructuredMethod> result) : IWasmMethodLowerer
    {
        public ManagedMethodBody? Method { get; private set; }
        public BranchPrefixDuplicationPolicy? BranchPrefixDuplication { get; private set; }
        public DispatcherEliminationPolicy? DispatcherElimination { get; private set; }

        public StructuredMethod Lower(ManagedMethodBody method,
            BranchPrefixDuplicationPolicy branchPrefixDuplication = BranchPrefixDuplicationPolicy.SizeAware,
            DispatcherEliminationPolicy dispatcherElimination = DispatcherEliminationPolicy.Disabled)
        {
            Method = method;
            BranchPrefixDuplication = branchPrefixDuplication;
            DispatcherElimination = dispatcherElimination;
            return result();
        }
    }

    private sealed class EmissionStub(Func<ManagedMethodBodyEmission> result) : IManagedMethodBodyEmitter
    {
        public MethodDefinitionModel? Method { get; private set; }
        public MethodInstanceModel? MethodInstance { get; private set; }

        public ManagedMethodBodyEmission Emit(MethodDefinitionModel method, ManagedMethodIdentity callerIdentity,
            StructuredMethod structured, MethodRootMap rootMap, InstructionModuleTarget target,
            IFunctionIndexResolver functionIndices, MethodInstanceModel? methodInstance = null)
        {
            Method = method;
            MethodInstance = methodInstance;
            return result();
        }
    }
}
