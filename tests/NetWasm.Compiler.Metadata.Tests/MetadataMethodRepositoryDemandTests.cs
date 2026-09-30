using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataMethodRepositoryDemandTests
{
    [Fact]
    public void MethodSignaturesResolveOnlyWhenTheirMethodInstanceIsDemanded()
    {
        using var assets = TestAssets.Create();
        var unavailable = assets.CompileSource(
            "Unavailable.ValueTypes",
            "namespace Unavailable; public readonly struct ExternalValue;");
        var fixture = assets.CompileSource(
            "MethodSignatureDemandFixture",
            """
            using Unavailable;

            namespace MethodSignatureDemandFixture;

            public enum Code : byte
            {
                None,
                One,
            }

            public static class EntryPoint
            {
                public static int Run() => 42;

                public static ExternalValue Unused(ExternalValue value) => value;

                public static Code EchoEnum(Code value) => value;

                public static T Echo<T>(T value) => value;
            }
            """,
            unavailable);
        var aliases = ImmutableDictionary<string, string>.Empty.Add(
            "Unavailable.ValueTypes",
            "NetWasm.CoreLib");
        using var lease = MetadataCompilationTestFactory.Load(
            fixture,
            [assets.CoreLib],
            aliases);
        var snapshot = lease.Snapshot;
        var materializations = new MetadataCompilationMaterializationFactory();
        var types = new MetadataTypeRepositoryFactory(materializations);
        var typeFinder = new MetadataTypeFinderFactory(materializations);
        var definitions = new MetadataTypeDefinitionResolverFactory(
            materializations,
            typeFinder);
        var methods = new MetadataMethodRepositoryFactory(materializations);
        var finder = new MetadataMethodFinderFactory(materializations, methods)
            .Create(snapshot);
        var instances = new MetadataMethodInstanceResolverFactory(
            materializations,
            definitions,
            types,
            methods).Create(snapshot);

        var run = finder.FindMethod(
            snapshot.EntryAssemblyIdentity,
            "MethodSignatureDemandFixture.EntryPoint",
            "Run");
        var unused = finder.FindMethod(
            snapshot.EntryAssemblyIdentity,
            "MethodSignatureDemandFixture.EntryPoint",
            "Unused");
        var echoEnum = finder.FindMethod(
            snapshot.EntryAssemblyIdentity,
            "MethodSignatureDemandFixture.EntryPoint",
            "EchoEnum");
        var echo = finder.FindMethod(
            snapshot.EntryAssemblyIdentity,
            "MethodSignatureDemandFixture.EntryPoint",
            "Echo");

        Assert.Equal(
            CliValueKind.I4,
            Resolve(instances, run).Signature.ReturnType);

        var missing = Assert.Throws<CompilerException>(() =>
            Resolve(instances, unused));
        Assert.Equal(DiagnosticCode.AssemblyResolution, missing.Diagnostic.Code);

        var enumInstance = Resolve(instances, echoEnum);
        Assert.Equal(CliValueKind.I4, enumInstance.Signature.ReturnType);
        Assert.Equal(
            CliValueKind.I4,
            Assert.Single(enumInstance.Signature.ParameterTypes));

        var enumDefinition = materializations.Create(snapshot).Types.Values.Single(
            type => type.FullName == "MethodSignatureDemandFixture.Code");
        var enumIdentity = new MetadataTypeIdentityResolverFactory(types)
            .Create(snapshot)
            .GetTypeIdentity(enumDefinition.Key);
        var constructed = Resolve(
            instances,
            echo,
            new CliGenericContext([], [enumIdentity]));
        Assert.Equal(CliValueKind.I4, constructed.Signature.ReturnType);
        Assert.Equal(
            CliValueKind.I4,
            Assert.Single(constructed.Signature.ParameterTypes));
    }

    private static MethodInstanceModel Resolve(
        IMethodInstanceResolver instances,
        MethodDefinitionModel method,
        CliGenericContext? context = null) => instances.ResolveMethodInstance(
        method.Key.Assembly,
        method.Key.MetadataToken,
        method.Name,
        0,
        context);
}
