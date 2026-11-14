// From the compiler harness's ComponentObservationCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Collections;
using System.ComponentModel;
using System.Threading.Tasks;
namespace ObservationFixture;
sealed class Item : IRevertibleChangeTracking, IDataErrorInfo, INotifyDataErrorInfo, INotifyPropertyChanging, IExtenderProvider, ISynchronizeInvoke
{
    public int Value, Saved;
    public bool IsChanged => Value != Saved;
    public void AcceptChanges() => Saved = Value;
    public void RejectChanges() => Value = Saved;
    public string this[string name] => name == "Value" && Value < 0 ? "negative" : "";
    public string Error => this["Value"];
    public bool HasErrors => Error != "";
    public IEnumerable GetErrors(string name) => HasErrors ? new string[] { Error } : new string[0];
    public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;
    public event PropertyChangingEventHandler PropertyChanging;
    public void Set(int value) { PropertyChanging?.Invoke(this, new PropertyChangingEventArgs("Value")); Value = value; ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs("Value")); }
    public bool CanExtend(object value) => value is Item;
    public bool InvokeRequired => false;
    public object Invoke(Delegate callback, object[] args) => ((Func<int, int>)callback)((int)args[0]);
    public IAsyncResult BeginInvoke(Delegate callback, object[] args) => Task.FromResult(Invoke(callback,args));
    public object EndInvoke(IAsyncResult result) => ((Task<object>)result).Result;
}
sealed class NamedChanging : PropertyChangingEventArgs { public NamedChanging() : base(null) { } public override string PropertyName => "override"; }
sealed class NamedError : DataErrorsChangedEventArgs { public NamedError() : base(null) { } public override string PropertyName => "override"; }
public static class EntryPoint
{
    public static int Run(int input)
    {
        var item = new Item(); IRevertibleChangeTracking tracking = item; IChangeTracking baseTracking = item;
        IDataErrorInfo errors = item; INotifyDataErrorInfo notifying = item; INotifyPropertyChanging changing = item;
        var before = 0; var after = 0;
        PropertyChangingEventHandler onChanging = (sender,args) => { if (sender == item && args.PropertyName == "Value" && item.Value == 0) before++; };
        EventHandler<DataErrorsChangedEventArgs> onErrors = (sender,args) => { if (sender == item && args.PropertyName == "Value" && item.Value == -1) after++; };
        changing.PropertyChanging += onChanging; notifying.ErrorsChanged += onErrors;
        item.Set(-1);
        if (before != 1 || after != 1 || !baseTracking.IsChanged || !notifying.HasErrors || errors.Error != "negative" || errors["Other"] != "") return 1;
        var count=0; foreach (var error in notifying.GetErrors("Value")) { if ((string)error != "negative") return 2; count++; } if (count != 1) return 3;
        tracking.RejectChanges(); if (item.Value != 0 || baseTracking.IsChanged || notifying.HasErrors) return 4;
        item.Value=42; baseTracking.AcceptChanges(); item.Value=43; tracking.RejectChanges(); if (item.Value != 42 || tracking.IsChanged) return 5;
        changing.PropertyChanging -= onChanging; notifying.ErrorsChanged -= onErrors; item.Set(44); if (before != 1 || after != 1) return 6;
        foreach (var ignored in notifying.GetErrors(null)) return 7;
        if (new PropertyChangingEventArgs(null).PropertyName != null || new DataErrorsChangedEventArgs(null).PropertyName != null || new NamedChanging().PropertyName != "override" || new NamedError().PropertyName != "override") return 8;
        var handled=new HandledEventArgs(); if (handled.Handled || !new HandledEventArgs(true).Handled) return 9;
        HandledEventHandler handler=(sender,args)=>{if(sender==item) args.Handled=true;}; handler(item,handled); if(!handled.Handled) return 10;
        IExtenderProvider extender=item; if(!extender.CanExtend(item) || extender.CanExtend(new object())) return 11;
        ISynchronizeInvoke invoker=item; Func<int,int> callback=value=>value+1;
        if(invoker.InvokeRequired || (int)invoker.Invoke(callback,new object[]{41}) != 42 || (int)invoker.EndInvoke(invoker.BeginInvoke(callback,new object[]{41})) !=42) return 12;
        return 42;
    }
}
