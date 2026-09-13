using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record StaticInitializerGuard(
    int Address,
    EntityKey? Direct,
    string? Constructed)
{
    public static string KeyFor(EntityKey initializer) =>
        $"{initializer.Assembly.Name}:0x{initializer.MetadataToken:x8}";
}
