using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ContainerCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ContainersPreserveOwnershipSitesAndFiltering(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("CategoryFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "CategoryFixture.EntryPoint",
            "Run", [new("run", "CategoryFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.ComponentModel;
        namespace CategoryFixture;
        public sealed class Filter : ContainerFilterService
        {
            public override ComponentCollection FilterComponents(ComponentCollection components) => components.Count == 0 ? components : new ComponentCollection(new IComponent[] { components[0] });
        }
        public sealed class Owner : Container
        {
            public ContainerFilterService Filter;
            protected override object GetService(Type type) => type == typeof(ContainerFilterService) ? Filter : base.GetService(type);
            public void Unlist(IComponent component) => RemoveWithoutUnsiting(component);
            public void Validate(IComponent component, string name) => ValidateName(component, name);
        }
        public static class EntryPoint
        {
            private static int _disposed;
            private static void Disposed(object sender, EventArgs args) { _disposed++; }
            public static int Run(int input)
            {
                var owner = new Owner(); owner.Add(null); owner.Remove(null);
                var first = new Component(); var second = new Component();
                owner.Add(first, "first"); owner.Add(second, "second"); owner.Add(first, "ignored");
                if (owner.Components.Count != 2 || owner.Components["FIRST"] != first || first.Site.Name != "first") return 1;
                if (first.Site.DesignMode || first.Site.Component != first || first.Site.GetService(typeof(ISite)) != first.Site || first.Site.GetService(typeof(IContainer)) != owner) return 2;
                second.Site.Name = "renamed"; if (owner.Components["renamed"] != second) return 3;
                var other = new Owner(); other.Add(first, "moved");
                if (owner.Components.Count != 1 || first.Container != other || first.Site.Name != "moved") return 4;
                owner.Unlist(second); if (owner.Components.Count != 0 || second.Site == null) return 5;
                second.Site = null; owner.Add(second, "second");
                _disposed = 0;
                for (var i = 0; i < 7; i++) { var item = new Component(); item.Disposed += Disposed; owner.Add(item); }
                if (owner.Components.Count != 8) return 6;
                owner.Dispose(); if (_disposed != 7 || second.Site != null || owner.Components.Count != 0) return 7;
                owner.Dispose(); if (_disposed != 7) return 8;
                var filtered = new Owner { Filter = new Filter() }; filtered.Add(new Component()); filtered.Add(new Component());
                if (filtered.Components.Count != 1) return 9;
                if (!new InheritanceAttribute().Equals(InheritanceAttribute.NotInherited) || !InheritanceAttribute.Default.IsDefaultAttribute() || InheritanceAttribute.Inherited.IsDefaultAttribute()) return 10;
                if (InheritanceAttribute.Inherited.ToString() != "Inherited" || InheritanceAttribute.InheritedReadOnly.ToString() != "InheritedReadOnly" || InheritanceAttribute.Default.ToString() != "NotInherited") return 11;
                if (new InheritanceAttribute(InheritanceLevel.Inherited).GetHashCode() != InheritanceAttribute.Inherited.GetHashCode()) return 12;
                var failed = 0;
                try { owner.Validate(null, "x"); } catch (ArgumentNullException) { failed++; }
                try { new InheritanceAttribute((InheritanceLevel)99).ToString(); } catch (ArgumentException) { failed++; }
                try { other.Add(new Component(), "MOVED"); } catch (PlatformNotSupportedException e) { if (!string.IsNullOrEmpty(e.Message)) failed++; }
                if (other.Components.Count != 1) return 13;
                return failed == 3 ? input + 1 : 100 + failed;
            }
        }
        """;
}
