using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface ITypeFinder
{
    TypeDefinitionModel FindType(string fullName);
}
