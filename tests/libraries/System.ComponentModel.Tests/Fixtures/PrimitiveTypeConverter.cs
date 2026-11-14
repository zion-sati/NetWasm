// From the compiler harness's PrimitiveTypeConverterCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
namespace CategoryFixturePrimitiveTypeConverter;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var boolean = new BooleanConverter(); var character = new CharConverter(); var text = new StringConverter();
        TypeConverter[] converters = { boolean, character, text };
        for (var i = 0; i < converters.Length; i++)
            if (!converters[i].CanConvertFrom(typeof(string)) || !converters[i].CanConvertTo(typeof(string)) || converters[i].CanConvertFrom(typeof(int))) return 1;
        if (!(bool)boolean.ConvertFromString(" TRUE ") || (bool)boolean.ConvertFromString("false") || boolean.ConvertToString(true) != "True") return 2;
        if (!boolean.GetStandardValuesSupported() || !boolean.GetStandardValuesExclusive()) return 3;
        var values = boolean.GetStandardValues(null);
        if (values.Count != 2 || !(bool)values[0] || (bool)values[1] || !ReferenceEquals(values, new BooleanConverter().GetStandardValues(null))) return 4;
        if ((char)character.ConvertFromString("") != '\0' || (char)character.ConvertFromString("  ") != '\0' || (char)character.ConvertFromString(" ") != ' ' || (char)character.ConvertFromString(" x ") != 'x') return 5;
        if (character.ConvertToString('\0') != "" || character.ConvertToString('x') != "x") return 6;
        var original = "untouched";
        if (!ReferenceEquals(text.ConvertFrom(original), original) || (string)text.ConvertFrom(null) != "") return 7;
        if (!boolean.IsValid("true") || boolean.IsValid("yes") || !character.IsValid("x") || character.IsValid("xy")) return 8;
        var caught = 0;
        try { boolean.ConvertFromString("yes"); } catch (FormatException e) { if (e.InnerException is FormatException && !string.IsNullOrEmpty(e.Message)) caught++; }
        try { character.ConvertFromString("xy"); } catch (FormatException e) { if (!string.IsNullOrEmpty(e.Message)) caught++; }
        foreach (var converter in converters)
        {
            try { converter.ConvertFrom(1); } catch (NotSupportedException) { caught++; }
            try { converter.ConvertTo("value", null); } catch (ArgumentNullException) { caught++; }
        }
        return caught == 8 ? input + 1 : 100 + caught;
    }
}
