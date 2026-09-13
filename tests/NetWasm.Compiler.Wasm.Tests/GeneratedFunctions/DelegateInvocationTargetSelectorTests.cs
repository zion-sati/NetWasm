using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

namespace NetWasm.Compiler.Wasm.Tests.GeneratedFunctions;

public sealed class DelegateInvocationTargetSelectorTests
{
    private static readonly AssemblyIdentity Assembly = new("selector-tests");
    private static readonly EntityKey TypeKey = new(Assembly, 1);

    [Fact]
    public void SelectorFiltersByInvokeIdentityAndOrdersTargets()
    {
        var invoke = Method("Invoke", "Callback", 1);
        var otherInvoke = Method("Invoke", "OtherCallback", 2);
        var targetB = Method("TargetB", "Owner", 3);
        var targetA = Method("TargetA", "Owner", 4);
        var selector = new[]
        {
            new DelegateInvocationTargetSelector(),
        }.Cast<IDelegateInvocationTargetSelector>().Single();

        var selected = selector.Select(
            invoke,
            [Binding(invoke, targetB), Binding(otherInvoke, targetA),
                Binding(invoke, targetA)]);

        Assert.Equal(
            [targetB.CanonicalName, targetA.CanonicalName],
            selected.Select(binding => binding.TargetIdentity.CanonicalName));
    }

    [Fact]
    public void SelectorNormalizesDefaultBindingsAndRejectsMissingInvoke()
    {
        var selector = new[]
        {
            new DelegateInvocationTargetSelector(),
        }.Cast<IDelegateInvocationTargetSelector>().Single();
        var invoke = Method("Invoke", "Callback", 1);

        Assert.Empty(selector.Select(invoke, default));
        Assert.Throws<ArgumentNullException>(() => selector.Select(null!, []));
    }

    private static ManagedDelegateBinding Binding(
        MethodInstanceModel invoke,
        MethodInstanceModel target)
    {
        var type = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        return new(
            new ManagedMethodIdentity(invoke.CanonicalName),
            new ManagedMethodIdentity(target.CanonicalName),
            invoke,
            target,
            [],
            new ManagedDelegateValueBinding(
                type,
                type,
                ManagedDelegateAdaptation.Identity));
    }

    private static MethodInstanceModel Method(
        string name,
        string declaringTypeName,
        int token)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4);
        var definition = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000000 + token),
            TypeKey,
            name,
            true,
            signature,
            1);
        return new(
            definition,
            CliTypeIdentity.Named(
                Assembly,
                "SelectorTests",
                declaringTypeName,
                isValueType: false),
            [],
            signature);
    }
}
