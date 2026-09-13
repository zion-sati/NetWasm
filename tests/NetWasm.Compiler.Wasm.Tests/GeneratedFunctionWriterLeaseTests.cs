using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class GeneratedFunctionWriterLeaseTests
{
    [Fact]
    public void PreservesItsWriterFamilyAndRejectsMissingMembers()
    {
        var lease = new GeneratedFunctionWriterFactory().Create();

        Assert.NotNull(lease.Bytes);
        Assert.NotNull(lease.Snapshots);
        Assert.NotNull(lease.Instructions);
        Assert.Throws<ArgumentNullException>(() => new GeneratedFunctionWriterLease(
            null!,
            lease.Snapshots,
            lease.Instructions));
        Assert.Throws<ArgumentNullException>(() => new GeneratedFunctionWriterLease(
            lease.Bytes,
            null!,
            lease.Instructions));
        Assert.Throws<ArgumentNullException>(() => new GeneratedFunctionWriterLease(
            lease.Bytes,
            lease.Snapshots,
            null!));
    }
}
