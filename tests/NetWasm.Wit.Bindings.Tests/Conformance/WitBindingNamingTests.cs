namespace NetWasm.Wit.Bindings.Tests.Conformance;

// Valid WIT whose plain C# names would collide still generates bindings that
// compile: two `host` interfaces from different packages, each with a `point`,
// two exported interfaces with a `go` beside the world's own `go`, and an
// inline interface named like its world, whose own functions share the
// world's imports class. Only the colliding declarations are qualified, and a
// type an interface `use`s is the same declaration, not a collision.
public sealed class WitBindingNamingTests
{
    private const string Wit = """
        package local:app;

        package a:x {
          interface host {
            record point { x: u32 }
            get: func() -> point;
          }
        }

        package b:y {
          interface host {
            record point { y: u32 }
            get: func() -> point;
          }
        }

        world collide {
          import a:x/host;
          import b:y/host;
          import collide: interface {
            ping: func();
          }
          import pong: func();
          export run: interface {
            go: func(x: u32) -> u32;
          }
          export other: interface {
            go: func(x: u32) -> u32;
          }
          export go: func(x: u32) -> u32;
          export single: func() -> u32;
        }
        """;

    // An interface that `use`s another's types, as wasi:http does wasi:io's,
    // names those types again without declaring them, so they do not collide.
    private const string Uses = """
        package local:uses;

        interface types {
          resource request {
            constructor();
          }
          record point { x: u32 }
        }

        interface handler {
          use types.{request, point};
          handle: func(r: borrow<request>, p: point) -> u32;
        }

        world app {
          import types;
          import handler;
        }
        """;

    [Fact]
    public void CollidingNamesAreQualifiedAndTheBindingsCompile()
    {
        var source = Generate(Wit, "collide");

        foreach (var declaration in (string[])
                 [
                     "public static partial class AXHostImports", "public static partial class BYHostImports",
                     "public readonly struct AXHostPoint", "public readonly struct BYHostPoint",
                     "public static partial class CollideCollideImports", "public static partial class CollideImports",
                     "public static partial uint RunGo(uint x);", "public static partial uint OtherGo(uint x);",
                     "public static partial uint Go(uint x);", "public static partial uint Single();",
                 ])
        {
            Assert.Contains(declaration, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TypesAnInterfaceUsesKeepTheirNames()
    {
        var source = Generate(Uses, "app");

        Assert.Contains("class Request", source, StringComparison.Ordinal);
        Assert.Contains("public readonly struct Point", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TypesRequest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TypesPoint", source, StringComparison.Ordinal);
    }

    private static string Generate(string wit, string world)
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-wit-names-");
        try
        {
            var path = Path.Combine(directory.FullName, "world.wit");
            File.WriteAllText(path, wit);
            var document = BindingCompiler.Reader().Read(path);

            var source = WitBindingCompositionRoot.Create().Generate(document, document.SelectWorld(world));

            Assert.Empty(BindingCompiler.Compile(source));
            return source;
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
