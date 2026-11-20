// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// typeof of an open generic class whose closed base the program never names or constructs: the definition's run-time
// type names its base, as typeof(TagAttribute<>).BaseType is Attribute on desktop .NET, even where no attribute exists.
// The build used to stop with NW1008, the base having no layout for the definition's type descriptor to name.
public sealed class GenericDefinitionBaseCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void AnOpenGenericDefinitionsClosedBaseIsItsRunTimeBase() => runner.Run(new(
        "GenericDefinitionBase", "GenericDefinitionBase", Source, [0, 1])
    {
        ExecuteWasm64 = true,
        ExecuteOptimizedWasm = true,
        ReportAllMismatches = true,
    });

    private const string Source = """
        using System;
        namespace GenericDefinitionBase;

        // Neither base is named or constructed anywhere else in the program.
        public class TagAttribute<T> : Attribute;
        public class Shape;
        public sealed class Square<T> : Shape;

        public static class EntryPoint
        {
            private static int _trace;

            public static int Trace() => _trace;

            public static int Run(int input)
            {
                var hash = 17;
                var definition = input == 0 ? typeof(TagAttribute<>) : typeof(Square<>);
                var baseType = definition.BaseType;
                hash = hash * 31 + (baseType is null ? 1 : 2);
                hash = hash * 31 + (baseType is not null && baseType != typeof(object) && baseType.BaseType == typeof(object) ? 3 : 4);
                hash = hash * 31 + (definition == typeof(TagAttribute<>) ? 5 : 6);
                _trace = hash;
                return hash;
            }
        }
        """;
}
