// From the compiler harness's ComponentContractsCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
namespace ComponentContractsFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var first = new Item(); first.Site = new Site(first, "alpha");
        var last = new Item(); last.Site = new Site(last, "beta");
        IComponent[] items = { first, null, last };
        var collection = new ComponentCollection(items); items[0] = last;
        if (collection.Count != 3 || collection[0] != first || collection[1] != null || collection[2] != last) return 1;
        if (collection["ALPHA"] != first || collection["beta"] != last || collection["missing"] != null || collection[(string)null] != null) return 2;
        first.Site.Name = "renamed";
        if (collection["alpha"] != null || collection["RENAMED"] != first || first.Site.GetService(typeof(IComponent)) != first) return 3;
        var copied = new IComponent[5]; collection.CopyTo(copied, 1);
        if (copied[1] != first || copied[2] != null || copied[3] != last) return 4;
        var count = 0; foreach (IComponent item in collection) count++;
        var disposed = 0; first.Disposed += (_, _) => disposed++; ((IDisposable)first).Dispose();
        var cancel = new CancelEventArgs(); if (cancel.Cancel) return 5;
        CancelEventHandler handler = (_, e) => e.Cancel = true; handler(first, cancel);
        if (!cancel.Cancel || !new CancelEventArgs(true).Cancel || count != 3 || disposed != 1) return 6;
        var converter = new TypeConverterAttribute("Converter, Example");
        if (converter.ConverterTypeName != "Converter, Example" || !converter.Equals(new TypeConverterAttribute(converter.ConverterTypeName)) || TypeConverterAttribute.Default.ConverterTypeName != "") return 7;
        var designer = new DesignerAttribute("Designer", "Base, Assembly");
        // TypeId is the attribute type's full name and the base type's name (issue 225: without type names a type's name reads its ID).
        if ((string)designer.TypeId != typeof(DesignerAttribute).FullName + "Base" || designer.DesignerBaseTypeName != "Base, Assembly" || !designer.Equals(new DesignerAttribute("Designer", "Base, Assembly"))) return 8;
        var caught = 0;
        try { new ComponentCollection(null); } catch (ArgumentNullException) { caught++; }
        try { var item = collection[-1]; } catch (ArgumentOutOfRangeException) { caught++; }
        try { new TypeConverterAttribute((string)null); } catch (ArgumentNullException) { caught++; }
        try { new DesignerAttribute((string)null); } catch (ArgumentNullException) { caught++; }
        return caught == 4 ? input + 1 : 9;
    }
    private sealed class Item : IComponent
    {
        public ISite Site { get; set; }
        public event EventHandler Disposed;
        public void Dispose() => Disposed?.Invoke(this, EventArgs.Empty);
    }
    private sealed class Site : ISite
    {
        public Site(IComponent component, string name) { Component = component; Name = name; }
        public IComponent Component { get; }
        public IContainer Container => null;
        public bool DesignMode => false;
        public string Name { get; set; }
        public object GetService(Type type) => type == typeof(IComponent) ? Component : null;
    }
}
