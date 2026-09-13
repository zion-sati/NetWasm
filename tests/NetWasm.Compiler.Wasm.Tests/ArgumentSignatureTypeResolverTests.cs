using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ArgumentSignatureTypeResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("ArgumentSignatureTests");
    private static readonly EntityKey DeclaringTypeKey = new(Assembly, 1);
    private static readonly EntityKey MethodKey = new(Assembly, 2);

    [Fact]
    public void RejectsNullMethodBody()
    {
        var resolver = new ArgumentSignatureTypeResolver(new RecordingTypeIdentityResolver());

        Assert.Throws<ArgumentNullException>(() =>
            ThroughContract<IArgumentSignatureTypeResolver>(resolver).Resolve(null!, 0));
    }

    [Fact]
    public void ResolvesStaticParameterFromDefinitionSignature()
    {
        var parameter = CliTypeIdentity.Named(Assembly, "Tests", "DefinitionParameter", false);
        var method = CreateMethod(
            isStatic: true,
            MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void), parameter));

        var result = ThroughContract<IArgumentSignatureTypeResolver>(CreateResolver())
            .Resolve(CreateBody(method), 0);

        Assert.Equal(parameter, result);
    }

    [Fact]
    public void ResolvesConstructedStaticParameterFromMethodInstanceSignature()
    {
        var definitionParameter = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "DefinitionParameter",
            false);
        var constructedParameter = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "ConstructedParameter",
            false);
        var method = CreateMethod(
            isStatic: true,
            MethodSignatureModel.Create(
                CliTypeIdentity.FromStackKind(CliValueKind.Void),
                definitionParameter));
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "Tests", "ConstructedType", false),
            [],
            MethodSignatureModel.Create(
                CliTypeIdentity.FromStackKind(CliValueKind.Void),
                constructedParameter));

        var result = ThroughContract<IArgumentSignatureTypeResolver>(CreateResolver()).Resolve(
            CreateBody(method) with { MethodInstance = instance },
            0);

        Assert.Equal(constructedParameter, result);
    }

    [Fact]
    public void ResolvesInstanceReceiverFromDefinitionDeclaringType()
    {
        var expected = CliTypeIdentity.Named(Assembly, "Tests", "DefinitionType", false);
        var identities = new RecordingTypeIdentityResolver(expected);
        var method = CreateMethod(
            isStatic: false,
            MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void)));

        var resolver = new ArgumentSignatureTypeResolver(identities);
        var result = ThroughContract<IArgumentSignatureTypeResolver>(resolver)
            .Resolve(CreateBody(method), 0);

        Assert.Equal(expected, result);
        Assert.Equal(DeclaringTypeKey, identities.RequestedKey);
    }

    [Fact]
    public void ResolvesConstructedInstanceReceiverFromMethodInstanceDeclaringType()
    {
        var expected = CliTypeIdentity.Named(Assembly, "Tests", "ConstructedType", false);
        var identities = new RecordingTypeIdentityResolver();
        var method = CreateMethod(
            isStatic: false,
            MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void)));
        var instance = new MethodInstanceModel(
            method,
            expected,
            [],
            method.Signature);

        var resolver = new ArgumentSignatureTypeResolver(identities);
        var result = ThroughContract<IArgumentSignatureTypeResolver>(resolver).Resolve(
            CreateBody(method) with { MethodInstance = instance },
            0);

        Assert.Equal(expected, result);
        Assert.Null(identities.RequestedKey);
    }

    [Fact]
    public void ResolvesInstanceParameterFromConstructedMethodInstanceSignature()
    {
        var definitionParameter = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "DefinitionParameter",
            false);
        var constructedParameter = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "ConstructedParameter",
            false);
        var method = CreateMethod(
            isStatic: false,
            MethodSignatureModel.Create(
                CliTypeIdentity.FromStackKind(CliValueKind.Void),
                definitionParameter));
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "Tests", "ConstructedType", false),
            [],
            MethodSignatureModel.Create(
                CliTypeIdentity.FromStackKind(CliValueKind.Void),
                constructedParameter));

        var result = ThroughContract<IArgumentSignatureTypeResolver>(CreateResolver()).Resolve(
            CreateBody(method) with { MethodInstance = instance },
            1);

        Assert.Equal(constructedParameter, result);
    }

    private static ArgumentSignatureTypeResolver CreateResolver() =>
        new(new RecordingTypeIdentityResolver());

    private static TResolver ThroughContract<TResolver>(TResolver resolver)
        where TResolver : IArgumentSignatureTypeResolver => resolver;

    private static StructuredMethodHeader CreateBody(MethodDefinitionModel method) =>
        new(method, null, 0, [], [], []);

    private static MethodDefinitionModel CreateMethod(
        bool isStatic,
        MethodSignatureModel signature) =>
        new(
            MethodKey,
            DeclaringTypeKey,
            "ResolveArgument",
            isStatic,
            signature,
            1);

    private sealed class RecordingTypeIdentityResolver(CliTypeIdentity? result = null) :
        ICilTypeIdentityResolver
    {
        public EntityKey? RequestedKey { get; private set; }

        public CliTypeIdentity Resolve(EntityKey key)
        {
            RequestedKey = key;
            return result ?? CliTypeIdentity.Named(Assembly, "Tests", "Fallback", false);
        }
    }
}
