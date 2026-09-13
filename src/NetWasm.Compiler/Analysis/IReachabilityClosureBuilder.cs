using System.Collections.Generic;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IReachabilityClosureBuilder
{
    ReachableProgram Build(
        MethodDefinitionModel entryPoint,
        IEnumerable<ProgramExport> requestedExports,
        ReachabilityRoots? roots = null);
}
