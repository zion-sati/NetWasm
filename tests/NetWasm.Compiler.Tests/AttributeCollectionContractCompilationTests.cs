using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeCollectionContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ExplicitAttributeCollectionsRetainManagedBehavior(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("AttributeCollectionFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "AttributeCollectionFixture.EntryPoint",
            "Run", [new("run", "AttributeCollectionFixture.EntryPoint", "Run")], Target: target,
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
        namespace AttributeCollectionFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var display = new DerivedDisplay("label");
                var attributes = new AttributeCollection(BrowsableAttribute.Yes, display);
                if (attributes.Count != 2 || attributes[0] != BrowsableAttribute.Yes || attributes[1] != display) return 1;
                for (var i = 0; i < 8; i++)
                    if (attributes[typeof(BrowsableAttribute)] != BrowsableAttribute.Yes || attributes[typeof(DisplayNameAttribute)] != display) return 2;
                if (!attributes.Contains(new BrowsableAttribute(true)) || attributes.Contains(new BrowsableAttribute(false)) || attributes.Contains((Attribute)null) || !attributes.Contains((Attribute[])null)) return 3;
                if (!attributes.Matches(new BrowsableAttribute(true)) || attributes.Matches(new BrowsableAttribute(false)) || !attributes.Matches((Attribute[])null)) return 4;
                var merged = AttributeCollection.FromExisting(attributes, BrowsableAttribute.No, DesignOnlyAttribute.Yes);
                if (merged.Count != 3 || merged[typeof(BrowsableAttribute)] != BrowsableAttribute.No || merged[typeof(DesignOnlyAttribute)] != DesignOnlyAttribute.Yes || attributes[0] != BrowsableAttribute.Yes) return 5;
                if (AttributeCollection.FromExisting(attributes, null).Count != 2 || new AttributeCollection((Attribute[])null).Count != 0 || AttributeCollection.Empty.Count != 0) return 6;
                var copied = new Attribute[4]; attributes.CopyTo(copied, 1);
                if (copied[1] != BrowsableAttribute.Yes || copied[2] != display || ((ICollection)attributes).IsSynchronized || ((ICollection)attributes).SyncRoot != attributes) return 7;
                var count = 0; foreach (Attribute item in attributes) count++; if (count != 2) return 8;
                var caught = 0;
                try { var a = attributes[(Type)null]; } catch (ArgumentNullException) { caught++; }
                try { new AttributeCollection(new Attribute[] { null }); } catch (ArgumentNullException) { caught++; }
                try { AttributeCollection.FromExisting(null); } catch (ArgumentNullException) { caught++; }
                try { AttributeCollection.FromExisting(attributes, new Attribute[] { null }); } catch (ArgumentNullException) { caught++; }
                try { var a = attributes[typeof(ReadOnlyAttribute)]; } catch (PlatformNotSupportedException) { caught++; }
                try { var a = attributes[typeof(ReadOnlyAttribute)]; } catch (PlatformNotSupportedException) { caught++; }
                var custom = new CustomCollection(new Attribute[] { DesignOnlyAttribute.Yes });
                if (custom.Count != 1 || custom[0] != DesignOnlyAttribute.Yes || custom[typeof(DesignOnlyAttribute)] != DesignOnlyAttribute.Yes) return 10;
                try { custom.Default(null); } catch (ArgumentNullException) { caught++; }
                try { custom.Default(typeof(ReadOnlyAttribute)); } catch (PlatformNotSupportedException) { caught++; }
                return caught == 8 ? input + 1 : 9;
            }
            private sealed class CustomCollection : AttributeCollection
            {
                private readonly Attribute[] values;
                public CustomCollection(Attribute[] values) { this.values = values; }
                protected override Attribute[] Attributes => values;
                public Attribute Default(Type type) => GetDefaultAttribute(type);
            }
            private sealed class DerivedDisplay : DisplayNameAttribute
            {
                public DerivedDisplay(string name) : base(name) { }
            }
        }
        """;
}
