using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

// The release's GC stress sweep runs the classes eng/compiler-gc-stress-classes.txt
// lists, so the list names every class that runs differential fixtures and no other.
public sealed class GcStressClassListTests
{
    [Fact]
    public void TheListNamesEveryClassThatRunsDifferentialFixtures()
    {
        var expected = typeof(GcStressClassListTests).Assembly.GetTypes()
            .Where(static type => type.GetConstructors().Any(static constructor =>
                constructor.GetParameters().Any(static parameter => parameter.ParameterType == typeof(CorrectnessTestRunner))))
            .Select(static type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);
        var listed = File.ReadAllLines(Path.Combine(TestAssets.FindRepositoryRoot(), "eng", "compiler-gc-stress-classes.txt"))
            .Select(static line => line.Trim())
            .Where(static line => line.Length != 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(expected.Except(listed).Order(StringComparer.Ordinal));
        Assert.Empty(listed.Except(expected).Order(StringComparer.Ordinal));
    }
}
