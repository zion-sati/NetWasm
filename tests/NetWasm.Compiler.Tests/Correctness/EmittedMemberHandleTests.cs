using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class EmittedMemberHandleTests(CorrectnessTestRunner runner) : EmittedAssemblyTestBase(runner)
{
    public static TheoryData<string, string> MatrixCells =>
        CorpusCaseTestData.Cells(CorpusCaseTestData.MemberHandles);

    [Theory]
    [MemberData(nameof(MatrixCells))]
    public void MemberHandlesPreserveGenericAndDeclaringTypeValidation(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell, new MemberHandleFixtureBuilder());

    [Fact]
    public void PersistedFixtureContainsEveryIntendedHandleScenario()
    {
        var builder = Assert.IsAssignableFrom<IEmittedAssemblyBuilder>(new MemberHandleFixtureBuilder());
        using var stream = new MemoryStream(builder.Build().ToArray());
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var methods = metadata.MethodDefinitions
            .Select(metadata.GetMethodDefinition)
            .Select(method => metadata.GetString(method.Name))
            .ToHashSet(StringComparer.Ordinal);

        string[] expected =
        [
            "NonGenericMethodOneArgument",
            "GenericMethodOneArgument",
            "GenericFieldOneArgument",
            "GenericConstructorOneArgument",
            "GenericMethodExactType",
            "GenericFieldExactType",
            "GenericConstructorExactType",
            "GenericMethodDefaultType",
            "GenericFieldDefaultType",
            "InheritedMethodDerivedType",
            "MethodUnrelatedType",
            "InheritedFieldDerivedType",
            "ConstructedGenericMethodOneArgument",
            "ByReferenceSignatureTypes",
            "PointerSignatureTypes",
            "Run",
            "DesktopRun",
        ];
        Assert.Subset(methods, expected.ToHashSet(StringComparer.Ordinal));
        Assert.True(metadata.GetTableRowCount(TableIndex.TypeSpec) > 0);
        Assert.True(metadata.GetTableRowCount(TableIndex.MethodSpec) > 0);
    }

    [Fact(Skip = KnownNonBugSkipReasons.DefaultMemberHandleContext)]
    public void CoreClrDefaultDeclaringTypeHandleAcceptanceIsOutsideTheNetWasmProfile() =>
        throw new InvalidOperationException("The documented profile difference must remain skipped.");

    [Fact(Skip = KnownNonBugSkipReasons.DerivedMethodHandleContext)]
    public void CoreClrDerivedMethodDeclaringTypeHandleAcceptanceIsOutsideTheNetWasmProfile() =>
        throw new InvalidOperationException("The documented profile difference must remain skipped.");
}
