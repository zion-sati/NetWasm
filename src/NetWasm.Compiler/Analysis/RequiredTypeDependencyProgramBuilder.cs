using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class RequiredTypeDependencyProgramBuilder(
    IReachableProgramBuilder inner,
    IRequiredTypeDependencyValidator dependencies,
    ITypeIdentityResolver identities,
    ICompilerDiagnosticDependencyPathBinder dependencyPaths) : IReachableProgramBuilder
{
    public ReachableProgram Build(
        MethodDefinitionModel? entryPoint,
        ImmutableArray<ProgramExport> exports,
        ReachabilityLedgerSnapshot state,
        IDelegateTypeRecognizer delegateTypes,
        ITypeTestPlanner typeTestPlanner)
    {
        try
        {
            // As the program was before managed optimization: the types it removed allocations of stay required.
            dependencies.Validate(state.Types.Select(identities.GetTypeIdentity)
                .Concat(state.ConstructedTypes)
                .Concat(state.RequiredTypes)
                .Concat(state.MethodInstances.Values.SelectMany(method => method.MethodArguments)));
        }
        catch (CompilerException failure)
        {
            var diagnostic = dependencyPaths.Bind(failure.Diagnostic, state.TypeDependencyPaths);
            if (ReferenceEquals(diagnostic, failure.Diagnostic)) throw;
            throw new CompilerException(diagnostic, failure);
        }
        return inner.Build(entryPoint, exports, state, delegateTypes, typeTestPlanner);
    }
}
