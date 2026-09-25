namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RandomCilRunDirectoryFactoryTests
{
    [Theory]
    [InlineData(0, false, "debug", "normal")]
    [InlineData(0, true, "debug", "diagnostic")]
    [InlineData(1, false, "release", "normal")]
    [InlineData(1, true, "release", "diagnostic")]
    public void RepeatedSliceUsesDistinctRootsWithExactIdentity(int profile, bool diagnostic, string profileName, string mode)
    {
        var roots = new Roots();
        var factory = Factory(roots);

        var first = factory.Create((CilProfile)profile, 42, 3, diagnostic);
        var second = factory.Create((CilProfile)profile, 42, 3, diagnostic);

        Assert.Equal("root-1", first.Root);
        Assert.Equal("root-2", second.Root);
        Assert.Equal(Path.Combine(first.Root, profileName, "shard-0042", "slice-03", mode), first.Directory);
        Assert.Equal(Path.Combine(second.Root, profileName, "shard-0042", "slice-03", mode), second.Directory);
        Assert.NotEqual(first, second);
        Assert.Equal(2, roots.Calls);
    }

    [Theory]
    [InlineData(2, 0, 0)]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void InvalidIdentityDoesNotAllocate(int profile, int shard, int slice)
    {
        var roots = new Roots();
        Assert.Throws<ArgumentOutOfRangeException>(() => Factory(roots).Create((CilProfile)profile, shard, slice, false));
        Assert.Equal(0, roots.Calls);
    }

    [Fact]
    public void RootFailurePropagatesWithoutRetry()
    {
        var cause = new IOException("root failure");
        var roots = new Roots { Failure = cause };
        Assert.Same(cause, Assert.Throws<IOException>(() => Factory(roots).Create(CilProfile.Debug, 0, 0, false)));
        Assert.Equal(1, roots.Calls);
    }

    private static IRandomCilRunDirectoryFactory Factory(Roots roots) =>
        Assert.IsAssignableFrom<IRandomCilRunDirectoryFactory>(new RandomCilRunDirectoryFactory(roots));

    private sealed class Roots : ICorpusRunDirectoryFactory
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }
        public string Create()
        {
            Calls++;
            if (Failure is not null) throw Failure;
            return "root-" + Calls;
        }
    }
}
