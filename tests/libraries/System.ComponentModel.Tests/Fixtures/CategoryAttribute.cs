// From the compiler harness's CategoryAttributeCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
using CategoryAttribute = System.ComponentModel.CategoryAttribute;
namespace CategoryFixtureCategoryAttribute;
public sealed class CustomCategory : CategoryAttribute
{
    public int Calls;
    public CustomCategory(string value) : base(value) { }
    protected override string GetLocalizedString(string value)
    {
        Calls++;
        return value == "custom" ? "Custom label" : base.GetLocalizedString(value);
    }
}
public static class EntryPoint
{
    public static int Run(int input)
    {
        string[] keys = { "Action", "Appearance", "Asynchronous", "Behavior", "Config", "Data", "DDE", "Default", "Design", "DragDrop", "Focus", "Font", "Format", "Key", "Layout", "List", "Mouse", "Position", "Scale", "Text", "WindowStyle", "unknown", "" };
        string[] expected = { "Action", "Appearance", "Asynchronous", "Behavior", "Configurations", "Data", "DDE", "Misc", "Design", "Drag Drop", "Focus", "Font", "Format", "Key", "Layout", "List", "Mouse", "Position", "Scale", "Text", "Window Style", "unknown", "" };
        for (var i = 0; i < keys.Length; i++)
        {
            var category = new CategoryAttribute(keys[i]);
            var copy = new CategoryAttribute(keys[i]);
            if (category.Category != expected[i] || !category.Equals(copy) || category.GetHashCode() != copy.GetHashCode()) return 100 + i;
            if (category.Equals(null) || category.Equals("wrong")) return 200 + i;
        }
        CategoryAttribute[] defaults = { CategoryAttribute.Action, CategoryAttribute.Appearance, CategoryAttribute.Asynchronous, CategoryAttribute.Behavior, CategoryAttribute.Data, CategoryAttribute.Default, CategoryAttribute.Design, CategoryAttribute.DragDrop, CategoryAttribute.Focus, CategoryAttribute.Format, CategoryAttribute.Key, CategoryAttribute.Layout, CategoryAttribute.Mouse, CategoryAttribute.WindowStyle };
        string[] names = { "Action", "Appearance", "Asynchronous", "Behavior", "Data", "Misc", "Design", "Drag Drop", "Focus", "Format", "Key", "Layout", "Mouse", "Window Style" };
        for (var i = 0; i < defaults.Length; i++)
            if (defaults[i].Category != names[i]) return 300 + i;
        if (!ReferenceEquals(CategoryAttribute.Action, CategoryAttribute.Action) || !ReferenceEquals(CategoryAttribute.Default, CategoryAttribute.Default)) return 1;
        if (!new CategoryAttribute().IsDefaultAttribute() || !new CategoryAttribute("Misc").IsDefaultAttribute() || CategoryAttribute.Action.IsDefaultAttribute()) return 2;
        var custom = new CustomCategory("custom");
        if (custom.Calls != 0 || custom.Category != "Custom label" || custom.Category != "Custom label" || custom.Calls != 1) return 3;
        var fallback = new CustomCategory("unknown");
        if (fallback.Category != "unknown" || fallback.Category != "unknown" || fallback.Calls != 1) return 4;
        var nullCategory = new CategoryAttribute(null);
        if (nullCategory.Category != null || nullCategory.GetHashCode() != 0 || !nullCategory.Equals(new CategoryAttribute(null))) return 5;
        return input + 1;
    }
}
