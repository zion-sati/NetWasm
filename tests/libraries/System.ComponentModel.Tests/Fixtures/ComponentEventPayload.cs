// From the compiler harness's ComponentEventPayloadCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
using System.Reflection;
namespace CategoryFixtureComponentEventPayload;
public sealed class Completion : AsyncCompletedEventArgs
{
    public Completion(Exception error, bool cancelled, object state) : base(error, cancelled, state) { }
    public void Check() => RaiseExceptionIfNecessary();
}
public static class EntryPoint
{
    public static int Run(int input)
    {
        var state = new object(); var error = new Exception("failure");
        var success = new Completion(null, false, state); success.Check();
        if (success.Cancelled || success.Error != null || success.UserState != state) return 1;
        var caught = 0;
        try { new Completion(error, true, state).Check(); } catch (TargetInvocationException e) { if (e.InnerException == error && !string.IsNullOrEmpty(e.Message)) caught++; }
        try { new Completion(null, true, state).Check(); } catch (InvalidOperationException e) { if (!string.IsNullOrEmpty(e.Message)) caught++; }
        var result = new RunWorkerCompletedEventArgs(42, null, false);
        if ((int)result.Result != 42 || result.UserState != null) return 2;
        try { var ignored = new RunWorkerCompletedEventArgs(42, error, false).Result; } catch (TargetInvocationException e) { if (e.InnerException == error) caught++; }
        try { var ignored = new RunWorkerCompletedEventArgs(42, null, true).Result; } catch (InvalidOperationException) { caught++; }
        var work = new DoWorkEventArgs(state); work.Result = 42; work.Cancel = true;
        if (work.Argument != state || (int)work.Result != 42 || !work.Cancel) return 3;
        var progress = new ProgressChangedEventArgs(-7, state); if (progress.ProgressPercentage != -7 || progress.UserState != state) return 4;
        var adding = new AddingNewEventArgs(); if (adding.NewObject != null) return 5;
        adding.NewObject = state; if (adding.NewObject != state || new AddingNewEventArgs(state).NewObject != state) return 6;
        var collection = new CollectionChangeEventArgs(CollectionChangeAction.Add, state);
        if (collection.Action != CollectionChangeAction.Add || collection.Element != state) return 7;
        var moved = new ListChangedEventArgs(ListChangedType.ItemMoved, 5, 2);
        var added = new ListChangedEventArgs(ListChangedType.ItemAdded, 3);
        var changed = new ListChangedEventArgs(ListChangedType.ItemChanged, 4, (PropertyDescriptor)null);
        var descriptor = new ListChangedEventArgs(ListChangedType.PropertyDescriptorChanged, (PropertyDescriptor)null);
        if (moved.NewIndex != 5 || moved.OldIndex != 2 || moved.ListChangedType != ListChangedType.ItemMoved || moved.PropertyDescriptor != null) return 8;
        if (added.NewIndex != 3 || added.OldIndex != -1 || changed.NewIndex != 4 || changed.OldIndex != 4 || descriptor.NewIndex != 0 || descriptor.OldIndex != 0) return 9;
        var calls = 0;
        AsyncCompletedEventHandler asyncHandler = (sender, args) => { if (sender == state && args == success) calls++; }; asyncHandler(state, success);
        RunWorkerCompletedEventHandler completedHandler = (sender, args) => { if (args == result) calls++; }; completedHandler(state, result);
        DoWorkEventHandler workHandler = (sender, args) => { if (args == work) calls++; }; workHandler(state, work);
        ProgressChangedEventHandler progressHandler = (sender, args) => { if (args == progress) calls++; }; progressHandler(state, progress);
        AddingNewEventHandler addingHandler = (sender, args) => { if (args == adding) calls++; }; addingHandler(state, adding);
        CollectionChangeEventHandler collectionHandler = (sender, args) => { if (args == collection) calls++; }; collectionHandler(state, collection);
        ListChangedEventHandler listHandler = (sender, args) => { if (args == moved) calls++; }; listHandler(state, moved);
        return caught == 4 && calls == 7 ? input + 1 : 10;
    }
}
