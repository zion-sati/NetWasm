using System.Collections.Immutable;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IReachableProgramBuilder
{
    ReachableProgram Build(
        MethodDefinitionModel entryPoint,
        ImmutableArray<ProgramExport> exports,
        ReachabilityLedgerSnapshot state,
        IDelegateTypeRecognizer delegateTypes,
        ITypeTestPlanner typeTestPlanner);
}
