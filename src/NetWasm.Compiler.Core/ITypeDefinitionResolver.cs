namespace NetWasm.Compiler.Core;

public interface ITypeDefinitionResolver
{
    TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity);
}
