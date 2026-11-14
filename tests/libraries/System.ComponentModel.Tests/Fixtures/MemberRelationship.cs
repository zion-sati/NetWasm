// From the compiler harness's MemberRelationshipCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
namespace RelationshipFixture;
sealed class Descriptor : MemberDescriptor { public Descriptor(string name):base(name){} }
sealed class Owner { public override bool Equals(object other)=>other is Owner; public override int GetHashCode()=>17; }
public static class EntryPoint
{
    public static int Run(int input)
    {
        var empty=default(MemberRelationship);
        if(!empty.IsEmpty || empty.Owner!=null || empty.Member!=null || empty!=MemberRelationship.Empty || !empty.Equals((object)MemberRelationship.Empty) || empty.GetHashCode()!=MemberRelationship.Empty.GetHashCode())return 1;
        var owner=new Owner();var member=new Descriptor("Value");var a=new MemberRelationship(owner,member);var b=new MemberRelationship(owner,member);
        if(a.IsEmpty || !ReferenceEquals(a.Owner,owner) || !ReferenceEquals(a.Member,member) || a!=b || !(a==b) || !a.Equals(b) || !a.Equals((object)b) || a.GetHashCode()!=b.GetHashCode())return 2;
        if(a.Equals(null) || a.Equals(new object()) || a==new MemberRelationship(new Owner(),member) || a==new MemberRelationship(owner,new Descriptor("Value")))return 3;
        IEquatable<MemberRelationship> boxed=a;if(!boxed.Equals(b))return 4;
        try{new MemberRelationship(null,member);return 5;}catch(ArgumentNullException e){if(e.ParamName!="owner")return 6;}
        try{new MemberRelationship(owner,null);return 7;}catch(ArgumentNullException e){if(e.ParamName!="member")return 8;}
        var args=new ResolveNameEventArgs("item");if(args.Name!="item" || args.Value!=null)return 9;
        var calls=0;ResolveNameEventHandler handler=(sender,e)=>{if(sender==owner && ReferenceEquals(e,args)){calls++;e.Value=member;}};handler(owner,args);
        if(calls!=1 || !ReferenceEquals(args.Value,member))return 10;args.Value=null;if(args.Value!=null || new ResolveNameEventArgs(null).Name!=null)return 11;
        return 42;
    }
}
