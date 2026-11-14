using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeTargetContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AssemblyModuleAndParameterQueriesHaveRuntimeFallbacks(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("AttributeTargetFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "AttributeTargetFixture.EntryPoint",
            "Run", [new("run", "AttributeTargetFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        namespace AttributeTargetFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var caught = 0;
                Assembly assembly = new CustomAssembly();
                Module module = new CustomModule();
                ParameterInfo parameter = new CustomParameter();
                MemberInfo member = typeof(EntryPoint);
                try { Attribute.GetCustomAttribute(assembly, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttribute((Assembly)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttribute(assembly, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.GetCustomAttribute(assembly, typeof(Marker), false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttribute((Assembly)null, typeof(Marker), false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttribute(assembly, null, false); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.GetCustomAttributes(assembly, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttributes((Assembly)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttributes(assembly, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.GetCustomAttributes(assembly, typeof(Marker), false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttributes((Assembly)null, typeof(Marker), false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttributes(assembly, null, false); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.IsDefined(assembly, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.IsDefined((Assembly)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.IsDefined(assembly, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.IsDefined(assembly, typeof(Marker), false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.IsDefined((Assembly)null, typeof(Marker), false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.IsDefined(assembly, null, false); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.GetCustomAttribute(module, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttribute((Module)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttribute(module, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.GetCustomAttribute(module, typeof(Marker), false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttribute((Module)null, typeof(Marker), false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttribute(module, null, false); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.GetCustomAttributes(module, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttributes((Module)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttributes(module, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.GetCustomAttributes(module, typeof(Marker), false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttributes((Module)null, typeof(Marker), false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttributes(module, null, false); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.IsDefined(module, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.IsDefined((Module)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.IsDefined(module, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.IsDefined(module, typeof(Marker), false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.IsDefined((Module)null, typeof(Marker), false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.IsDefined(module, null, false); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.GetCustomAttribute(parameter, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttribute((ParameterInfo)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttribute(parameter, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.GetCustomAttribute(parameter, typeof(Marker), false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttribute((ParameterInfo)null, typeof(Marker), false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttribute(parameter, null, false); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.GetCustomAttributes(parameter, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttributes((ParameterInfo)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttributes(parameter, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.GetCustomAttributes(parameter, typeof(Marker), false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttributes((ParameterInfo)null, typeof(Marker), false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttributes(parameter, null, false); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.IsDefined(parameter, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.IsDefined((ParameterInfo)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.IsDefined(parameter, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.IsDefined(parameter, typeof(Marker), false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.IsDefined((ParameterInfo)null, typeof(Marker), false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.IsDefined(parameter, null, false); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { Attribute.GetCustomAttributes(assembly); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttributes((Assembly)null); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttributes(assembly, false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttributes((Assembly)null, false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttributes(module); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttributes((Module)null); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttributes(module, false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttributes((Module)null, false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttributes(parameter); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttributes((ParameterInfo)null); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttributes(parameter, false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttributes((ParameterInfo)null, false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttributes(member); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttributes((MemberInfo)null); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { Attribute.GetCustomAttributes(member, false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Attribute.GetCustomAttributes((MemberInfo)null, false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute(assembly, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute((Assembly)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute(assembly, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute(module, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute((Module)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute(module, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute(parameter, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute((ParameterInfo)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute(parameter, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute<Marker>(assembly); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute<Marker>((Assembly)null); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute<Marker>(module); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute<Marker>((Module)null); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute<Marker>(parameter); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute<Marker>((ParameterInfo)null); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute(parameter, typeof(Marker), false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute((ParameterInfo)null, typeof(Marker), false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute(parameter, null, false); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute<Marker>(parameter, false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttribute<Marker>((ParameterInfo)null, false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes(assembly); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes((Assembly)null); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes(module); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes((Module)null); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes(parameter); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes((ParameterInfo)null); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes(parameter, false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes((ParameterInfo)null, false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes(assembly, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes((Assembly)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes(assembly, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes(module, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes((Module)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes(module, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes(parameter, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes((ParameterInfo)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes(parameter, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes<Marker>(assembly); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes<Marker>((Assembly)null); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes<Marker>(module); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes<Marker>((Module)null); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes<Marker>(parameter); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes<Marker>((ParameterInfo)null); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes(parameter, typeof(Marker), false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes((ParameterInfo)null, typeof(Marker), false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes(parameter, null, false); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes<Marker>(parameter, false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes<Marker>((ParameterInfo)null, false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.IsDefined(assembly, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.IsDefined((Assembly)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.IsDefined(assembly, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { CustomAttributeExtensions.IsDefined(module, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.IsDefined((Module)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.IsDefined(module, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { CustomAttributeExtensions.IsDefined(parameter, typeof(Marker)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.IsDefined((ParameterInfo)null, typeof(Marker)); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.IsDefined(parameter, null); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                try { CustomAttributeExtensions.IsDefined(parameter, typeof(Marker), false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.IsDefined((ParameterInfo)null, typeof(Marker), false); } catch (ArgumentNullException e) { if (e.ParamName == "element") caught++; }
                try { CustomAttributeExtensions.IsDefined(parameter, null, false); } catch (ArgumentNullException e) { if (e.ParamName == "attributeType") caught++; }
                return caught == 130 ? input + 1 : 1;
            }
            private sealed class CustomAssembly : Assembly { }
            private sealed class CustomModule : Module { }
            private sealed class CustomParameter : ParameterInfo { }
            private sealed class Marker : Attribute { }
        }
        """;
}
