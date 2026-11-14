// From the compiler harness's BindingMetadataCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
namespace BindingMetadataFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var yes=new BindableAttribute(true); var no=new BindableAttribute(false);
        if(!yes.Bindable || no.Bindable || yes.Direction!=BindingDirection.OneWay || !yes.Equals(BindableAttribute.Yes) || !no.Equals(BindableAttribute.No)) return 1;
        if(!ReferenceEquals(BindableAttribute.Default,BindableAttribute.No) || yes.IsDefaultAttribute() || !no.IsDefaultAttribute()) return 2;
        var both=new BindableAttribute(BindableSupport.Yes,BindingDirection.TwoWay);
        if(both.Direction!=BindingDirection.TwoWay || !both.Equals(yes) || both.GetHashCode()!=yes.GetHashCode()) return 3;
        var inferred=new BindableAttribute(BindableSupport.Default); if(!inferred.Bindable || !inferred.IsDefaultAttribute()) return 4;
        var unusual=new BindableAttribute((BindableSupport)123,(BindingDirection)456); if(!unusual.Bindable || (int)unusual.Direction!=456 || unusual.IsDefaultAttribute()) return 5;
        if(yes.Equals(null) || yes.Equals(new object()) || !yes.Equals(yes) || new BindableAttribute(BindableSupport.No).Bindable) return 6;
        var empty=new DefaultBindingPropertyAttribute(); var named=new DefaultBindingPropertyAttribute("Value");
        if(empty.Name!=null || !empty.Equals(DefaultBindingPropertyAttribute.Default) || named.Name!="Value" || !named.Equals(new DefaultBindingPropertyAttribute("Value")) || named.Equals(empty) || named.Equals(null)) return 7;
        if(named.GetHashCode()!=new DefaultBindingPropertyAttribute("Value").GetHashCode() || empty.GetHashCode()!=new DefaultBindingPropertyAttribute(null).GetHashCode()) return 8;
        var complex=new ComplexBindingPropertiesAttribute("Items","Name"); var source=new ComplexBindingPropertiesAttribute("Items");
        if(complex.DataSource!="Items" || complex.DataMember!="Name" || source.DataMember!=null || !new ComplexBindingPropertiesAttribute().Equals(ComplexBindingPropertiesAttribute.Default)) return 9;
        if(!complex.Equals(new ComplexBindingPropertiesAttribute("Items","Name")) || complex.Equals(source) || complex.Equals(new ComplexBindingPropertiesAttribute("Other","Name")) || complex.Equals(null)) return 10;
        if(complex.GetHashCode()!=new ComplexBindingPropertiesAttribute("Items","Name").GetHashCode()) return 11;
        var lookup=new LookupBindingPropertiesAttribute("Items","Display","Id","Key");
        if(lookup.DataSource!="Items" || lookup.DisplayMember!="Display" || lookup.ValueMember!="Id" || lookup.LookupMember!="Key") return 12;
        if(!new LookupBindingPropertiesAttribute().Equals(LookupBindingPropertiesAttribute.Default) || !lookup.Equals(new LookupBindingPropertiesAttribute("Items","Display","Id","Key")) || lookup.Equals(null)) return 13;
        if(lookup.Equals(new LookupBindingPropertiesAttribute("Other","Display","Id","Key")) || lookup.Equals(new LookupBindingPropertiesAttribute("Items","Other","Id","Key")) || lookup.Equals(new LookupBindingPropertiesAttribute("Items","Display","Other","Key")) || lookup.Equals(new LookupBindingPropertiesAttribute("Items","Display","Id","Other"))) return 14;
        if(lookup.GetHashCode()!=new LookupBindingPropertiesAttribute("Items","Display","Id","Key").GetHashCode() || !new LookupBindingPropertiesAttribute(null,null,null,null).Equals(LookupBindingPropertiesAttribute.Default)) return 15;
        return 42;
    }
}
