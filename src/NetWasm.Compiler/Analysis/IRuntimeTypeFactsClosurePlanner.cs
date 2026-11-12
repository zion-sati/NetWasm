using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IRuntimeTypeFactsClosurePlanner
{
    ImmutableArray<CliTypeIdentity> Plan(
        ImmutableArray<CliTypeIdentity> seeds,
        bool includeGenericArguments,
        bool includeDeclaringTypes,
        bool includeGenericDefinitions,
        bool includeElementTypes);
}
