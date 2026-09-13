using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IMethodFinderFactory
{
    IMethodFinder Create(MetadataCompilationSnapshot snapshot);
}
