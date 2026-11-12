using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal interface IStaticFieldWriteCollector
{
    ImmutableHashSet<EntityKey> Collect(ReachableProgram program, ReachabilityRoots roots);
}
