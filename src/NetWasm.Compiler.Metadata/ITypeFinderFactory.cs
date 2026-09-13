using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface ITypeFinderFactory
{
    ITypeFinder Create(MetadataCompilationSnapshot snapshot);
}
