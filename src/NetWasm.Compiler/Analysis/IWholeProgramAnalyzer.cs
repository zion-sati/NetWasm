using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

public interface IWholeProgramAnalyzer
{
    ReachableProgram Analyze(
        MethodDefinitionModel entryPoint,
        IEnumerable<ProgramExport> requestedExports,
        ReachabilityRoots? roots = null);
}
