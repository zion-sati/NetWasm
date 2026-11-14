// From the compiler harness's MemberDescriptorCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Collections;
using System.ComponentModel;
using CategoryAttribute = System.ComponentModel.CategoryAttribute;
using DisplayNameAttribute = System.ComponentModel.DisplayNameAttribute;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
namespace DescriptorFixture;
public sealed class Descriptor : MemberDescriptor
{
    public int Fills;
    public Descriptor(string name, Attribute[] attributes) : base(name, attributes) { }
    public Descriptor(MemberDescriptor other) : base(other) { }
    public Descriptor(MemberDescriptor other, Attribute[] attributes) : base(other, attributes) { }
    public void Replace(Attribute[] attributes) { AttributeArray = attributes; }
    public int NameHash => NameHashCode;
    protected override void FillAttributes(IList list) { Fills++; base.FillAttributes(list); }
    public object Target(Type type, object instance) => GetInvocationTarget(type, instance);
    public static ISite Site(object component) => GetSite(component);
}
public static class EntryPoint
{
    public static int Run(int input)
    {
        Attribute[] attributes = { new CategoryAttribute("Data"), new DescriptionAttribute("details"), BrowsableAttribute.Yes, DesignOnlyAttribute.No, new DisplayNameAttribute("label"), BrowsableAttribute.No, null };
        var member = new Descriptor("name", attributes);
        if (member.Name != "name" || member.NameHash != "name".GetHashCode() || member.GetHashCode() != member.NameHash) return 1;
        if (member.Attributes.Count != 5 || member.Fills != 1 || member.IsBrowsable || member.DesignTimeOnly) return 2;
        if (member.Category != "Data" || member.Description != "details" || member.DisplayName != "label") return 3;
        if (!ReferenceEquals(member.Attributes, member.Attributes) || member.Fills != 1) return 4;
        var copy = new Descriptor(member);
        if (copy.Name != "name" || copy.DisplayName != "label" || copy.Attributes.Count != 5 || copy.IsBrowsable) return 5;
        var merged = new Descriptor(member, new Attribute[] { BrowsableAttribute.Yes, new DisplayNameAttribute("new") });
        if (!merged.IsBrowsable || merged.DisplayName != "new" || merged.Attributes.Count != 5) return 6;
        var first = new Descriptor("a", new Attribute[] { BrowsableAttribute.Yes });
        var second = new Descriptor("a", new Attribute[] { BrowsableAttribute.Yes });
        if (!first.Equals(second) || first.GetHashCode() != second.GetHashCode() || first.Equals(null) || first.Equals("wrong")) return 7;
        var before = first.Attributes;
        first.Replace(new Attribute[] { BrowsableAttribute.No, new DisplayNameAttribute() });
        if (first.IsBrowsable || first.DisplayName != "a" || ReferenceEquals(before, first.Attributes)) return 8;
        if (first.Equals(second) || new Descriptor("different", null).Equals(second)) return 9;
        if (Descriptor.Site(null) != null || Descriptor.Site(new object()) != null) return 10;
        var failures = 0;
        try { new Descriptor((string)null, attributes); } catch (ArgumentNullException e) { if (e.ParamName == "name") failures++; }
        try { new Descriptor("", attributes); } catch (ArgumentException e) { if (e.ParamName == "name") failures++; }
        try { new Descriptor((MemberDescriptor)null); } catch (ArgumentNullException e) { if (e.ParamName == "descr") failures++; }
        try { new Descriptor((MemberDescriptor)null, attributes); } catch (ArgumentNullException e) { if (e.ParamName == "oldMemberDescriptor") failures++; }
        try { member.Target(null, member); } catch (ArgumentNullException e) { if (e.ParamName == "type") failures++; }
        try { member.Target(typeof(Descriptor), null); } catch (ArgumentNullException e) { if (e.ParamName == "instance") failures++; }
        if (failures != 6) return 11;
        return input + 1;
    }
}
