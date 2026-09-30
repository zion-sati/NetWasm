using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class RequestedExportMethodSelectorTests
{
    [Fact]
    public void SelectsTheDirectInstanceAndIgnoresConstructedSiblings()
    {
        var definition = new FakeProgram().GetMethod(EmitterTestSupport.EntryKey) with
        {
            Signature = MethodSignatureModel.Create(
                CliValueKind.ValueType,
                CliValueKind.ValueType),
        };
        var selectedSignature = MethodSignatureModel.Create(
            CliValueKind.I8,
            CliValueKind.I8);
        var direct = CreateInstance(definition, [], selectedSignature);
        var constructed = CreateInstance(
            definition with { GenericArity = 1 },
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)],
            selectedSignature);

        var result = RequestedExportMethodSelector.Select(
            ImmutableDictionary<string, EntityKey>.Empty.Add(
                "echo",
                definition.Key),
            ImmutableDictionary<string, MethodInstanceModel>.Empty
                .Add("direct", direct)
                .Add("constructed", constructed));

        Assert.Same(direct, Assert.Single(result).Value);
    }

    [Fact]
    public void MissingOrContradictoryDirectInstancesAreCompilerInvariants()
    {
        var definition = new FakeProgram().GetMethod(EmitterTestSupport.EntryKey);
        var requested = ImmutableDictionary<string, EntityKey>.Empty.Add(
            "extra",
            definition.Key);

        var missing = Assert.Throws<CompilerException>(() =>
            RequestedExportMethodSelector.Select(
                requested,
                ImmutableDictionary<string, MethodInstanceModel>.Empty));
        Assert.Equal(DiagnosticCode.CompilerInvariant, missing.Diagnostic.Code);
        Assert.Contains("no direct selected method instance", missing.Message);

        var first = CreateInstance(definition, [], definition.Signature);
        var second = first with
        {
            DeclaringType = CliTypeIdentity.Named(
                EmitterTestSupport.Assembly,
                "Tests",
                "OtherEntryPoint",
                isValueType: false),
        };
        var contradictory = Assert.Throws<CompilerException>(() =>
            RequestedExportMethodSelector.Select(
                requested,
                ImmutableDictionary<string, MethodInstanceModel>.Empty
                    .Add("first", first)
                    .Add("second", second)));
        Assert.Equal(DiagnosticCode.CompilerInvariant,
            contradictory.Diagnostic.Code);
        Assert.Contains("multiple direct selected method instances",
            contradictory.Message);
    }

    [Fact]
    public void RejectsMissingCollaborativeInputs()
    {
        var exports = ImmutableDictionary<string, EntityKey>.Empty;
        var methods = ImmutableDictionary<string, MethodInstanceModel>.Empty;

        Assert.Throws<ArgumentNullException>(() =>
            RequestedExportMethodSelector.Select(null!, methods));
        Assert.Throws<ArgumentNullException>(() =>
            RequestedExportMethodSelector.Select(exports, null!));
    }

    private static MethodInstanceModel CreateInstance(
        MethodDefinitionModel definition,
        ImmutableArray<CliTypeIdentity> methodArguments,
        MethodSignatureModel signature) => new(
            definition,
            CliTypeIdentity.Named(
                EmitterTestSupport.Assembly,
                "Tests",
                "EntryPoint",
                isValueType: false),
            methodArguments,
            signature);
}
