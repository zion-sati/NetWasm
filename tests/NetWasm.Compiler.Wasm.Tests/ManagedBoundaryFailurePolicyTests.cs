using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ManagedBoundaryFailurePolicyTests
{
    [Fact]
    public void ClassifiesEveryBoundaryKind()
    {
        var policy = EmitterTestSupport.CreateBoundaryFailureDispositions();

        foreach (var kind in Enum.GetValues<ManagedBoundaryKind>())
            Assert.True(Enum.IsDefined(policy.Resolve(kind)));
    }

    [Theory]
    [InlineData(ManagedBoundaryKind.ProcessEntryPoint)]
    [InlineData(ManagedBoundaryKind.SynchronousExport)]
    [InlineData(ManagedBoundaryKind.HostCallback)]
    public void OutwardSynchronousBoundariesReportAndTerminate(ManagedBoundaryKind kind)
    {
        var policy = EmitterTestSupport.CreateBoundaryFailureDispositions();

        Assert.Equal(ManagedBoundaryFailureDisposition.ReportAndTerminate, policy.Resolve(kind));
    }

    [Theory]
    [InlineData(ManagedBoundaryKind.AsynchronousExportStart)]
    [InlineData(ManagedBoundaryKind.AsynchronousExportCompletion)]
    [InlineData(ManagedBoundaryKind.AsynchronousExportObservation)]
    public void AsynchronousExportBoundariesRejectTheirOperation(ManagedBoundaryKind kind)
    {
        var policy = EmitterTestSupport.CreateBoundaryFailureDispositions();

        Assert.Equal(ManagedBoundaryFailureDisposition.RejectAsynchronousOperation, policy.Resolve(kind));
    }

    [Theory]
    [InlineData(ManagedBoundaryKind.ComponentAdapter)]
    [InlineData(ManagedBoundaryKind.ComponentInitialize)]
    [InlineData(ManagedBoundaryKind.ComponentReallocate)]
    [InlineData(ManagedBoundaryKind.ComponentPostReturn)]
    public void ComponentBoundariesUseTheComponentFailurePath(ManagedBoundaryKind kind)
    {
        var policy = EmitterTestSupport.CreateBoundaryFailureDispositions();

        Assert.Equal(ManagedBoundaryFailureDisposition.FailComponentOperation, policy.Resolve(kind));
    }

    [Theory]
    [InlineData(ManagedBoundaryKind.AsynchronousImportCompletion)]
    [InlineData(ManagedBoundaryKind.InternalRuntimeDispatch)]
    public void InternalContinuationBoundariesPropagateManagedExceptions(ManagedBoundaryKind kind)
    {
        var policy = EmitterTestSupport.CreateBoundaryFailureDispositions();

        Assert.Equal(ManagedBoundaryFailureDisposition.PropagateManagedException, policy.Resolve(kind));
    }

    [Fact]
    public void RejectsUnknownBoundaryKind()
    {
        var policy = EmitterTestSupport.CreateBoundaryFailureDispositions();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            policy.Resolve((ManagedBoundaryKind)int.MaxValue));
    }

}
