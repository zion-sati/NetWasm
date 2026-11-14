using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ParameterInfoMetadataContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ParameterDescriptorFieldsAndRuntimeBoundaryRemainDistinct(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ParameterMetadataFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ParameterMetadataFixture.EntryPoint",
            "Run", [new("run", "ParameterMetadataFixture.EntryPoint", "Run")], Target: target,
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
        namespace ParameterMetadataFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var member = new Member();
                var supplied = new Supplied(member);
                if (supplied.ParameterType != typeof(int) || supplied.Name != "value" || supplied.Position != 2 || !ReferenceEquals(supplied.Member, member)) return 1;
                if (!supplied.IsIn || !supplied.IsOut || !supplied.IsOptional || !supplied.IsRetval || !supplied.IsLcid) return 2;
                if ((int)supplied.StoredDefault != 42) return 3;
                if (supplied.MetadataToken != 0x08000000 || supplied.IsDefined(typeof(Attribute), false)) return 7;
                if (supplied.GetCustomAttributes(false).Length != 0 || supplied.GetCustomAttributes(typeof(Attribute), true).Length != 0) return 8;
                if (supplied.GetOptionalCustomModifiers().Length != 0 || supplied.GetRequiredCustomModifiers().Length != 0) return 9;
                var rejected = 0;
                try { supplied.IsDefined(null, false); } catch (ArgumentNullException) { rejected++; }
                try { supplied.GetCustomAttributes(null, false); } catch (ArgumentNullException) { rejected++; }
                try { _ = supplied.DefaultValue; } catch (PlatformNotSupportedException) { rejected++; }
                try { _ = supplied.RawDefaultValue; } catch (PlatformNotSupportedException) { rejected++; }
                try { _ = supplied.HasDefaultValue; } catch (PlatformNotSupportedException) { rejected++; }
                try { ((System.Runtime.Serialization.IObjectReference)supplied).GetRealObject(default); } catch (PlatformNotSupportedException) { rejected++; }
                try { supplied.ToString(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) rejected++; }
                if (rejected != 7) return 10;
                supplied.Clear();
                if (supplied.IsIn || supplied.IsOut || supplied.IsOptional || supplied.IsRetval || supplied.IsLcid) return 4;
                Expression<Action<int>> expression = value => Accept(value);
                var runtime = ((MethodCallExpression)expression.Body).Method.GetParameters()[0];
                if (runtime.ParameterType != typeof(int)) return 5;
                var caught = 0;
                try { _ = runtime.Name; } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = runtime.Position; } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = runtime.Member; } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = runtime.Attributes; } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = runtime.MetadataToken; } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { runtime.IsDefined(typeof(Attribute), false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { runtime.GetCustomAttributes(false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { runtime.GetCustomAttributes(typeof(Attribute), false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { runtime.GetOptionalCustomModifiers(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { runtime.GetRequiredCustomModifiers(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                return caught == 10 ? input + 1 : 6;
            }
            public static void Accept(int value) { }
            private sealed class Member : MemberInfo { }
            private sealed class Supplied : ParameterInfo
            {
                public Supplied(MemberInfo member)
                {
                    AttrsImpl = ParameterAttributes.In | ParameterAttributes.Out | ParameterAttributes.Optional | ParameterAttributes.Retval | ParameterAttributes.Lcid;
                    ClassImpl = typeof(int); MemberImpl = member; NameImpl = "value"; PositionImpl = 2; DefaultValueImpl = 42;
                }
                public object StoredDefault => DefaultValueImpl;
                public void Clear() { AttrsImpl = ParameterAttributes.None; }
            }
        }
        """;
}
