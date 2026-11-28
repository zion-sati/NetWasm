// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Globalization;
using System.Text;

namespace NetWasm.Compiler.Tests.AttributeQueries;

// A program asking every attribute query of every target a program names statically (an assembly, a module and
// types) with every filter shape (none,
// a sealed attribute, an unsealed base, Attribute itself, an interface, an open generic definition, a closed generic
// instantiation, and a type parameter instantiated with a closed generic or an unsealed base), inheriting and not,
// through every overload: the target's own methods, Attribute's statics and CustomAttributeExtensions' generic and
// Type-taking forms. Each result is one int: an attribute's Id, a Boolean, or for many attributes their count, the sum
// of their Ids and which array type holds them; an exception is a negative code.
internal static class AttributeQueryMatrix
{
    private enum TargetKind
    {
        Assembly,
        Module,
        Member,
        Parameter,
    }

    // The targets a program names without looking members up at run time, which NetWasm does not support: a member, a
    // parameter or a return value is reached only through GetMethod and its siblings.
    private static readonly (string Name, string Expression, TargetKind Kind)[] Targets =
    [
        ("assembly", "typeof(Base).Assembly", TargetKind.Assembly),
        ("module", "typeof(Base).Module", TargetKind.Module),
        ("type", "typeof(Derived)", TargetKind.Member),
        ("base type", "typeof(Base)", TargetKind.Member),
        ("serializable type", "typeof(Plain)", TargetKind.Member),
    ];

    // A filter as a Type, and as a type argument where one can be.
    private static readonly (string Name, string Type, string? Argument)[] Filters =
    [
        ("sealed", "typeof(LeafAttribute)", "LeafAttribute"),
        ("base", "typeof(TagAttribute)", "TagAttribute"),
        ("Attribute", "typeof(Attribute)", "Attribute"),
        ("interface", "typeof(IMarker)", null),
        ("open", "typeof(InfoAttribute<>)", null),
        ("closed", "typeof(InfoAttribute<int>)", "InfoAttribute<int>"),
    ];

    // The targets of a group: "types", "assembly" or "module".
    public static bool InGroup(string target, string group) => group switch
    {
        "types" => target.EndsWith("type", StringComparison.Ordinal),
        _ => target == group,
    };

