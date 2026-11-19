// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// A reduction that reaches new code (an attribute query on a Type known only at run time constructs the attributes the
// program's types carry) adds call sites to methods an optimization may already have specialized on the constants
// their earlier callers passed: an attribute's constructor called directly with one value, then by the query with
// another, and a flag test called with one flag, then by the type queries the attribute lookup reads with others.
// Each answer is compared with desktop .NET.
public sealed class GrowingReductionOrderCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void CallSitesALaterReductionReachesKeepTheirArguments() =>
        runner.Run(new("GrowingReductionOrder", "GrowingReductionOrder", Source, [0, 1, -1])
        {
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
        using System;
        namespace GrowingReductionOrder;

        [AttributeUsage(AttributeTargets.Class)]
        public sealed class TagAttribute : Attribute
        {
            public readonly int Value;
            public TagAttribute(int value) { Value = value; }
        }

        [AttributeUsage(AttributeTargets.Class)]
        public sealed class NameAttribute : Attribute
        {
            public readonly string Name;
            public NameAttribute(string name) { Name = name; }
        }

        [Tag(7), Name("seven")] public sealed class Seven { }
        [Tag(11)] public sealed class Eleven { }
        public sealed class Plain { }

        public static class EntryPoint
        {
            private static int _trace;

            public static int Trace() => _trace;

            public static int Run(int input)
            {
                // The only direct calls pass 5 and "five".
                var direct = new TagAttribute(5).Value * 1000 + new NameAttribute("five").Name.Length;
                Type type = input > 0 ? typeof(Seven) : input == 0 ? typeof(Eleven) : typeof(Plain);
                var tag = (TagAttribute?)Attribute.GetCustomAttribute(type, typeof(TagAttribute));
                var name = (NameAttribute?)Attribute.GetCustomAttribute(type, typeof(NameAttribute));
                var hash = direct * 31 + (tag?.Value ?? -1) * 7 + (name?.Name.Length ?? -1) + (type.IsInterface ? 100 : 0);
                _trace = hash;
                return hash;
            }
        }
        """;
}
