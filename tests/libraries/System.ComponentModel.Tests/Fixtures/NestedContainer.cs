// From the compiler harness's NestedContainerCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
namespace CategoryFixtureNestedContainer;
public sealed class NamedContainer : NestedContainer
{
    public NamedContainer(IComponent owner) : base(owner) { }
    protected override string OwnerName => "custom";
    public ISite MakeSite(IComponent component) => CreateSite(component, "item");
}
public static class EntryPoint
{
    private static int _disposed;
    private static void Disposed(object sender, EventArgs args) { _disposed++; }
    public static int Run(int input)
    {
        var root = new Container(); var owner = new Component(); root.Add(owner, "root");
        var nested = new NestedContainer(owner); INestedContainer contract = nested;
        if (contract.Owner != owner) return 1;
        var child = new Component(); child.Disposed += Disposed; nested.Add(child, "child");
        var site = (INestedSite)child.Site;
        if (site.FullName != "root.child" || site.Name != "child" || site.Component != child || site.Container != nested || site.DesignMode) return 2;
        if (site.GetService(typeof(ISite)) != site || site.GetService(typeof(INestedContainer)) != nested || site.GetService(typeof(IContainer)) != nested || site.GetService(typeof(string)) != null) return 3;
        var deeper = new NestedContainer(child); var leaf = new Component(); leaf.Disposed += Disposed; deeper.Add(leaf, "leaf");
        if (((INestedSite)leaf.Site).FullName != "root.child.leaf") return 4;
        owner.Site.Name = "renamed"; if (((INestedSite)leaf.Site).FullName != "renamed.child.leaf") return 5;
        child.Site.Name = null; if (((INestedSite)child.Site).FullName != null || ((INestedSite)leaf.Site).FullName != "leaf") return 6;
        child.Site.Name = "again"; if (((INestedSite)leaf.Site).FullName != "renamed.again.leaf") return 7;
        _disposed = 0; owner.Dispose();
        if (_disposed != 2 || nested.Components.Count != 0 || deeper.Components.Count != 0 || child.Site != null || leaf.Site != null) return 8;
        var detachedOwner = new Component(); var detached = new NestedContainer(detachedOwner); var item = new Component(); detached.Add(item, "item");
        if (((INestedSite)item.Site).FullName != "item" || item.Site.DesignMode) return 9;
        detached.Dispose(); var replacement = new Component(); replacement.Disposed += Disposed; detached.Add(replacement, "new");
        detachedOwner.Dispose(); if (replacement.Site == null || detached.Components.Count != 1 || _disposed != 2) return 10;
        detached.Dispose(); if (_disposed != 3) return 11;
        var custom = new NamedContainer(new Component()); var customItem = new Component(); custom.Add(customItem, "child");
        if (((INestedSite)customItem.Site).FullName != "custom.child") return 12;
        var caught = 0;
        try { new NestedContainer(null); } catch (ArgumentNullException e) { if (e.ParamName == "owner") caught++; }
        try { custom.MakeSite(null); } catch (ArgumentNullException e) { if (e.ParamName == "component") caught++; }
        return caught == 2 ? input + 1 : 13;
    }
}
