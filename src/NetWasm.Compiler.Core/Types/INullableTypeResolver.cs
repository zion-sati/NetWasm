namespace NetWasm.Compiler.Core.Types;

public interface INullableTypeResolver
{
    CliTypeIdentity? Resolve(CliTypeIdentity type);
}
