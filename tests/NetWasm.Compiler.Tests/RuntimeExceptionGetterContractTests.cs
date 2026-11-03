using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

// managed.throw takes a kind's tag where an exception would go and throws what
// the kind's getter in CoreLib's RuntimeExceptionHelpers returns: every kind has
// one, of no parameters, and every tag is below the limit managed.throw tells
// tags from objects by. The frozen OutOfMemoryException comes from
// PreallocatedOutOfMemoryException.GetInstance and carries the HResult its
// constructor gives.
public sealed class RuntimeExceptionGetterContractTests
{
    [Fact]
    public void EveryKindHasACoreLibGetter()
    {
        using var stream = File.OpenRead(CompilerCorrectnessEnvironment.Discover().CoreLibPath);
        using var assembly = new PEReader(stream);
        var metadata = assembly.GetMetadataReader();
        var getters = Methods(metadata, RuntimeExceptionGetters.TypeName);

        foreach (var kind in Enum.GetValues<ManagedExceptionKind>())
        {
            Assert.InRange(RuntimeExceptionGetters.Tag(kind), 1, RuntimeExceptionGetters.TagLimit - 1);
            var name = RuntimeExceptionGetters.MethodName(kind);
            Assert.True(getters.TryGetValue(name, out var parameters), $"{RuntimeExceptionGetters.TypeName}.{name} is missing");
            Assert.Equal(0, parameters);
        }
        Assert.Equal(Enum.GetValues<ManagedExceptionKind>().Length, getters.Count);
        Assert.Equal(0, Methods(metadata, RuntimeExceptionGetters.PreallocatedOutOfMemoryTypeName)["GetInstance"]);
    }

    [Fact]
    public void TheFrozenOutOfMemoryExceptionHasItsConstructorsHResult()
    {
        using var stream = File.OpenRead(CompilerCorrectnessEnvironment.Discover().CoreLibPath);
        using var assembly = new PEReader(stream);
        var metadata = assembly.GetMetadataReader();
        var field = Type(metadata, "System.HResults").GetFields()
            .Select(metadata.GetFieldDefinition)
            .Single(field => metadata.GetString(field.Name) == "COR_E_OUTOFMEMORY");

        Assert.Equal(RuntimeExceptionGetters.OutOfMemoryHResult,
            metadata.GetBlobReader(metadata.GetConstant(field.GetDefaultValue()).Value).ReadInt32());
    }

    private static TypeDefinition Type(MetadataReader metadata, string name) => metadata.TypeDefinitions
        .Select(metadata.GetTypeDefinition)
        .Single(type => $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}" == name);

    // Each method's name with its parameter count; constructors excluded.
    private static Dictionary<string, int> Methods(MetadataReader metadata, string type) => Type(metadata, type)
        .GetMethods()
        .Select(metadata.GetMethodDefinition)
        .Where(method => !metadata.GetString(method.Name).StartsWith('.'))
        .ToDictionary(method => metadata.GetString(method.Name), method => method.GetParameters().Count);
}
