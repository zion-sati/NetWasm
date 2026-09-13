using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

public interface ICompilationEntryResolver
{
    CompilationEntry Resolve(
        MetadataCompilationSnapshot metadata,
        IMethodFinder methods,
        ISymbolFormatter symbols,
        CompilerOptions options);
}
