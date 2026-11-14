// From the compiler harness's CustomTypeDescriptorCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
using DisplayNameAttribute = System.ComponentModel.DisplayNameAttribute;
namespace CategoryFixtureCustomTypeDescriptor;
public sealed class Item { public event EventHandler Changed; public void Fire() => Changed?.Invoke(this, EventArgs.Empty); }
public sealed class Event : EventDescriptor
{
    public Event(string name) : base(name, null) { }
    public override Type ComponentType => typeof(Item);
    public override Type EventType => typeof(EventHandler);
    public override bool IsMulticast => true;
    public override void AddEventHandler(object component, Delegate handler) { ((Item)component).Changed += (EventHandler)handler; }
    public override void RemoveEventHandler(object component, Delegate handler) { ((Item)component).Changed -= (EventHandler)handler; }
}
public sealed class Descriptor : CustomTypeDescriptor
{
    public Descriptor() { }
    public Descriptor(ICustomTypeDescriptor parent) : base(parent) { }
}
public sealed class Parent : CustomTypeDescriptor
{
    public readonly Event Event = new Event("Changed");
    public readonly object Marker = new object();
    public Attribute[] Seen;
    public Type EditorType;
    public override string GetClassName() => "Caller";
    public override string GetComponentName() => "component";
    public override AttributeCollection GetAttributes() => new AttributeCollection(new DisplayNameAttribute("caller"));
    public override TypeConverter GetConverter() => new BooleanConverter();
    public override EventDescriptor GetDefaultEvent() => Event;
    public override EventDescriptorCollection GetEvents() => new EventDescriptorCollection(new EventDescriptor[] { Event });
    public override EventDescriptorCollection GetEvents(Attribute[] attributes) { Seen = attributes; return GetEvents(); }
    public override PropertyDescriptorCollection GetProperties(Attribute[] attributes) { Seen = attributes; return PropertyDescriptorCollection.Empty; }
    public override object GetEditor(Type type) { EditorType = type; return Marker; }
    public override object GetPropertyOwner(PropertyDescriptor property) => Marker;
}
public static class EntryPoint
{
    private static int _calls;
    private static void Changed(object sender, EventArgs args) { _calls++; }
    public static int Run(int input)
    {
        ICustomTypeDescriptor empty = new Descriptor();
        if (empty.GetAttributes().Count != 0 || empty.GetClassName() != null || empty.GetComponentName() != null || empty.GetDefaultEvent() != null || empty.GetDefaultProperty() != null) return 1;
        if (empty.GetEvents().Count != 0 || empty.GetEvents(null).Count != 0 || empty.GetProperties().Count != 0 || empty.GetProperties(null).Count != 0 || empty.GetPropertyOwner(null) != null || empty.GetEditor(typeof(object)) != null) return 2;
        if (empty.GetConverter().ConvertToString(42) != "42") return 3;
        var parent = new Parent(); ICustomTypeDescriptor forwarded = new Descriptor(parent);
        if (forwarded.GetClassName() != "Caller" || forwarded.GetComponentName() != "component" || ((DisplayNameAttribute)forwarded.GetAttributes()[typeof(DisplayNameAttribute)]).DisplayName != "caller") return 4;
        if (!(forwarded.GetConverter() is BooleanConverter) || forwarded.GetDefaultEvent() != parent.Event || forwarded.GetPropertyOwner(null) != parent.Marker || forwarded.GetEditor(typeof(string)) != parent.Marker || parent.EditorType != typeof(string)) return 5;
        var filter = new Attribute[] { BrowsableAttribute.Yes };
        if (forwarded.GetEvents(filter)[0] != parent.Event || parent.Seen != filter || forwarded.GetProperties(filter).Count != 0 || parent.Seen != filter) return 6;
        var item = new Item(); _calls = 0; var handler = (EventHandler)Changed;
        parent.Event.AddEventHandler(item, handler); item.Fire(); parent.Event.RemoveEventHandler(item, handler); item.Fire();
        if (_calls != 1 || parent.Event.ComponentType != typeof(Item) || parent.Event.EventType != typeof(EventHandler) || !parent.Event.IsMulticast) return 7;
        var events = new EventDescriptorCollection(new EventDescriptor[] { new Event("z"), parent.Event, new Event("a") });
        if (events.Find("changed", true) != parent.Event || events.Find("changed", false) != null || events["Changed"] != parent.Event) return 8;
        // The invariant runtime uses ordinal ordering, unlike ICU InvariantCulture.
        var sorted = events.Sort(); if (sorted[0].Name != "Changed" || sorted[1].Name != "a" || sorted[2].Name != "z" || events[0].Name != "z") return 9;
        var named = events.Sort(new string[] { "z" }); if (named[0].Name != "z") return 10;
        events.Remove(parent.Event); events.Insert(0, parent.Event); if (!events.Contains(parent.Event) || events.IndexOf(parent.Event) != 0) return 11;
        var copied = new EventDescriptor[3]; ((System.Collections.ICollection)events).CopyTo(copied, 0); if (copied[0] != parent.Event) return 12;
        var count = 0; foreach (EventDescriptor value in events) count++; if (count != 3) return 13;
        try { EventDescriptorCollection.Empty.Add(parent.Event); return 14; } catch (NotSupportedException) { }
        return input + 1;
    }
}
