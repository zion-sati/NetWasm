// From the compiler harness's TextAndTypeListConverterCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
namespace ConverterFixture;
sealed class Types : TypeListConverter { public Types(Type[] types) : base(types) {} }
public static class EntryPoint
{
    public static int Run(int input)
    {
        TypeConverter text=new MultilineStringConverter();
        if((string)text.ConvertTo(null,null,"first\nsecond",typeof(string))!="(Text)" || (string)text.ConvertTo(null,null,"",typeof(string))!="(Text)" || (string)text.ConvertTo(null,null,null,typeof(string))!="")return 1;
        if(text.GetPropertiesSupported(null) || text.GetProperties(null,"text",null)!=null)return 2;
        var values=new Type[]{typeof(int),typeof(string)}; TypeConverter types=new Types(values);
        if(!types.CanConvertFrom(null,typeof(string)) || !types.CanConvertTo(null,typeof(string)) || !types.CanConvertTo(null,typeof(InstanceDescriptor)) || types.CanConvertFrom(null,typeof(int)))return 3;
        if(types.ConvertFrom(null,null,"System.Int32")!=typeof(int) || types.ConvertFrom(null,null,"System.String")!=typeof(string))return 4;
        if((string)types.ConvertTo(null,null,typeof(int),typeof(string))!="System.Int32" || (string)types.ConvertTo(null,null,null,typeof(string))!="(none)")return 5;
        var standard=types.GetStandardValues(null); if(standard.Count!=2 || standard[0]!=typeof(int) || standard[1]!=typeof(string) || !ReferenceEquals(standard,types.GetStandardValues(null)) || !types.GetStandardValuesExclusive(null) || !types.GetStandardValuesSupported(null))return 6;
        values[0]=typeof(bool); if(standard[0]!=typeof(int) || types.ConvertFrom(null,null,"System.Boolean")!=typeof(bool))return 7;
        if(new Types(null).GetStandardValues(null).Count!=0 || new Types(new Type[0]).GetStandardValues(null).Count!=0)return 8;
        var validation=0;
        try{text.ConvertTo(null,null,"text",null);}catch(ArgumentNullException e){if(e.ParamName=="destinationType")validation++;}
        try{types.ConvertTo(null,null,null,null);}catch(ArgumentNullException e){if(e.ParamName=="destinationType")validation++;}
        try{types.ConvertFrom(null,null,"Missing.Type");}catch(NotSupportedException){validation++;}
        try{types.ConvertTo(null,null,42,typeof(string));}catch(InvalidCastException){validation++;}
        try{types.ConvertTo(null,null,typeof(int),typeof(InstanceDescriptor));}catch(NotSupportedException){validation++;}
        return validation==5?42:9;
    }
}
