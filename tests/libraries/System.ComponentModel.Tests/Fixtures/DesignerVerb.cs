// From the compiler harness's DesignerVerbCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Collections;
using System.ComponentModel.Design;
namespace VerbFixture;
sealed class Tracked : DesignerVerbCollection
{
    public int Clears, Inserts, Removes, Sets;
    protected override void OnClear(){Clears++;base.OnClear();}
    protected override void OnInsert(int index,object value){Inserts++;base.OnInsert(index,value);}
    protected override void OnRemove(int index,object value){Removes++;base.OnRemove(index,value);}
    protected override void OnSet(int index,object oldValue,object newValue){Sets++;base.OnSet(index,oldValue,newValue);}
}
public static class EntryPoint
{
    public static int Run(int input)
    {
        var texts=new string[]{null,"","Save (&S)","(&A)(&B)","(&)","(&AB)","(&\n)","(&\r)","x(&()y","x(&))y","(&😀)"};
        var expected=new string[]{"","","Save ","","(&)","(&AB)","(&\n)","","xy","xy","(&😀)"};
        for(var i=0;i<texts.Length;i++){var item=new DesignerVerb(texts[i],null);if(item.Text!=expected[i] || item.Description!="" || item.CommandID!=StandardCommands.VerbFirst)return 1;}
        var calls=0;var verb=new DesignerVerb("Run",(s,e)=>calls++);verb.Invoke();verb.Invoke(42);if(calls!=2)return 2;
        verb.Description="help";if(verb.Description!="help")return 3;verb.Description=null;if(verb.Description!="")return 4;
        verb.Properties["Text"]="changed";if(verb.Text!="changed" || !verb.ToString().StartsWith("changed : "))return 5;
        var explicitId=new CommandID(Guid.Empty,42);var second=new DesignerVerb("Second",null,explicitId);if(second.CommandID!=explicitId)return 6;
        var values=new DesignerVerbCollection(new DesignerVerb[]{verb,null});if(values.Count!=2 || values[0]!=verb || values[1]!=null || !values.Contains(null) || values.IndexOf(verb)!=0)return 7;
        if(values.Add(second)!=2)return 8;values.Insert(1,second);values[0]=null;if(values.Count!=4 || values.IndexOf(second)!=1)return 9;
        values.Remove(second);if(values.Count!=3 || values[2]!=second)return 10;
        var copy=new DesignerVerb[5];values.CopyTo(copy,1);if(copy[1]!=null || copy[2]!=null || copy[3]!=second)return 11;
        values.AddRange(values);if(values.Count!=6 || values[5]!=second)return 12;
        try{values.AddRange((DesignerVerb[])null);return 13;}catch(ArgumentNullException e){if(e.ParamName!="value")return 14;}
        try{values.AddRange((DesignerVerbCollection)null);return 15;}catch(ArgumentNullException){}
        try{var ignored=values[-1];return 16;}catch(ArgumentOutOfRangeException){}
        ((IList)values).Add("wrong");try{var ignored=values[6];return 17;}catch(InvalidCastException){}
        values.Clear();if(values.Count!=0)return 18;
        if(StandardCommands.Copy.ID!=15 || StandardCommands.Cut.ID!=16 || StandardCommands.VerbFirst.ID!=8192 || StandardCommands.VerbLast.ID!=8448)return 19;
        if(StandardToolWindows.Toolbox!=new Guid("b1e99781-ab81-11d0-b683-00aa00a3ee26"))return 20;
        var tracked=new Tracked();tracked.Add(verb);tracked[0]=second;tracked.Remove(second);tracked.Clear();
        if(tracked.Inserts!=1 || tracked.Sets!=1 || tracked.Removes!=1 || tracked.Clears!=1)return 21;
        return 42;
    }
}
