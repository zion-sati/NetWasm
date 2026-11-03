using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TypeIdPreorderTests
{
    // Object (1) has Animal (2) and Array (3); Animal has Dog (4) and Cat (6),
    // Dog has Puppy (7); Array has int[] (5) and string[] (9); IPet (8) has no
    // base. Each type's descendants follow it, wherever they were discovered.
    [Fact]
    public void TypesFollowTheirBaseTypesInPreorder()
    {
        var numbers = TypeIdPreorder.Number([(1, 0), (2, 1), (3, 1), (4, 2), (5, 3), (6, 2), (7, 4), (8, 0), (9, 3)]);

        Assert.Equal((1, 8), numbers[1]);
        Assert.Equal((2, 5), numbers[2]);
        Assert.Equal((3, 4), numbers[4]);
        Assert.Equal((4, 4), numbers[7]);
        Assert.Equal((5, 5), numbers[6]);
        Assert.Equal((6, 8), numbers[3]);
        Assert.Equal((7, 7), numbers[5]);
        Assert.Equal((8, 8), numbers[9]);
        Assert.Equal((9, 9), numbers[8]);
    }

    [Fact]
    public void ATypeWhoseBaseIsNotNumberedIsARoot() =>
        Assert.Equal((1, 2), TypeIdPreorder.Number([(5, 99), (6, 5)])[5]);

    [Fact]
    public void CyclicBaseTypesAreRejected() =>
        Assert.Throws<InvalidOperationException>(() => TypeIdPreorder.Number([(1, 2), (2, 1)]));

    // A compiled program's classes, boxed value types and arrays hold the ids
    // of their base types' intervals, and nothing else does.
    [Fact]
    public void CompiledTypesLieInTheirBaseTypesIntervals()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("TypeIdPreorderFixture", """
            namespace TypeIdPreorderFixture;

            public abstract class Animal { }
            public sealed class Rock { }
            public class Dog : Animal { }
            public sealed class Cat : Animal { }
            public sealed class Puppy : Dog { }
            public enum Color { Red }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    object[] values = [new Rock(), new Puppy(), new Cat(), new Dog(), new int[input], new string[input], Color.Red, input];
                    var count = 0;
                    foreach (var value in values)
                    {
                        if (value is Animal) count += 1;
                        if (value is System.Array) count += 10;
                        if (value is System.Enum) count += 100;
                    }
                    return count;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "TypeIdPreorderFixture.EntryPoint", "Run", []));
        var layouts = result.Layouts.TypeLayouts;
        ObjectLayout Layout(string name) => layouts.ObjectIdentities
            .Concat(layouts.ConstructedObjects)
            .First(pair => pair.Key.CanonicalName.EndsWith(name, StringComparison.Ordinal)).Value;
        bool Within(ObjectLayout type, ObjectLayout interval) =>
            type.TypeId >= interval.TypeId && type.TypeId <= interval.LastSubtypeId;

        var animal = Layout("TypeIdPreorderFixture.Animal");
        Assert.All(["TypeIdPreorderFixture.Dog", "TypeIdPreorderFixture.Cat", "TypeIdPreorderFixture.Puppy"],
            name => Assert.True(Within(Layout(name), animal), name));
        Assert.False(Within(Layout("TypeIdPreorderFixture.Rock"), animal));
        Assert.True(Within(Layout("TypeIdPreorderFixture.Puppy"), Layout("TypeIdPreorderFixture.Dog")));
        Assert.False(Within(Layout("TypeIdPreorderFixture.Cat"), Layout("TypeIdPreorderFixture.Dog")));
        var array = layouts.ObjectsByName["System.Array"];
        Assert.True(Within(Layout("primitive:i4[]"), array));
        Assert.True(Within(Layout("primitive:string[]"), array));
        Assert.True(Within(Layout("TypeIdPreorderFixture.Color"), Layout("System.Enum")));
        Assert.All(layouts.ObjectIdentities.Values.Concat(layouts.ConstructedObjects.Values),
            layout => Assert.InRange(layout.LastSubtypeId, layout.TypeId, int.MaxValue));
    }
}
