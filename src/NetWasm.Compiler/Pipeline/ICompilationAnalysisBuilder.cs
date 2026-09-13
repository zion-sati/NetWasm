using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Pipeline;

internal interface ICompilationAnalysisBuilder
{
    CompilationAnalysis Analyze(
        MetadataCompilationSnapshot metadata,
        CompilationPreparation preparation);
}
