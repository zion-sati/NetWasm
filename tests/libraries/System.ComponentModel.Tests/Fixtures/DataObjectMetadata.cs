// From the compiler harness's DataObjectMetadataCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
namespace DataObjectFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var yes=new DataObjectAttribute(); var no=new DataObjectAttribute(false);
        if(!yes.IsDataObject || no.IsDataObject || !yes.Equals(DataObjectAttribute.DataObject) || !no.Equals(DataObjectAttribute.NonDataObject) || !ReferenceEquals(DataObjectAttribute.Default,DataObjectAttribute.NonDataObject)) return 1;
        if(yes.IsDefaultAttribute() || !no.IsDefaultAttribute() || yes.Equals(null) || yes.Equals(new object()) || yes.Equals(no) || !yes.Equals(yes) || yes.GetHashCode()!=new DataObjectAttribute(true).GetHashCode()) return 2;
        var first=new DataObjectFieldAttribute(true); var second=new DataObjectFieldAttribute(true,true); var third=new DataObjectFieldAttribute(true,true,true);
        if(!first.PrimaryKey || first.IsIdentity || first.IsNullable || first.Length!=-1 || !second.IsIdentity || second.IsNullable || second.Length!=-1 || !third.IsNullable || third.Length!=-1) return 3;
        for(var bits=0; bits<8; bits++)
        {
            var field=new DataObjectFieldAttribute((bits&1)!=0,(bits&2)!=0,(bits&4)!=0,42);
            var equal=new DataObjectFieldAttribute((bits&1)!=0,(bits&2)!=0,(bits&4)!=0,42);
            if(field.PrimaryKey!=((bits&1)!=0) || field.IsIdentity!=((bits&2)!=0) || field.IsNullable!=((bits&4)!=0) || field.Length!=42 || !field.Equals(field) || !field.Equals(equal) || field.GetHashCode()!=equal.GetHashCode() || field.Equals(null) || field.Equals(new object())) return 4;
            for(var changed=0; changed<8; changed++) if(field.Equals(new DataObjectFieldAttribute((changed&1)!=0,(changed&2)!=0,(changed&4)!=0,42))!=(changed==bits)) return 5;
            if(field.Equals(new DataObjectFieldAttribute(field.PrimaryKey,field.IsIdentity,field.IsNullable,43))) return 6;
        }
        if(new DataObjectFieldAttribute(false,false,false,-123).Length!=-123) return 7;
        for(var value=-1; value<6; value++)
        {
            var kind=(DataObjectMethodType)value; var normal=new DataObjectMethodAttribute(kind); var chosen=new DataObjectMethodAttribute(kind,true); var equal=new DataObjectMethodAttribute(kind,true);
            if(normal.MethodType!=kind || normal.IsDefault || !chosen.IsDefault || normal.Equals(chosen) || !chosen.Equals(equal) || !chosen.Equals(chosen) || chosen.GetHashCode()!=equal.GetHashCode()) return 8;
            if(!normal.Match(chosen) || !chosen.Match(normal) || !chosen.Match(chosen) || chosen.Match(new DataObjectMethodAttribute((DataObjectMethodType)(value+1))) || chosen.Match(null) || chosen.Match(new object()) || chosen.Equals(null)) return 9;
        }
        return (int)DataObjectMethodType.Fill==0 && (int)DataObjectMethodType.Select==1 && (int)DataObjectMethodType.Update==2 && (int)DataObjectMethodType.Insert==3 && (int)DataObjectMethodType.Delete==4 ? 42 : 10;
    }
}
