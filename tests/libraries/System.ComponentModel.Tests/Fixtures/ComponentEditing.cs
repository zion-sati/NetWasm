// From the compiler harness's ComponentEditingCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
namespace EditingFixture;
sealed class Builder : IIntellisenseBuilder
{
    public string Name => "Builder";
    public bool Show(string language,string value,ref string replacement) { if(language!="C#") return false; replacement=value+"!"; return true; }
}
class Editor : InstanceCreationEditor
{
    public override object CreateInstance(ITypeDescriptorContext context,Type type) => context==null && type==typeof(string) ? "created" : null;
}
sealed class NamedEditor : Editor { public override string Text=>"Custom"; }
sealed class Context : ITypeDescriptorContext
{
    public IContainer Container => null;
    public object Instance => this;
    public PropertyDescriptor PropertyDescriptor => null;
    public object GetService(Type serviceType) => null;
    public bool OnComponentChanging() => true;
    public void OnComponentChanged() { }
}
sealed class CustomComponentEditor : ComponentEditor
{
    public ITypeDescriptorContext SeenContext;
    public object SeenComponent;
    public override bool EditComponent(ITypeDescriptorContext context, object component)
    { SeenContext=context; SeenComponent=component; return component!=null; }
}
public static class EntryPoint
{
    public static int Run(int input)
    {
        IIntellisenseBuilder builder=new Builder(); var replacement="unchanged";
        if(builder.Name!="Builder" || builder.Show("Other","value",ref replacement) || replacement!="unchanged" || !builder.Show("C#","value",ref replacement) || replacement!="value!")return 1;
        InstanceCreationEditor editor=new Editor(); if(editor.Text!="(New...)" || (string)editor.CreateInstance(null,typeof(string))!="created" || editor.CreateInstance(null,typeof(int))!=null || new NamedEditor().Text!="Custom")return 2;
        var names=new string[]{null,"","  ","machine"," machine ","a/b","a\\b"};
        var validNames=new bool[]{false,false,false,true,true,true,false};
        for(var i=0;i<names.Length;i++)if(SyntaxCheck.CheckMachineName(names[i])!=validNames[i])return 3;
        var paths=new string[]{null,"","  ","\\\\server\\share","  \\\\server\\share","\\server","/root","relative"};
        var validPaths=new bool[]{false,false,false,true,true,false,false,false};
        for(var i=0;i<paths.Length;i++)if(SyntaxCheck.CheckPath(paths[i])!=validPaths[i])return 4;
        var rooted=new string[]{null,"","  ","/root","  /root  ","relative","C:\\folder","\\\\server\\share"};
        var validRooted=new bool[]{false,false,false,true,true,false,false,false};
        for(var i=0;i<rooted.Length;i++)if(SyntaxCheck.CheckRootedPath(rooted[i])!=validRooted[i])return 5;
        var custom=new CustomComponentEditor(); ComponentEditor componentEditor=custom; var component=new object(); var context=new Context();
        if(!componentEditor.EditComponent(component) || custom.SeenContext!=null || !ReferenceEquals(custom.SeenComponent,component))return 6;
        if(!componentEditor.EditComponent(context,component) || !ReferenceEquals(custom.SeenContext,context) || !ReferenceEquals(custom.SeenComponent,component))return 7;
        if(componentEditor.EditComponent(null) || custom.SeenContext!=null || custom.SeenComponent!=null)return 8;
        return 42;
    }
}
