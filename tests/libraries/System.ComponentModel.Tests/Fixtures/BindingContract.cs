// From the compiler harness's BindingContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Collections;
using System.ComponentModel;
namespace CategoryFixtureBindingContract;
public sealed class CallerList : ArrayList, IBindingListView, ITypedList, IRaiseItemChangedEvents
{
    public bool AllowNew => true;
    public bool AllowEdit => true;
    public bool AllowRemove => true;
    public bool SupportsChangeNotification => true;
    public bool SupportsSearching => false;
    public bool SupportsSorting => false;
    public bool IsSorted => false;
    public PropertyDescriptor SortProperty => null;
    public ListSortDirection SortDirection => ListSortDirection.Ascending;
    public event ListChangedEventHandler ListChanged;
    public object AddNew() { Add(42); ListChanged?.Invoke(this, new ListChangedEventArgs(ListChangedType.ItemAdded, Count - 1)); return 42; }
    public void AddIndex(PropertyDescriptor property) { }
    public void RemoveIndex(PropertyDescriptor property) { }
    public void ApplySort(PropertyDescriptor property, ListSortDirection direction) => throw new NotSupportedException();
    public int Find(PropertyDescriptor property, object key) => IndexOf(key);
    public void RemoveSort() { }
    public void ApplySort(ListSortDescriptionCollection sorts) => throw new NotSupportedException();
    public string Filter { get; set; }
    public ListSortDescriptionCollection SortDescriptions => new ListSortDescriptionCollection();
    public void RemoveFilter() { Filter = null; }
    public bool SupportsAdvancedSorting => false;
    public bool SupportsFiltering => false;
    public bool RaisesItemChangedEvents => true;
    public string GetListName(PropertyDescriptor[] accessors) => "caller";
    public PropertyDescriptorCollection GetItemProperties(PropertyDescriptor[] accessors) => PropertyDescriptorCollection.Empty;
}
public static class EntryPoint
{
    public static int Run(int input)
    {
        var sort = new ListSortDescription(null, ListSortDirection.Descending);
        if (sort.PropertyDescriptor != null || sort.SortDirection != ListSortDirection.Descending) return 1;
        sort.SortDirection = (ListSortDirection)7; if ((int)sort.SortDirection != 7) return 2;
        ListSortDescription[] source = { sort, null }; var descriptions = new ListSortDescriptionCollection(source); source[0] = null;
        if (descriptions.Count != 2 || descriptions[0] != sort || descriptions[1] != null || !descriptions.Contains(sort) || descriptions.IndexOf(sort) != 0 || descriptions.IndexOf(new object()) != -1) return 3;
        var copy = new ListSortDescription[2]; descriptions.CopyTo(copy, 0); if (copy[0] != sort) return 4;
        IList list = descriptions; ICollection collection = descriptions;
        if (!list.IsReadOnly || !list.IsFixedSize || !collection.IsSynchronized || collection.SyncRoot != descriptions) return 5;
        var enumerator = list.GetEnumerator(); if (!enumerator.MoveNext() || enumerator.Current != sort || !enumerator.MoveNext() || enumerator.Current != null || enumerator.MoveNext()) return 6;
        var failures = 0;
        try { descriptions[0] = null; } catch (InvalidOperationException) { failures++; }
        try { list[0] = null; } catch (InvalidOperationException) { failures++; }
        try { list.Add(sort); } catch (InvalidOperationException) { failures++; }
        try { list.Clear(); } catch (InvalidOperationException) { failures++; }
        try { list.Insert(0, sort); } catch (InvalidOperationException) { failures++; }
        try { list.Remove(sort); } catch (InvalidOperationException) { failures++; }
        try { list.RemoveAt(0); } catch (InvalidOperationException) { failures++; }
        if (new ListSortDescriptionCollection(null).Count != 0 || failures != 7) return 7;
        var caller = new CallerList(); IBindingListView view = caller; IBindingList binding = view; var calls = 0;
        ListChangedEventHandler handler = (sender, args) => { if (sender == caller && args.ListChangedType == ListChangedType.ItemAdded && args.NewIndex == 0) calls++; };
        binding.ListChanged += handler; if ((int)binding.AddNew() != 42 || calls != 1 || ((IList)binding).Count != 1 || (int)((IList)binding)[0] != 42) return 8;
        binding.ListChanged -= handler;
        if (!binding.AllowNew || !binding.AllowEdit || !binding.AllowRemove || !binding.SupportsChangeNotification || binding.SupportsSearching || binding.SupportsSorting || binding.IsSorted || binding.SortProperty != null || binding.SortDirection != ListSortDirection.Ascending) return 9;
        view.Filter = "supplied"; if (view.Filter != "supplied" || view.SortDescriptions.Count != 0 || view.SupportsAdvancedSorting || view.SupportsFiltering) return 10;
        view.RemoveFilter(); if (view.Filter != null || ((ITypedList)caller).GetListName(null) != "caller" || ((ITypedList)caller).GetItemProperties(null).Count != 0 || !((IRaiseItemChangedEvents)caller).RaisesItemChangedEvents) return 11;
        return input + 1;
    }
}
