using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class FieldInfoMetadataContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void FieldFlagsEqualityAndRuntimeDescriptorsRemainCompatible(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("FieldMetadataFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "FieldMetadataFixture.EntryPoint",
            "Run", [new("run", "FieldMetadataFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        using System.Linq.Expressions;
        namespace FieldMetadataFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var field = new Field();
                var accesses = new[] { FieldAttributes.Assembly, FieldAttributes.Family, FieldAttributes.FamANDAssem, FieldAttributes.FamORAssem, FieldAttributes.Private, FieldAttributes.Public };
                for (var i = 0; i < accesses.Length; i++)
                {
                    field.Flags = accesses[i];
                    if (field.IsAssembly != (i == 0) || field.IsFamily != (i == 1) || field.IsFamilyAndAssembly != (i == 2) || field.IsFamilyOrAssembly != (i == 3) || field.IsPrivate != (i == 4) || field.IsPublic != (i == 5)) return 1;
                }
                field.Flags = FieldAttributes.Static | FieldAttributes.InitOnly | FieldAttributes.Literal | FieldAttributes.NotSerialized | FieldAttributes.PinvokeImpl | FieldAttributes.SpecialName;
                if (!field.IsStatic || !field.IsInitOnly || !field.IsLiteral || !field.IsNotSerialized || !field.IsPinvokeImpl || !field.IsSpecialName) return 2;
                field.Flags = 0;
                if (field.IsStatic || field.IsInitOnly || field.IsLiteral || field.IsNotSerialized || field.IsPinvokeImpl || field.IsSpecialName) return 3;
                if (field.MemberType != MemberTypes.Field || !field.IsSecurityCritical || field.IsSecuritySafeCritical || field.IsSecurityTransparent) return 4;
                FieldInfo same = field;
                FieldInfo other = new Field();
                FieldInfo missing = null;
                if (field != same || field == other || field == null || null == field || missing != null) return 5;
                if (!field.Equals((object)same) || field.GetHashCode() != same.GetHashCode()) return 6;
                Expression<Func<int>> expression = () => Holder.Number;
                var runtime = (FieldInfo)((MemberExpression)expression.Body).Member;
                if (!runtime.IsStatic || !runtime.IsInitOnly || runtime.IsLiteral || runtime.FieldType != typeof(int)) return 7;
                try { _ = runtime.Attributes; return 8; } catch (PlatformNotSupportedException e) { if (e.Message.Length == 0) return 9; }
                var rejected = 0;
                try { _ = field.FieldHandle; } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) rejected++; }
                try { field.GetValue(null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) rejected++; }
                try { field.SetValue(null, 1); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) rejected++; }
                try { field.SetValue(null, 1, BindingFlags.Default, null, null); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) rejected++; }
                try { field.GetRawConstantValue(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) rejected++; }
                try { field.GetOptionalCustomModifiers(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) rejected++; }
                try { field.GetRequiredCustomModifiers(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) rejected++; }
                if (rejected != 7) return 10;
                var provider = new AccessField();
                FieldInfo abstractProvider = provider;
                abstractProvider.SetValue(null, input, BindingFlags.Default, null, null);
                if ((int)abstractProvider.GetValue(null) != input || (int)abstractProvider.GetRawConstantValue() != 42) return 11;
                return input + 1;
            }
            private sealed class AccessField : FieldInfo
            {
                private object value;
                public override object GetValue(object target) => value;
                public override void SetValue(object target, object value, BindingFlags flags, Binder binder, System.Globalization.CultureInfo culture) { this.value = value; }
                public override object GetRawConstantValue() => 42;
            }
            private sealed class Field : FieldInfo
            {
                public FieldAttributes Flags;
                public override FieldAttributes Attributes => Flags;
            }
            private static class Holder { public static readonly int Number = 42; }
        }
        """;
}
