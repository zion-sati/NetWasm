using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Pipeline;

internal interface ICompilationEmissionBuilder
{
    CompilationEmission Emit(
        MetadataCompilationSnapshot metadata,
        CompilationPreparation preparation,
        CompilationAnalysis analysis,
        CompilationLayouts layouts,
        CompilerOptions options);
}