    // Each query of the group's targets: its description and its C# expression, in order.
    public static IReadOnlyList<(string Name, string Expression)> Queries(string group)
    {
        var queries = new List<(string, string)>();
        for (var index = 0; index < Targets.Length; index++)
        {
            var (target, t, kind) = Targets[index];
            if (!InGroup(target, group))
            {
                continue;
            }
            var inherits = kind is TargetKind.Member or TargetKind.Parameter;
            void Add(string form, string expression) => queries.Add(($"{target}: {form}", expression));
            foreach (var inherit in new[] { "true", "false" })
            {
                Add($"own GetCustomAttributes({inherit})", $"{t}.GetCustomAttributes({inherit})");
                Add($"Attribute.GetCustomAttributes(t, {inherit})", $"Attribute.GetCustomAttributes({t}, {inherit})");
                if (inherits) Add($"GetCustomAttributes({inherit}) extension", $"CustomAttributeExtensions.GetCustomAttributes({t}, {inherit})");
            }
            Add("Attribute.GetCustomAttributes(t)", $"Attribute.GetCustomAttributes({t})");
            Add("GetCustomAttributes() extension", $"CustomAttributeExtensions.GetCustomAttributes({t})");
            foreach (var (filter, type, argument) in Filters)
            {
                foreach (var inherit in new[] { "true", "false" })
                {
                    Add($"own GetCustomAttributes({filter}, {inherit})", $"{t}.GetCustomAttributes({type}, {inherit})");
                    Add($"own IsDefined({filter}, {inherit})", $"{t}.IsDefined({type}, {inherit})");
                    Add($"Attribute.GetCustomAttributes(t, {filter}, {inherit})", $"Attribute.GetCustomAttributes({t}, {type}, {inherit})");
                    Add($"Attribute.GetCustomAttribute(t, {filter}, {inherit})", $"Attribute.GetCustomAttribute({t}, {type}, {inherit})");
                    Add($"Attribute.IsDefined(t, {filter}, {inherit})", $"Attribute.IsDefined({t}, {type}, {inherit})");
                    if (inherits)
                    {
                        Add($"GetCustomAttributes({filter}, {inherit}) extension", $"CustomAttributeExtensions.GetCustomAttributes({t}, {type}, {inherit})");
                        Add($"GetCustomAttribute({filter}, {inherit}) extension", $"CustomAttributeExtensions.GetCustomAttribute({t}, {type}, {inherit})");
                        Add($"IsDefined({filter}, {inherit}) extension", $"CustomAttributeExtensions.IsDefined({t}, {type}, {inherit})");
                        if (argument is not null)
                        {
                            Add($"GetCustomAttribute<{filter}>({inherit})", $"{t}.GetCustomAttribute<{argument}>({inherit})");
                            Add($"GetCustomAttributes<{filter}>({inherit})", $"{t}.GetCustomAttributes<{argument}>({inherit})");
                        }
                    }
                }
                Add($"Attribute.GetCustomAttributes(t, {filter})", $"Attribute.GetCustomAttributes({t}, {type})");
                Add($"Attribute.GetCustomAttribute(t, {filter})", $"Attribute.GetCustomAttribute({t}, {type})");
                Add($"Attribute.IsDefined(t, {filter})", $"Attribute.IsDefined({t}, {type})");
                Add($"GetCustomAttributes({filter}) extension", $"CustomAttributeExtensions.GetCustomAttributes({t}, {type})");
                Add($"GetCustomAttribute({filter}) extension", $"CustomAttributeExtensions.GetCustomAttribute({t}, {type})");
                Add($"IsDefined({filter}) extension", $"CustomAttributeExtensions.IsDefined({t}, {type})");
                if (argument is not null)
                {
                    Add($"GetCustomAttribute<{filter}>()", $"{t}.GetCustomAttribute<{argument}>()");
                    Add($"GetCustomAttributes<{filter}>()", $"{t}.GetCustomAttributes<{argument}>()");
                }
            }
            // A filter that is a type parameter of the method asking.
            foreach (var argument in new[] { "InfoAttribute<short>", "TagAttribute" })
            {
                Add($"GetCustomAttributes<T = {argument}>()", $"Many{index}<{argument}>()");
                Add($"GetCustomAttribute<T = {argument}>()", $"One{index}<{argument}>()");
                if (inherits)
                {
                    Add($"GetCustomAttributes<T = {argument}>(false)", $"ManyOwn{index}<{argument}>()");
                }
            }
        }
        return queries;
    }

