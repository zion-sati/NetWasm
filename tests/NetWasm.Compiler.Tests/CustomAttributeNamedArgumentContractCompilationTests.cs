using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class CustomAttributeNamedArgumentContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void NamedAttributeValuesPreserveUpstreamSemantics(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("NamedAttributeFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "NamedAttributeFixture.EntryPoint",
            "Run", [new("run", "NamedAttributeFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        namespace NamedAttributeFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var field = new Field();
                object boxed = 42;
                var first = new CustomAttributeNamedArgument(field, boxed);
                var same = new CustomAttributeNamedArgument(field, new CustomAttributeTypedArgument(typeof(int), boxed));
                if (!ReferenceEquals(first.MemberInfo, field) || first.MemberName != "Number" || !first.IsField) return 1;
                if (first.TypedValue.ArgumentType != typeof(int) || !ReferenceEquals(first.TypedValue.Value, boxed)) return 2;
                if (first.ToString() != "Number = 42") return 3;
                if (first != same || !first.Equals((object)same) || first.Equals(null) || first.Equals(new object())) return 4;
                if (first.GetHashCode() != same.GetHashCode()) return 5;
                var property = new Property();
                var text = new CustomAttributeNamedArgument(property, "abc");
                if (text.IsField || text.MemberName != "Text" || text.ToString() != "Text = \"abc\"") return 6;
                if (first == text) return 7;
                try { new CustomAttributeNamedArgument(null!, boxed); return 8; } catch (ArgumentNullException e) { if (e.ParamName != "memberInfo") return 9; }
                try { new CustomAttributeNamedArgument(null!, default(CustomAttributeTypedArgument)); return 10; } catch (ArgumentNullException e) { if (e.ParamName != "memberInfo") return 11; }
                try { new CustomAttributeNamedArgument(new Other(), boxed); return 12; } catch (ArgumentException) { }
                var other = new Other();
                var accepted = new CustomAttributeNamedArgument(other, new CustomAttributeTypedArgument(boxed));
                if (!ReferenceEquals(accepted.MemberInfo, other) || accepted.IsField) return 13;
                return input + 1;
            }
            private sealed class Field : FieldInfo
            {
                public override string Name => "Number";
                public override Type FieldType => typeof(int);
            }
            private sealed class Property : PropertyInfo
            {
                public override string Name => "Text";
                public override Type PropertyType => typeof(string);
            }
            private sealed class Other : MemberInfo { }
        }
        """;
}
