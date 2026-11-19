// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// An attribute query whose filter is an open generic attribute type (typeof(TagAttribute<>)) finds every attribute
// whose type, or a base of it, is a constructed instance of that definition, and returns them in an Attribute[]:
// on a type known at compile time or only at run time, with and without inheritance, through the static Attribute
// methods and the extension methods, and where the answer is cast to an interface the attribute implements, as
// .NET's COM source-generator support reads IUnknownDerivedAttribute<,>.
public sealed class OpenGenericAttributeQueryTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void OpenGenericAttributeFiltersMatchEveryConstructedInstanceAsOnDesktop() => runner.Run(new(
        "OpenGenericAttributeQuery", "OpenGenericAttributeQuery", Source, [0, 1, 2])
    {
        ExecuteWasm64 = true,
        ExecuteOptimizedWasm = true,
        ReportAllMismatches = true,
    });

    private const string Source = """
        using System;
        using System.Reflection;

        [assembly: OpenGenericAttributeQuery.Tag<short>(11)]
        [assembly: OpenGenericAttributeQuery.Info<int>]
        [assembly: OpenGenericAttributeQuery.AssemblyMarker]

        namespace OpenGenericAttributeQuery;

        public interface ITagInfo
        {
            int Value { get; }
        }

        [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
        public class TagAttribute<T>(int value) : Attribute, ITagInfo
        {
            public int Value { get; } = value;
        }

        public sealed class IntTagAttribute(int value) : TagAttribute<int>(value);

        [AttributeUsage(AttributeTargets.All, Inherited = false)]
        public sealed class InfoAttribute<T> : Attribute, ITagInfo
        {
            public int Value => 9;
        }

        [AttributeUsage(AttributeTargets.All)]
        public sealed class AssemblyMarkerAttribute : Attribute, ITagInfo
        {
            public int Value => 13;
        }

        [AttributeUsage(AttributeTargets.All)]
        public sealed class PairAttribute<TKey, TValue> : Attribute
        {
            public int Arity => 2;
        }

        [Tag<int>(1), Tag<string>(2)] public class Many;
        [IntTag(3)] public class Derived;
        [Tag<long>(4)] public class Base;
        public class Child : Base;
        [Info<byte>] public interface IMarked;
        [Info<int>] public class NonInheritedBase;
        public class NonInheritedChild : NonInheritedBase;
        [Pair<int, string>] public class Pair;
        public class Plain;

        public static class EntryPoint
        {
            private static int _trace;

            public static int Trace() => _trace;

            private static int Sum(Attribute[] attributes)
            {
                var sum = 0;
                foreach (var attribute in attributes) sum = sum * 31 + ((ITagInfo)attribute).Value;
                return sum;
            }

            private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

            public static int Run(int input)
            {
                var hash = 17;
                // Compile-time targets.
                hash = Mix(hash, typeof(Many).IsDefined(typeof(TagAttribute<>), false) ? 1 : 2);
                hash = Mix(hash, typeof(Plain).IsDefined(typeof(TagAttribute<>), true) ? 3 : 4);
                hash = Mix(hash, Sum((Attribute[])typeof(Many).GetCustomAttributes(typeof(TagAttribute<>), false)));
                hash = Mix(hash, typeof(Many).GetCustomAttributes(typeof(TagAttribute<>), false).Length);
                hash = Mix(hash, ((ITagInfo?)typeof(Derived).GetCustomAttribute(typeof(TagAttribute<>)))?.Value ?? -1);
                hash = Mix(hash, Sum(Attribute.GetCustomAttributes(typeof(Child), typeof(TagAttribute<>), true)));
                hash = Mix(hash, Attribute.GetCustomAttributes(typeof(Child), typeof(TagAttribute<>), false).Length);
                hash = Mix(hash, Attribute.IsDefined(typeof(NonInheritedChild), typeof(InfoAttribute<>), true) ? 5 : 6);
                hash = Mix(hash, ((ITagInfo?)Attribute.GetCustomAttribute(typeof(IMarked), typeof(InfoAttribute<>)))?.Value ?? -1);
                hash = Mix(hash, typeof(Pair).GetCustomAttribute(typeof(PairAttribute<,>)) is PairAttribute<int, string> pair
                    ? pair.Arity : -1);
                // A closed filter still matches only its own instance.
                hash = Mix(hash, typeof(Many).GetCustomAttributes(typeof(TagAttribute<int>), false).Length);
                try
                {
                    hash = Mix(hash, typeof(Many).GetCustomAttribute(typeof(TagAttribute<>)) is null ? 7 : 8);
                }
                catch (AmbiguousMatchException)
                {
                    hash = Mix(hash, 9);
                }

                // An assembly's own attributes: a sealed attribute among generic ones, and an open sealed generic one.
                var assembly = typeof(EntryPoint).Assembly;
                hash = Mix(hash, Attribute.GetCustomAttributes(assembly, typeof(AssemblyMarkerAttribute)).Length);
                hash = Mix(hash, ((ITagInfo?)Attribute.GetCustomAttribute(assembly, typeof(InfoAttribute<>)))?.Value ?? -1);
                hash = Mix(hash, Attribute.IsDefined(assembly, typeof(InfoAttribute<>)) ? 14 : 15);

                // Run-time targets, as the COM support reads them from a type handle.
                var handle = input switch
                {
                    0 => typeof(Many).TypeHandle,
                    1 => typeof(IMarked).TypeHandle,
                    _ => typeof(Plain).TypeHandle,
                };
                var type = Type.GetTypeFromHandle(handle)!;
                hash = Mix(hash, type.IsDefined(typeof(TagAttribute<>), true) ? 10 : 11);
                hash = Mix(hash, Sum((Attribute[])type.GetCustomAttributes(typeof(TagAttribute<>), true)));
                hash = Mix(hash, ((ITagInfo?)type.GetCustomAttribute(typeof(InfoAttribute<>)))?.Value ?? -1);
                hash = Mix(hash, type.GetCustomAttributes(typeof(TagAttribute<>), true) is Attribute[] ? 12 : 13);
                _trace = hash;
                return hash;
            }
        }
        """;
}
