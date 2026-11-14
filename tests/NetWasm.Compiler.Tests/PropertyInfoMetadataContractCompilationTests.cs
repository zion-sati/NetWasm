using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class PropertyInfoMetadataContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PropertyOverloadsForwardAndUnavailableDefaultsReject(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("PropertyMetadataFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "PropertyMetadataFixture.EntryPoint",
            "Run", [new("run", "PropertyMetadataFixture.EntryPoint", "Run")], Target: target,
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
        using System.Globalization;
        namespace PropertyMetadataFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var provider = new Provider();
                PropertyInfo property = provider;
                if (property.MemberType != MemberTypes.Property || !property.IsSpecialName || !property.CanRead || !property.CanWrite) return 1;
                if (property.GetGetMethod() != null || property.GetSetMethod() != null || property.GetMethod != provider.Method || property.SetMethod != provider.Method) return 2;
                if (property.GetAccessors().Length != 0 || property.GetAccessors(true).Length != 1) return 3;
                property.SetValue(null, input);
                if ((int)property.GetValue(null) != input || provider.Index != null || provider.Flags != BindingFlags.Default || provider.Binder != null || provider.Culture != null) return 4;
                object[] indices = new object[] { 3 };
                property.SetValue(null, 42, indices);
                if ((int)property.GetValue(null, indices) != 42 || !ReferenceEquals(provider.Index, indices)) return 5;
                if (property.GetOptionalCustomModifiers().Length != 0 || property.GetRequiredCustomModifiers().Length != 0) return 6;
                PropertyInfo same = provider;
                if (property != same || property == null || null == property || property == new Provider() || !property.Equals((object)same) || property.GetHashCode() != same.GetHashCode()) return 7;
                var missing = new Missing();
                var caught = 0;
                try { missing.GetValue(null); } catch (PlatformNotSupportedException) { caught++; }
                try { missing.SetValue(null, 1); } catch (PlatformNotSupportedException) { caught++; }
                try { missing.GetAccessors(); } catch (PlatformNotSupportedException) { caught++; }
                try { missing.GetIndexParameters(); } catch (PlatformNotSupportedException) { caught++; }
                try { _ = missing.Attributes; } catch (PlatformNotSupportedException) { caught++; }
                try { missing.GetConstantValue(); } catch (PlatformNotSupportedException) { caught++; }
                try { missing.GetRawConstantValue(); } catch (PlatformNotSupportedException) { caught++; }
                Expression<Func<int>> expression = () => Holder.Value;
                var runtime = (PropertyInfo)((MemberExpression)expression.Body).Member;
                if (!runtime.CanRead || runtime.CanWrite || runtime.PropertyType != typeof(int) || runtime.GetMethod == null) return 8;
                try { runtime.GetOptionalCustomModifiers(); } catch (PlatformNotSupportedException) { caught++; }
                try { runtime.GetRequiredCustomModifiers(); } catch (PlatformNotSupportedException) { caught++; }
                return caught == 9 ? input + 1 : 9;
            }
            private sealed class Missing : PropertyInfo { }
            private sealed class Method : MethodInfo { }
            private sealed class Provider : PropertyInfo
            {
                public readonly MethodInfo Method = new Method();
                public object Value; public object[] Index; public BindingFlags Flags; public Binder Binder; public CultureInfo Culture;
                public override PropertyAttributes Attributes => PropertyAttributes.SpecialName;
                public override MethodInfo GetGetMethod(bool nonPublic) => nonPublic ? Method : null;
                public override MethodInfo GetSetMethod(bool nonPublic) => nonPublic ? Method : null;
                public override MethodInfo[] GetAccessors(bool nonPublic) => nonPublic ? new[] { Method } : Array.Empty<MethodInfo>();
                public override object GetValue(object target, BindingFlags flags, Binder binder, object[] index, CultureInfo culture)
                { Flags = flags; Binder = binder; Index = index; Culture = culture; return Value; }
                public override void SetValue(object target, object value, BindingFlags flags, Binder binder, object[] index, CultureInfo culture)
                { Value = value; Flags = flags; Binder = binder; Index = index; Culture = culture; }
            }
            private static class Holder { public static int Value => 42; }
        }
        """;
}
