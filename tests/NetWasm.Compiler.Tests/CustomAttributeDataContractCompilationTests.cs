using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class CustomAttributeDataContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AttributeDataPreservesProviderAndFormattingContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("AttributeDataFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "AttributeDataFixture.EntryPoint",
            "Run", [new("run", "AttributeDataFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Reflection;
        namespace AttributeDataFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var data = new Data();
                if (!data.Equals(data) || data.Equals(new Data()) || data.Equals(null)) return 18;
                if (data.GetHashCode() != ((object)data).GetHashCode()) return 19;
                var list = new CustomAttributeData[] { data };
                var member = new Member { Items = list };
                var module = new Mod { Items = list };
                var assembly = new Asm { Items = list };
                var parameter = new Param { Items = list };
                if (!ReferenceEquals(CustomAttributeData.GetCustomAttributes(member), list)) return 1;
                if (!ReferenceEquals(CustomAttributeData.GetCustomAttributes(module), list)) return 2;
                if (!ReferenceEquals(CustomAttributeData.GetCustomAttributes(assembly), list)) return 3;
                if (!ReferenceEquals(CustomAttributeData.GetCustomAttributes(parameter), list)) return 4;
                if (!ReferenceEquals(member.CustomAttributes, list) || !ReferenceEquals(module.CustomAttributes, list) ||
                    !ReferenceEquals(assembly.CustomAttributes, list) || !ReferenceEquals(parameter.CustomAttributes, list)) return 5;
                if (data.AttributeType != typeof(Marker)) return 6;
                // The text names the types as the program reads their names (#225).
                if (data.ToString() != "[" + typeof(Marker).FullName + "((" + typeof(int).Name + ")42, Text = \"abc\")]") return 7;
                data.Empty = true;
                if (data.ToString() != "[" + typeof(Marker).FullName + "()]") return 8;
                try { CustomAttributeData.GetCustomAttributes((MemberInfo)null!); return 9; } catch (ArgumentNullException e) { if (e.ParamName != "target") return 10; }
                try { CustomAttributeData.GetCustomAttributes((Module)null!); return 11; } catch (ArgumentNullException e) { if (e.ParamName != "target") return 12; }
                try { CustomAttributeData.GetCustomAttributes((Assembly)null!); return 13; } catch (ArgumentNullException e) { if (e.ParamName != "target") return 14; }
                try { CustomAttributeData.GetCustomAttributes((ParameterInfo)null!); return 15; } catch (ArgumentNullException e) { if (e.ParamName != "target") return 16; }
                var caught = 0;
                try { new MissingMember().GetCustomAttributesData(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { new MissingModule().GetCustomAttributesData(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { new MissingAssembly().GetCustomAttributesData(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { new MissingParameter().GetCustomAttributesData(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                if (caught != 4) return 17;
                return input + 1;
            }
            private sealed class Member : MemberInfo { public IList<CustomAttributeData> Items; public override IList<CustomAttributeData> GetCustomAttributesData() => Items; }
            private sealed class Mod : Module { public IList<CustomAttributeData> Items; public override IList<CustomAttributeData> GetCustomAttributesData() => Items; }
            private sealed class Asm : Assembly { public IList<CustomAttributeData> Items; public override IList<CustomAttributeData> GetCustomAttributesData() => Items; }
            private sealed class Param : ParameterInfo { public IList<CustomAttributeData> Items; public override IList<CustomAttributeData> GetCustomAttributesData() => Items; }
            private sealed class MissingMember : MemberInfo { }
            private sealed class MissingModule : Module { }
            private sealed class MissingAssembly : Assembly { }
            private sealed class MissingParameter : ParameterInfo { }
            private sealed class Ctor : ConstructorInfo { public override Type DeclaringType => typeof(Marker); }
            private sealed class Field : FieldInfo { public override string Name => "Text"; public override Type FieldType => typeof(string); }
            private sealed class Data : CustomAttributeData
            {
                public bool Empty;
                public override ConstructorInfo Constructor => new Ctor();
                public override IList<CustomAttributeTypedArgument> ConstructorArguments => Empty ? Array.Empty<CustomAttributeTypedArgument>() : new[] { new CustomAttributeTypedArgument(42) };
                public override IList<CustomAttributeNamedArgument> NamedArguments => Empty ? Array.Empty<CustomAttributeNamedArgument>() : new[] { new CustomAttributeNamedArgument(new Field(), "abc") };
            }
        }
        public sealed class Marker : Attribute { }
        """;
}
