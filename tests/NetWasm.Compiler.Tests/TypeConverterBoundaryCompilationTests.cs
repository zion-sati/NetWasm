using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TypeConverterBoundaryCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DescriptorDiscoveryAndOverridesRespectBoundaries(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ConverterFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ConverterFixture.DerivedConverter",
            "Run", [new("run", "ConverterFixture.DerivedConverter", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.ComponentModel;
        using System.ComponentModel.Design.Serialization;
        using System.Globalization;
        namespace ConverterFixture;
        public sealed class Item { public int Value; }
        public sealed partial class DerivedConverter : TypeConverter
        {
            private sealed class Simple : SimplePropertyDescriptor
            {
                public Simple() : base(typeof(Item), "value", typeof(int), new Attribute[] { ReadOnlyAttribute.No, new DefaultValueAttribute(7) }) { }
                public override object GetValue(object component) => ((Item)component).Value;
                public override void SetValue(object component, object value) { ((Item)component).Value = (int)value; }
                public object Construct(Type type) => CreateInstance(type);
                public Type Resolve(string name) => GetTypeFromName(name);
                public object Target(Type type, object instance) => GetInvocationTarget(type, instance);
            }
        }
        public sealed partial class DerivedConverter
        {
            public static int Run(int input)
            {
                var property = new DerivedConverter.Simple();
                var item = new Item { Value = 7 };
                if (property.IsReadOnly || property.ComponentType != typeof(Item) || property.PropertyType != typeof(int)) return 1;
                if (!property.CanResetValue(item) || property.ShouldSerializeValue(item)) return 2;
                item.Value = 8; if (property.CanResetValue(item)) return 3;
                property.ResetValue(item); if (item.Value != 7) return 4;
                if (property.Resolve(null) != null || property.Resolve("") != null) return 5;
                var caught = 0;
                try { var result = property.Converter; } catch (PlatformNotSupportedException e) { if (!string.IsNullOrEmpty(e.Message)) caught++; }
                try { property.GetChildProperties(); } catch (PlatformNotSupportedException) { caught++; }
                try { property.GetChildProperties(new Attribute[0]); } catch (PlatformNotSupportedException) { caught++; }
                try { property.GetChildProperties(item); } catch (PlatformNotSupportedException) { caught++; }
                try { property.GetChildProperties(item, null); } catch (PlatformNotSupportedException) { caught++; }
                try { property.GetEditor(typeof(object)); } catch (PlatformNotSupportedException) { caught++; }
                try { property.Construct(typeof(Item)); } catch (PlatformNotSupportedException) { caught++; }
                try { property.Resolve("Item"); } catch (PlatformNotSupportedException) { caught++; }
                try { property.Target(typeof(Item), item); } catch (PlatformNotSupportedException) { caught++; }
                try { property.Target(null, item); } catch (ArgumentNullException e) { if (e.ParamName == "type") caught++; }
                try { property.Target(typeof(Item), null); } catch (ArgumentNullException e) { if (e.ParamName == "instance") caught++; }
                return caught == 11 ? input + 1 : 100 + caught;
            }
        }
        """;
}
