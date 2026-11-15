// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class AttributeRetentionCompilationTests
{
    [Theory]
    [InlineData(0, WasmTarget.Wasm32)]
    [InlineData(0, WasmTarget.Wasm64)]
    [InlineData(1, WasmTarget.Wasm32)]
    [InlineData(1, WasmTarget.Wasm64)]
    [InlineData(2, WasmTarget.Wasm32)]
    [InlineData(2, WasmTarget.Wasm64)]
    [InlineData(3, WasmTarget.Wasm32)]
    [InlineData(3, WasmTarget.Wasm64)]
    [InlineData(4, WasmTarget.Wasm32)]
    [InlineData(4, WasmTarget.Wasm64)]
    public void RetainsOnlySelectedMaterializationAndIndependentExecutableRoots(int mode, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var action = mode switch
        {
            0 => "return input;",
            1 => "return typeof(Derived).IsDefined(typeof(MarkerAttribute), true) ? input : 1;",
            2 => "return Check((MarkerAttribute[])typeof(Derived).GetCustomAttributes(typeof(MarkerAttribute), false), false, input);",
            3 => "return Check((MarkerAttribute[])typeof(Derived).GetCustomAttributes(typeof(MarkerAttribute), input > 0), input > 0, input);",
            _ => "return new DirectAttribute().Value == 3 ? input : 1;",
        };
        var source = Source.Replace("// QUERY", action, StringComparison.Ordinal);
        var assembly = assets.CompileSource("AttributeDependencyRetention", source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "AttributeDependencyRetention.EntryPoint", "Run", [], Target: target));
        var methods = compilation.Program.MethodInstances.Values.ToArray();
        Assert.Contains(methods, method => method.DeclaringType.FullName == "AttributeDependencyRetention.EntryPoint" &&
            method.Definition.Name == "Run");
        Assert.Equal(mode is 2 or 3 or 4, methods.Any(method =>
            method.DeclaringType.FullName == "AttributeDependencyRetention.DirectDependency"));
        Assert.Equal(mode is 2 or 3, methods.Any(method =>
            method.DeclaringType.FullName == "AttributeDependencyRetention.SetterDependency"));
        Assert.Equal(mode == 3, methods.Any(method =>
            method.DeclaringType.FullName == "AttributeDependencyRetention.InheritedDependency"));
        Assert.DoesNotContain(methods, method => method.DeclaringType.FullName is
            "AttributeDependencyRetention.UnrelatedAttribute" or "AttributeDependencyRetention.UnrelatedDependency");
        Assert.Equal(mode is 2 or 3, methods.Any(method => method.Definition.Name == "ApplySetter" &&
            method.DeclaringType.FullName == "System.Runtime.CompilerServices.RuntimeCustomAttributes"));
        Assert.DoesNotContain(methods, method =>
            method.DeclaringType.FullName == "System.Runtime.CompilerServices.RuntimeCustomAttributes" &&
            method.Definition.Name is "GetOne" or "GetMany" or "IsDefined" or "Unsupported");
        Assert.DoesNotContain("unrelated-payload", compilation.Program.StringLiterals);
        if (mode is 0 or 1)
        {
            Assert.DoesNotContain(methods, method => method.DeclaringType.FullName is
                "AttributeDependencyRetention.DirectAttribute" or "AttributeDependencyRetention.InheritedAttribute");
        }

        Assert.Equal(42, ExecuteWithStandardWasiNode(compilation.ApplicationModule, assets.Directory,
            42, target, compilation.StaticDataEnd));
        if (mode == 3)
        {
            Assert.Equal(-42, ExecuteWithStandardWasiNode(compilation.ApplicationModule, assets.Directory,
                -42, target, compilation.StaticDataEnd));
        }
    }

    private const string Source = """
        using System;
        using System.Reflection;
        namespace AttributeDependencyRetention;

        public abstract class MarkerAttribute : Attribute { public int Value; }
        public sealed class DirectAttribute : MarkerAttribute
        {
            public DirectAttribute() => Value = DirectDependency.Read();
            public int Label { get => Value; set => Value += SetterDependency.Read(value); }
        }
        public sealed class InheritedAttribute : MarkerAttribute
        {
            public InheritedAttribute() => Value = InheritedDependency.Read();
        }
        public sealed class UnrelatedAttribute : Attribute
        {
            public UnrelatedAttribute(string text)
            {
                _ = UnrelatedDependency.Read();
                // This unsupported query must remain unreachable when the
                // containing attribute is not selected for materialization.
                _ = typeof(Derived).GetCustomAttributes<Attribute>();
            }
        }
        // Each dependency reads state, so constant call folding cannot replace the
        // call that proves its attribute was materialized.
        public static class DirectDependency { public static int Value = 3; public static int Read() => Value; }
        public static class SetterDependency { public static int Offset; public static int Read(int value) => value + Offset; }
        public static class InheritedDependency { public static int Value = 7; public static int Read() => Value; }
        public static class UnrelatedDependency { public static int Read() => 100; }
        [Inherited] public class Base;
        [Direct(Label = 5)] public class Derived : Base;
        [Unrelated("unrelated-payload")] public class Unrelated;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                // Runtime type roots alone must not bring their attribute bodies.
                if (typeof(Unrelated) == typeof(Derived)) return 2;
                // QUERY
            }
            private static int Check(MarkerAttribute[] attributes, bool inherit, int input)
            {
                var sum = 0;
                foreach (var attribute in attributes) sum += attribute.Value;
                return attributes.Length == (inherit ? 2 : 1) && sum == (inherit ? 15 : 8) ? input : 3;
            }
        }
        """;
}
