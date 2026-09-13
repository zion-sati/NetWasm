using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class CilTypeIdentityResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("Types");
    private static readonly EntityKey Key = new(Assembly, 0x02000001);

    [Theory]
    [InlineData("System", "IntPtr", true, CliValueKind.NativeInt)]
    [InlineData("System", "UIntPtr", true, CliValueKind.NativeInt)]
    [InlineData("System", "Int32", true, CliValueKind.I4)]
    [InlineData("Test", "Value", true, CliValueKind.ValueType)]
    [InlineData("Test", "Reference", false, CliValueKind.ManagedReference)]
    public void ResolveNormalizesEveryTypeDefinitionStackFamily(
        string typeNamespace,
        string name,
        bool isValueType,
        CliValueKind expected)
    {
        var repository = new TypeRepository(typeNamespace, name, isValueType);
        var resolver = new CilTypeIdentityResolver(
            repository);

        var result = ((ICilTypeIdentityResolver)resolver).Resolve(Key);

        Assert.Equal(expected, result.StackKind);
    }

    [Fact]
    public void ConstructorRejectsMissingCollaborators()
    {
        var repository = new TypeRepository("Test", "Reference", isValueType: false);

        Assert.Throws<ArgumentNullException>(() =>
            new CilTypeIdentityResolver(null!));
    }

    private sealed class TypeRepository(
        string typeNamespace,
        string name,
        bool isValueType) : ITypeRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => new(
            key,
            typeNamespace,
            name,
            isValueType,
            [],
            []);
    }
}