    public static string Source(IReadOnlyList<(string Name, string Expression)> queries)
    {
        var source = new StringBuilder("""
            using System;
            using System.Collections;
            using System.Reflection;

            [assembly: Tag(1)]
            [assembly: Leaf(2)]
            [assembly: Info<int>(3)]
            [assembly: Info<short>(4)]
            [assembly: Mark(5)]
            [module: Tag(6)]
            [module: Leaf(7)]
            [module: Info<int>(8)]
            [module: Mark(9)]

            public interface IHasId { int Id { get; } }
            public interface IMarker { }

            [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
            public class TagAttribute(int id) : Attribute, IHasId { public int Id { get; } = id; }

            [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
            public sealed class LeafAttribute(int id) : TagAttribute(id) { }

            [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
            public sealed class InfoAttribute<T>(int id) : Attribute, IHasId { public int Id { get; } = id; }

            [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
            public sealed class MarkAttribute(int id) : Attribute, IHasId, IMarker { public int Id { get; } = id; }

            [Tag(10)] [Leaf(11)] [Info<int>(12)] [Mark(13)]
            public class Base
            {
                [Tag(20)] [Mark(21)] [Info<short>(22)]
                public virtual void Method([Tag(30)] [Mark(31)] [Info<int>(32)] int value) { }

                [return: Tag(40)] [return: Leaf(41)]
                public virtual int Returns() => 0;

                [Tag(50)] [Leaf(51)]
                public virtual int Property { get; set; }

                [Tag(60)] [Mark(61)]
                public virtual event Action Event;

                [Tag(70)] [Info<int>(71)]
                public int Field;

                [Tag(80)] [Mark(81)]
                public Base() { }

                protected void Raise() => Event();
            }

            // Desktop adds SerializableAttribute, from the type's flags, to what it reads from metadata.
            [Serializable] [Tag(90)]
            public sealed class Plain { }

            [Leaf(14)] [Info<short>(15)]
            public class Derived : Base
            {
                [Leaf(23)]
                public override void Method([Leaf(33)] int value) { }

                [return: Info<int>(42)]
                public override int Returns() => 1;

                [Info<int>(52)]
                public override int Property { get; set; }

                [Leaf(62)]
                public override event Action Event;

                [Tag(82)]
                public Derived() { }
            }

            public static class EntryPoint
            {

            """);
        // Each target's queries with a type parameter as the filter.
        for (var index = 0; index < Targets.Length; index++)
        {
            var (_, t, kind) = Targets[index];
            source.AppendLine(CultureInfo.InvariantCulture, $"    private static object Many{index}<T>() where T : Attribute => {t}.GetCustomAttributes<T>();");
            source.AppendLine(CultureInfo.InvariantCulture, $"    private static object One{index}<T>() where T : Attribute => {t}.GetCustomAttribute<T>();");
            if (kind is TargetKind.Member or TargetKind.Parameter)
            {
                source.AppendLine(CultureInfo.InvariantCulture, $"    private static object ManyOwn{index}<T>() where T : Attribute => {t}.GetCustomAttributes<T>(false);");
            }
        }
        source.Append("""

                // An array's exact type, by the filters' element types.
                private static int ArrayKind(object value)
                {
                    var type = value.GetType();
                    return type == typeof(LeafAttribute[]) ? 1 : type == typeof(TagAttribute[]) ? 2 :
                        type == typeof(Attribute[]) ? 3 : type == typeof(InfoAttribute<int>[]) ? 4 :
                        type == typeof(InfoAttribute<short>[]) ? 5 : type == typeof(IMarker[]) ? 6 :
                        type == typeof(object[]) ? 7 : value is Array ? 8 : 0;
                }

                // The fixture's own attributes and SerializableAttribute count; those a compiler adds (CompilationRelaxations,
                // RefSafetyRules and the like) differ between the compiles, so they do not.
                private static int Id(object item) => item switch
                {
                    IHasId withId => withId.Id,
                    SerializableAttribute => 500,
                    _ => 0,
                };

                private static int Encode(object value)
                {
                    switch (value)
                    {
                        case null: return 0;
                        case bool flag: return flag ? 1 : 2;
                        case IEnumerable items:
                            var count = 0;
                            var sum = 0;
                            foreach (var item in items)
                            {
                                if (Id(item) == 0) continue;
                                count++;
                                sum += Id(item);
                            }
                            return ArrayKind(value) * 1_000_000 + count * 10_000 + sum;
                        default: return Id(value) is var id and not 0 ? id : -9;
                    }
                }

                private static int Code(Func<object> query)
                {
                    try { return Encode(query()); }
                    catch (AmbiguousMatchException) { return -1; }
                    catch (ArgumentException) { return -2; }
                    catch (PlatformNotSupportedException) { return -3; }
                    catch (NotSupportedException) { return -4; }
                    catch (Exception) { return -5; }
                }

                public static int[] Results() =>
                [

            """);
        foreach (var (_, expression) in queries)
        {
            source.Append("            Code(() => ").Append(expression).AppendLine("),");
        }
        source.Append("""
                ];

                public static int Run(int input)
                {
                    foreach (var result in Results()) Console.WriteLine(result);
                    return input;
                }
            }
            """);
        return source.ToString();
    }
}
