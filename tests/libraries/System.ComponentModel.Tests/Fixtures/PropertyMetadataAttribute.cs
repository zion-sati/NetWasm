// From the compiler harness's PropertyMetadataAttributeCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
namespace CategoryFixturePropertyMetadataAttribute;
public static class EntryPoint
{
    public static int Run(int input)
    {
        if (!LocalizableAttribute.Yes.IsLocalizable || LocalizableAttribute.No.IsLocalizable || !ReferenceEquals(LocalizableAttribute.No, LocalizableAttribute.Default)) return 1;
        if (LocalizableAttribute.Yes.IsDefaultAttribute() || !new LocalizableAttribute(false).IsDefaultAttribute()) return 2;
        if (!ReferenceEquals(DesignerSerializationVisibilityAttribute.Visible, DesignerSerializationVisibilityAttribute.Default)) return 3;
        if ((int)DesignerSerializationVisibility.Hidden != 0 || (int)DesignerSerializationVisibility.Visible != 1 || (int)DesignerSerializationVisibility.Content != 2) return 4;
        if (DesignerSerializationVisibilityAttribute.Hidden.Visibility != DesignerSerializationVisibility.Hidden || DesignerSerializationVisibilityAttribute.Content.Visibility != DesignerSerializationVisibility.Content) return 5;
        int[] values = { -1, 0, 1, 2, 3, int.MinValue, int.MaxValue };
        for (var i = 0; i < values.Length; i++)
        {
            var first = new DesignerSerializationVisibilityAttribute((DesignerSerializationVisibility)values[i]);
            var second = new DesignerSerializationVisibilityAttribute((DesignerSerializationVisibility)values[i]);
            if ((int)first.Visibility != values[i] || !first.Equals(second) || first.GetHashCode() != second.GetHashCode()) return 100 + i;
            if (first.IsDefaultAttribute() != (values[i] == 1) || first.Equals(null) || first.Equals("wrong")) return 200 + i;
        }
        Attribute[] a = { new LocalizableAttribute(true), new LocalizableAttribute(false), new DesignerSerializationVisibilityAttribute(DesignerSerializationVisibility.Content) };
        Attribute[] b = { LocalizableAttribute.Yes, LocalizableAttribute.No, DesignerSerializationVisibilityAttribute.Content };
        for (var i = 0; i < a.Length; i++)
            if (!a[i].Equals(b[i]) || a[i].GetHashCode() != b[i].GetHashCode() || a[i].Equals(null) || a[i].Equals("wrong")) return 300 + i;
        return input + 1;
    }
}
