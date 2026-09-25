using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusExportFactory
{
    ImmutableArray<RequestedExport> Create(CorpusFixture fixture);
}
