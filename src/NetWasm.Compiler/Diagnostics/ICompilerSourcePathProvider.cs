using System.Collections.Immutable;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerSourcePathProvider
{
    ImmutableArray<string> Provide(CompilerOptions options);
}
