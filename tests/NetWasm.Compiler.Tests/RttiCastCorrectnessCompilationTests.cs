using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

[Collection(CorrectnessTestGroup.Name)]
public sealed class RttiCastCorrectnessCompilationTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    public static TheoryData<string, string> Cells => CorpusCaseTestData.Cells(CorpusCaseTestData.RttiCasts);

    [Theory]
    [MemberData(nameof(Cells))]
    public void CompilerPreservesRuntimeCastAndArrayAssignmentSemantics(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
