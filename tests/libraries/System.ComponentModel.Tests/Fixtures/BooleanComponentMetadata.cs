// From the compiler harness's BooleanComponentMetadataCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
#pragma warning disable CS0618
namespace BooleanMetadataFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var yes0=new ImmutableObjectAttribute(true); var no0=new ImmutableObjectAttribute(false);
        if(!yes0.Immutable || no0.Immutable || !yes0.Equals(new ImmutableObjectAttribute(true)) || !no0.Equals(new ImmutableObjectAttribute(false)) || yes0.Equals(no0) || yes0.Equals(null) || yes0.Equals(new object()) || !yes0.Equals(yes0) || yes0.GetHashCode()!=new ImmutableObjectAttribute(true).GetHashCode() || no0.GetHashCode()!=new ImmutableObjectAttribute(false).GetHashCode()) return 1001;
        if(!yes0.Equals(ImmutableObjectAttribute.Yes) || !no0.Equals(ImmutableObjectAttribute.No)) return 1020;
        if(yes0.IsDefaultAttribute()!=false || no0.IsDefaultAttribute()==false || !new ImmutableObjectAttribute(false).Equals(ImmutableObjectAttribute.Default)) return 1040;
        var yes1=new NotifyParentPropertyAttribute(true); var no1=new NotifyParentPropertyAttribute(false);
        if(!yes1.NotifyParent || no1.NotifyParent || !yes1.Equals(new NotifyParentPropertyAttribute(true)) || !no1.Equals(new NotifyParentPropertyAttribute(false)) || yes1.Equals(no1) || yes1.Equals(null) || yes1.Equals(new object()) || !yes1.Equals(yes1) || yes1.GetHashCode()!=new NotifyParentPropertyAttribute(true).GetHashCode() || no1.GetHashCode()!=new NotifyParentPropertyAttribute(false).GetHashCode()) return 1002;
        if(!yes1.Equals(NotifyParentPropertyAttribute.Yes) || !no1.Equals(NotifyParentPropertyAttribute.No)) return 1021;
        if(yes1.IsDefaultAttribute()!=false || no1.IsDefaultAttribute()==false || !new NotifyParentPropertyAttribute(false).Equals(NotifyParentPropertyAttribute.Default)) return 1041;
        var yes2=new ParenthesizePropertyNameAttribute(true); var no2=new ParenthesizePropertyNameAttribute(false);
        if(!yes2.NeedParenthesis || no2.NeedParenthesis || !yes2.Equals(new ParenthesizePropertyNameAttribute(true)) || !no2.Equals(new ParenthesizePropertyNameAttribute(false)) || yes2.Equals(no2) || yes2.Equals(null) || yes2.Equals(new object()) || !yes2.Equals(yes2) || yes2.GetHashCode()!=new ParenthesizePropertyNameAttribute(true).GetHashCode() || no2.GetHashCode()!=new ParenthesizePropertyNameAttribute(false).GetHashCode()) return 1003;
        if(yes2.IsDefaultAttribute()!=false || no2.IsDefaultAttribute()==false || !new ParenthesizePropertyNameAttribute(false).Equals(ParenthesizePropertyNameAttribute.Default)) return 1042;
        var yes3=new PasswordPropertyTextAttribute(true); var no3=new PasswordPropertyTextAttribute(false);
        if(!yes3.Password || no3.Password || !yes3.Equals(new PasswordPropertyTextAttribute(true)) || !no3.Equals(new PasswordPropertyTextAttribute(false)) || yes3.Equals(no3) || yes3.Equals(null) || yes3.Equals(new object()) || !yes3.Equals(yes3) || yes3.GetHashCode()!=new PasswordPropertyTextAttribute(true).GetHashCode() || no3.GetHashCode()!=new PasswordPropertyTextAttribute(false).GetHashCode()) return 1004;
        if(!yes3.Equals(PasswordPropertyTextAttribute.Yes) || !no3.Equals(PasswordPropertyTextAttribute.No)) return 1023;
        if(yes3.IsDefaultAttribute()!=false || no3.IsDefaultAttribute()==false || !new PasswordPropertyTextAttribute(false).Equals(PasswordPropertyTextAttribute.Default)) return 1043;
        var yes4=new RunInstallerAttribute(true); var no4=new RunInstallerAttribute(false);
        if(!yes4.RunInstaller || no4.RunInstaller || !yes4.Equals(new RunInstallerAttribute(true)) || !no4.Equals(new RunInstallerAttribute(false)) || yes4.Equals(no4) || yes4.Equals(null) || yes4.Equals(new object()) || !yes4.Equals(yes4) || yes4.GetHashCode()!=new RunInstallerAttribute(true).GetHashCode() || no4.GetHashCode()!=new RunInstallerAttribute(false).GetHashCode()) return 1005;
        if(!yes4.Equals(RunInstallerAttribute.Yes) || !no4.Equals(RunInstallerAttribute.No)) return 1024;
        if(yes4.IsDefaultAttribute()!=false || no4.IsDefaultAttribute()==false || !new RunInstallerAttribute(false).Equals(RunInstallerAttribute.Default)) return 1044;
        var yes5=new SettingsBindableAttribute(true); var no5=new SettingsBindableAttribute(false);
        if(!yes5.Bindable || no5.Bindable || !yes5.Equals(new SettingsBindableAttribute(true)) || !no5.Equals(new SettingsBindableAttribute(false)) || yes5.Equals(no5) || yes5.Equals(null) || yes5.Equals(new object()) || !yes5.Equals(yes5) || yes5.GetHashCode()!=new SettingsBindableAttribute(true).GetHashCode() || no5.GetHashCode()!=new SettingsBindableAttribute(false).GetHashCode()) return 1006;
        if(!yes5.Equals(SettingsBindableAttribute.Yes) || !no5.Equals(SettingsBindableAttribute.No)) return 1025;
        var yes6=new DesignTimeVisibleAttribute(true); var no6=new DesignTimeVisibleAttribute(false);
        if(!yes6.Visible || no6.Visible || !yes6.Equals(new DesignTimeVisibleAttribute(true)) || !no6.Equals(new DesignTimeVisibleAttribute(false)) || yes6.Equals(no6) || yes6.Equals(null) || yes6.Equals(new object()) || !yes6.Equals(yes6) || yes6.GetHashCode()!=new DesignTimeVisibleAttribute(true).GetHashCode() || no6.GetHashCode()!=new DesignTimeVisibleAttribute(false).GetHashCode()) return 1007;
        if(!yes6.Equals(DesignTimeVisibleAttribute.Yes) || !no6.Equals(DesignTimeVisibleAttribute.No)) return 1026;
        if(yes6.IsDefaultAttribute()!=true || no6.IsDefaultAttribute()==true || !new DesignTimeVisibleAttribute(true).Equals(DesignTimeVisibleAttribute.Default)) return 1046;
        var yes7=new RecommendedAsConfigurableAttribute(true); var no7=new RecommendedAsConfigurableAttribute(false);
        if(!yes7.RecommendedAsConfigurable || no7.RecommendedAsConfigurable || !yes7.Equals(new RecommendedAsConfigurableAttribute(true)) || !no7.Equals(new RecommendedAsConfigurableAttribute(false)) || yes7.Equals(no7) || yes7.Equals(null) || yes7.Equals(new object()) || !yes7.Equals(yes7) || yes7.GetHashCode()!=new RecommendedAsConfigurableAttribute(true).GetHashCode() || no7.GetHashCode()!=new RecommendedAsConfigurableAttribute(false).GetHashCode()) return 1008;
        if(!yes7.Equals(RecommendedAsConfigurableAttribute.Yes) || !no7.Equals(RecommendedAsConfigurableAttribute.No)) return 1027;
        if(yes7.IsDefaultAttribute()!=false || no7.IsDefaultAttribute()==false || !new RecommendedAsConfigurableAttribute(false).Equals(RecommendedAsConfigurableAttribute.Default)) return 1047;
        var yes8=new ListBindableAttribute(true); var no8=new ListBindableAttribute(false);
        if(!yes8.ListBindable || no8.ListBindable || !yes8.Equals(new ListBindableAttribute(true)) || !no8.Equals(new ListBindableAttribute(false)) || yes8.Equals(no8) || yes8.Equals(null) || yes8.Equals(new object()) || !yes8.Equals(yes8) || yes8.GetHashCode()!=new ListBindableAttribute(true).GetHashCode() || no8.GetHashCode()!=new ListBindableAttribute(false).GetHashCode()) return 1009;
        if(!yes8.Equals(ListBindableAttribute.Yes) || !no8.Equals(ListBindableAttribute.No)) return 1028;
        if(yes8.IsDefaultAttribute()!=true || no8.IsDefaultAttribute()==true || !new ListBindableAttribute(true).Equals(ListBindableAttribute.Default)) return 1048;
        if(new ParenthesizePropertyNameAttribute().NeedParenthesis || new PasswordPropertyTextAttribute().Password || new DesignTimeVisibleAttribute().Visible) return 1060;
        if(!new ListBindableAttribute(BindableSupport.Default).ListBindable || !new ListBindableAttribute(BindableSupport.Default).IsDefaultAttribute() || new ListBindableAttribute(BindableSupport.No).ListBindable || !new ListBindableAttribute((BindableSupport)123).ListBindable) return 1061;
        return 42;
    }
}
