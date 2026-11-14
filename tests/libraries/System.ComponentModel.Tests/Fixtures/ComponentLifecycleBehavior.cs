// From the compiler harness's ComponentLifecycleBehaviorCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
namespace ComponentLifecycleFixtureComponentLifecycleBehavior;
public sealed class Subject : Component
{
    public bool Enabled = true;
    public int Disposals;
    protected override bool CanRaiseEvents => Enabled;
    protected override void Dispose(bool disposing) { if (disposing) Disposals++; base.Dispose(disposing); }
    public bool IsDesign => DesignMode;
    public object Service(Type type) => GetService(type);
    public EventHandlerList Handlers => Events;
}
public sealed class Owner : IContainer
{
    public int Removes;
    public ComponentCollection Components => new ComponentCollection(Array.Empty<IComponent>());
    public void Add(IComponent component) { }
    public void Add(IComponent component, string name) { }
    public void Remove(IComponent component) { Removes++; component.Site = null; }
    public void Dispose() { }
}
public sealed class Site : ISite
{
    public Site(IComponent component, IContainer owner) { Component = component; Container = owner; }
    public IComponent Component { get; }
    public IContainer Container { get; }
    public bool DesignMode => true;
    public string Name { get; set; } = "subject";
    public object GetService(Type type) => type == typeof(Site) ? this : null;
}
public static class EntryPoint
{
    private static int _calls;
    private static void First(object sender, EventArgs args) { _calls += 1; }
    private static void Second(object sender, EventArgs args) { _calls += 10; }
    public static int Run(int input)
    {
        _calls = 0;
        var list = new EventHandlerList(); var key = new object();
        if (list[key] != null) return 1;
        list.AddHandler(key, (EventHandler)First); list.AddHandler(key, (EventHandler)Second);
        ((EventHandler)list[key])(null, EventArgs.Empty); if (_calls != 11) return 2;
        list.RemoveHandler(key, (EventHandler)First); ((EventHandler)list[key])(null, EventArgs.Empty); if (_calls != 21) return 3;
        list[key] = (EventHandler)First; var more = new EventHandlerList(); more.AddHandler(key, (EventHandler)Second); list.AddHandlers(more);
        ((EventHandler)list[key])(null, EventArgs.Empty); if (_calls != 32) return 4;
        list.Dispose(); if (list[key] != null) return 5;
        list.AddHandler(key, (EventHandler)First); if (list[key] == null) return 6;
        var failed = 0; try { list.AddHandlers(null); } catch (ArgumentNullException) { failed++; }
        var subject = new Subject();
        if (subject.Site != null || subject.Container != null || subject.IsDesign || subject.Service(typeof(Site)) != null || subject.ToString() != typeof(Subject).FullName) return 7;
        if (!ReferenceEquals(subject.Handlers, subject.Handlers)) return 8;
        subject.Handlers.AddHandler(key, (EventHandler)First); subject.Enabled = false;
        if (subject.Handlers[key] != null) return 9;
        subject.Enabled = true; if (subject.Handlers[key] == null) return 10;
        var owner = new Owner(); var site = new Site(subject, owner); subject.Site = site;
        if (subject.Container != owner || !subject.IsDesign || subject.Service(typeof(Site)) != site || subject.ToString() != "subject [" + typeof(Subject).FullName + "]") return 11;
        _calls = 0; subject.Disposed += First; subject.Disposed += Second; subject.Disposed -= Second;
        subject.Dispose(); if (subject.Disposals != 1 || owner.Removes != 1 || subject.Site != null || _calls != 1) return 12;
        subject.Enabled = false; subject.Dispose(); if (subject.Disposals != 2 || _calls != 1 || owner.Removes != 1) return 13;
        return failed == 1 ? input + 1 : 14;
    }
}
