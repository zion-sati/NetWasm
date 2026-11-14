// From the compiler harness's MarshalByValueComponentCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
namespace ValueComponentFixture;
sealed class Item : MarshalByValueComponent
{
    public int Managed, Unmanaged;
    public EventHandlerList Handlers=>Events;
    public void ReleaseUnmanaged()=>Dispose(false);
    protected override void Dispose(bool disposing){if(disposing)Managed++;else Unmanaged++;base.Dispose(disposing);}
}
sealed class Site : ISite
{
    public IComponent Component {get;set;}
    public IContainer Container=>null;
    public bool DesignMode=>true;
    public string Name {get;set;}
    public readonly object Service=new object();
    public object GetService(Type type)=>type==typeof(string)?Service:null;
}
public static class EntryPoint
{
    public static int Run(int input)
    {
        var item=new Item(); IComponent component=item; IServiceProvider provider=item;
        if(item.Site!=null || item.Container!=null || item.DesignMode || provider.GetService(typeof(string))!=null || item.ToString()!=typeof(Item).FullName)return 1;
        if(!ReferenceEquals(item.Handlers,item.Handlers))return 2;
        var site=new Site{Component=item,Name="Named"};component.Site=site;
        if(!ReferenceEquals(item.Site,site) || !item.DesignMode || item.Container!=null || provider.GetService(typeof(string))!=site.Service || provider.GetService(typeof(int))!=null || item.ToString()!="Named ["+typeof(Item).FullName+"]")return 3;
        component.Site=null; var container=new Container();container.Add(item,"item");if(item.Container!=container || item.DesignMode || container.Components["item"]!=item)return 4;
        var disposed=0; var removed=0;
        EventHandler ignored=(sender,args)=>removed++;component.Disposed+=ignored;component.Disposed-=ignored;
        component.Disposed+=(sender,args)=>{if(sender==item && args==EventArgs.Empty && item.Site==null && container.Components.Count==0)disposed++;};
        item.ReleaseUnmanaged();if(item.Unmanaged!=1 || item.Managed!=0 || item.Site==null || disposed!=0)return 5;
        item.Dispose();if(item.Managed!=1 || item.Site!=null || disposed!=1 || removed!=0 || container.Components.Count!=0)return 6;
        ((IDisposable)item).Dispose();if(item.Managed!=2 || disposed!=2 || item.Unmanaged!=1)return 7;
        var key=new object();var calls=0;EventHandler handler=(sender,args)=>calls++;item.Handlers.AddHandler(key,handler);((EventHandler)item.Handlers[key])(item,EventArgs.Empty);item.Handlers.RemoveHandler(key,handler);if(calls!=1 || item.Handlers[key]!=null)return 8;
        container.Dispose();return 42;
    }
}
