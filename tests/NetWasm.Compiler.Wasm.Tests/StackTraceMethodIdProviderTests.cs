using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StackTraceMethodIdProviderTests
{
    [Fact]
    public void ProviderResolvesDirectAndConstructedMethodsOrZero()
    {
        var provider = As<IStackTraceMethodIdProvider>(new StackTraceMethodIdProvider());
        var program = new FakeProgram();
        var method = program.GetMethod(ConstructorKey);
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.Named(
                    Assembly,
                    "Test",
                    "Generic`1",
                    isValueType: false),
                [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]),
            [],
            method.Signature);
        var plan = new StackTraceMethodPlan(
            ImmutableDictionary<EntityKey, int>.Empty.Add(method.Key, 7),
            ImmutableDictionary<string, int>.Empty.Add(instance.CanonicalName, 11),
            [],
            4,
            5);

        Assert.Equal(7, provider.GetId(plan, method, null));
        Assert.Equal(11, provider.GetId(plan, method, instance));
        Assert.Equal(0, provider.GetId(
            plan with { DirectMethodIds = ImmutableDictionary<EntityKey, int>.Empty },
            method,
            null));
        Assert.Equal(0, provider.GetId(
            plan with { ConstructedMethodIds = ImmutableDictionary<string, int>.Empty },
            method,
            instance));
    }

    [Fact]
    public void ProviderRejectsMissingRequiredInputs()
    {
        var provider = As<IStackTraceMethodIdProvider>(new StackTraceMethodIdProvider());
        var method = new FakeProgram().GetMethod(ConstructorKey);

        Assert.Throws<ArgumentNullException>(() => provider.GetId(null!, method, null));
        Assert.Throws<ArgumentNullException>(() => provider.GetId(
            StackTraceMethodPlan.Disabled,
            null!,
            null));
    }

    private static T As<T>(T value) => value;
}
