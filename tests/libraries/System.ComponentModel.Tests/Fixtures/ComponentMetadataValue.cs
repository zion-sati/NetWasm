// From the compiler harness's ComponentMetadataValueCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
using DisplayNameAttribute = System.ComponentModel.DisplayNameAttribute;
namespace ComponentMetadataFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        if (!BrowsableAttribute.Yes.Browsable || BrowsableAttribute.No.Browsable || !ReferenceEquals(BrowsableAttribute.Yes, BrowsableAttribute.Default)) return 1;
        if (!new BrowsableAttribute(true).Equals(BrowsableAttribute.Yes) || !new BrowsableAttribute(true).IsDefaultAttribute() || BrowsableAttribute.No.IsDefaultAttribute()) return 2;
        if (!ReadOnlyAttribute.Yes.IsReadOnly || ReadOnlyAttribute.No.IsReadOnly || !ReadOnlyAttribute.No.IsDefaultAttribute() || ReadOnlyAttribute.Yes.IsDefaultAttribute()) return 3;
        if (!DesignOnlyAttribute.Yes.IsDesignOnly || DesignOnlyAttribute.No.IsDesignOnly || !DesignOnlyAttribute.No.IsDefaultAttribute() || DesignOnlyAttribute.Yes.IsDefaultAttribute()) return 4;
        var display = new DisplayNameAttribute("label");
        if (display.DisplayName != "label" || !display.Equals(new DisplayNameAttribute("label")) || display.IsDefaultAttribute() || !DisplayNameAttribute.Default.IsDefaultAttribute()) return 5;
        var category = new DesignerCategoryAttribute("Form");
        if (!category.Equals(DesignerCategoryAttribute.Form) || category.Category != "Form" || category.IsDefaultAttribute() || !new DesignerCategoryAttribute().IsDefaultAttribute()) return 6;
        // TypeId is the attribute type's full name and the category (issue 225: without type names a type's name reads its ID).
        if ((string)category.TypeId != typeof(DesignerCategoryAttribute).FullName + "Form") return 7;
        Attribute[] values = { new BrowsableAttribute(true), new ReadOnlyAttribute(false), new DesignOnlyAttribute(false), display, category };
        Attribute[] copies = { new BrowsableAttribute(true), new ReadOnlyAttribute(false), new DesignOnlyAttribute(false), new DisplayNameAttribute("label"), new DesignerCategoryAttribute("Form") };
        for (var i = 0; i < values.Length; i++)
        {
            if (!values[i].Equals(copies[i])) return 100 + i;
            if (values[i].GetHashCode() != copies[i].GetHashCode()) return 200 + i;
            if (values[i].Equals(null)) return 300 + i;
            if (values[i].Equals("wrong")) return 400 + i;
        }
        if (new DesignerCategoryAttribute(null).GetHashCode() != 0) return 9;
        return input + 1;
    }
}
