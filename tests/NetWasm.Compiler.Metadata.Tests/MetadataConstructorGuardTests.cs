using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

// Every collaborator a metadata actor takes is required: its constructor rejects each missing one by name.
public sealed class MetadataConstructorGuardTests
{
    private static readonly string[] Actors =
    [
        "NetWasm.Compiler.Metadata.FrameworkFieldContractValidator",
        "NetWasm.Compiler.Metadata.FrameworkMethodContractValidator",
        "NetWasm.Compiler.Metadata.RequiredTypeDependencyValidator",
    ];

    public static TheoryData<string, int> MissingDependencies()
    {
        var data = new TheoryData<string, int>();
        foreach (var name in Actors)
        {
            for (var index = 0; index < Constructor(name).GetParameters().Length; index++)
            {
                data.Add(name, index);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(MissingDependencies))]
    public void ConstructorsRejectEveryMissingDependency(string actor, int missing)
    {
        var constructor = Constructor(actor);
        var parameters = constructor.GetParameters();
        var arguments = parameters.Select(static parameter => Placeholder(parameter.ParameterType)).ToArray();
        arguments[missing] = null;

        var exception = Assert.Throws<TargetInvocationException>(() => constructor.Invoke(arguments));

        Assert.Equal(parameters[missing].Name, Assert.IsType<ArgumentNullException>(exception.InnerException).ParamName);
    }

    // The public constructor reads the snapshot's assemblies; the internal one takes them read.
    [Fact]
    public void ConstraintReadersRequireTheirMetadataAndDefinitions()
    {
        var definitions = (ITypeDefinitionResolver)Placeholder(typeof(ITypeDefinitionResolver))!;
        Assert.Throws<ArgumentNullException>("metadata", () => new MetadataGenericParameterConstraintReader(
            (MetadataCompilationSnapshot)null!, definitions));
        Assert.Throws<ArgumentNullException>("assemblies", () => new MetadataGenericParameterConstraintReader(
            (ImmutableDictionary<AssemblyIdentity, MetadataAssemblySnapshot>)null!, definitions));
        Assert.Throws<ArgumentNullException>("definitions", () => new MetadataGenericParameterConstraintReader(
            ImmutableDictionary<AssemblyIdentity, MetadataAssemblySnapshot>.Empty, null!));
    }

    private static ConstructorInfo Constructor(string name) => Assert.Single(
        typeof(ManagedAssembly).Assembly.GetType(name, throwOnError: true)!
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));

    private static object? Placeholder(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type)
        : type.IsInterface ? DispatchProxy.Create(type, typeof(UnusedDependencyProxy))
        : RuntimeHelpers.GetUninitializedObject(type);

#pragma warning disable CA1852 // DispatchProxy requires an inheritable proxy base type.
    private class UnusedDependencyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException();
    }
#pragma warning restore CA1852
}
