using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using static NetWasm.Compiler.Wasm.Tests.ExpressionIntrinsicEmissionFixture;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class DelegateDynamicInvokeIntrinsicEmitterTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RejectsContradictoryContractsBeforePublishingRoots(int defect)
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var result = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        if (defect == 4)
        {
            var nullable = CliTypeIdentity.GenericInstantiation(
                Type("Nullable`1", valueType: true),
                [result]);
            fixture.NullableTypes.Add(nullable, result);
            result = nullable;
        }
        var descriptor = TypedMethod(
            "Invoke",
            result,
            Type("Callback"),
            isStatic: false,
            parameters: defect == 2
                ? [CliTypeIdentity.FromStackKind(CliValueKind.Void)]
                : defect == 5
                    ? [CliTypeIdentity.Primitive(
                        "byref-without-element",
                        CliValueKind.ManagedAddress)]
                : []);
        var support = Support();
        if (defect == 3)
        {
            support = support with
            {
                ArgumentTarget = Method(
                    "InvalidArgument",
                    CliValueKind.ManagedReference,
                    isStatic: false),
            };
        }
        var methods = ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
            defect == 1 ? "contradictory-key" : descriptor.CanonicalName,
            descriptor);
        support = support with { Methods = methods };
        var members = MemberExecutionPlan.Empty with { DelegateInvocation = support };
        var factory = Method(
            "InvokeDelegate",
            defect == 0 ? CliValueKind.I4 : CliValueKind.ManagedReference,
            isStatic: true,
            parameters:
            [
                CliValueKind.ManagedReference,
                CliValueKind.ManagedReference,
                CliValueKind.ManagedReference,
            ]);
        var request = fixture.Request(
            RuntimeIntrinsic.DelegateDynamicInvoke,
            factory,
            members: members);

        var error = Assert.Throws<CompilerException>(() =>
            Create(fixture).Emit(request, fixture.Writer));

        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
        Assert.Equal(0, fixture.RootCount);
        Assert.Empty(fixture.Code);
    }

    [Fact]
    public void RejectsMissingCompilerPlanBeforePublishingRoots()
    {
        var fixture = new ExpressionIntrinsicEmissionFixture(false);
        var request = fixture.Request(RuntimeIntrinsic.DelegateDynamicInvoke);

        var error = Assert.Throws<CompilerException>(() =>
            Create(fixture).Emit(request, fixture.Writer));

        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
        Assert.Equal(0, fixture.RootCount);
        Assert.Empty(fixture.Code);
    }

    private static DelegateDynamicInvokePlan Support()
    {
        var unsupported = Method(
            "ThrowDynamicInvokeUnsupported",
            CliValueKind.ManagedReference,
            isStatic: true);
        var argument = Method(
            "ThrowDynamicInvokeArgument",
            CliValueKind.ManagedReference,
            isStatic: true);
        var count = Method(
            "ThrowDynamicInvokeParameterCount",
            CliValueKind.ManagedReference,
            isStatic: true);
        var invocation = Method(
            "ThrowTargetInvocation",
            CliValueKind.ManagedReference,
            isStatic: true,
            parameters: [CliValueKind.ManagedReference]);
        return new(
            ImmutableDictionary<string, MethodInstanceModel>.Empty,
            [],
            unsupported,
            argument,
            count,
            invocation);
    }

    private static MethodInstanceModel TypedMethod(
        string name,
        CliTypeIdentity result,
        CliTypeIdentity owner,
        bool isStatic,
        params CliTypeIdentity[] parameters)
    {
        var signature = MethodSignatureModel.Create(result, parameters);
        var definition = new MethodDefinitionModel(
            EmitterTestSupport.EntryKey,
            EmitterTestSupport.TypeKey,
            name,
            isStatic,
            signature,
            1);
        return new(definition, owner, [], signature);
    }

    private static DelegateDynamicInvokeIntrinsicEmitter Create(
        ExpressionIntrinsicEmissionFixture fixture) => new(
        fixture.Layouts,
        new AddressInstructionEmitter(fixture.Layouts),
        fixture.Layouts,
        fixture.Layouts,
        fixture.Scalars,
        fixture.Layouts,
        fixture.TypeValidator,
        fixture.Imports,
        fixture.Exceptions,
        fixture.Roots,
        new ExceptionPayloadBlockEmitter(fixture.Layouts),
        fixture,
        fixture,
        fixture);
}
