// From the compiler harness's ComponentLifecycleContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
namespace ComponentLifecycleFixtureComponentLifecycleContract;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var model = new Model();
        ISupportInitializeNotification notify = model;
        var events = 0; notify.Initialized += (_, _) => events++;
        ISupportInitialize init = notify; init.BeginInit();
        if (notify.IsInitialized) return 1;
        init.EndInit();
        if (!notify.IsInitialized || events != 1) return 2;
        IEditableObject edit = model; edit.BeginEdit();
        if (!model.Editing) return 3;
        edit.CancelEdit(); if (model.Editing) return 4;
        edit.BeginEdit(); edit.EndEdit(); if (model.Editing || model.Commits != 1) return 5;
        ICancelAddNew rows = model; rows.CancelNew(7); rows.EndNew(11);
        if (model.CanceledRow != 7 || model.CompletedRow != 11) return 6;
        var owner = new Owner(); IContainer container = owner;
        container.Add(null); container.Add(null, "named"); container.Remove(null);
        if (owner.Added != 2 || owner.Removed != 1 || owner.LastName != "named" || container.Components.Count != 0) return 7;
        ((IDisposable)container).Dispose(); if (!owner.Disposed) return 8;
        return input + 1;
    }
    private sealed class Model : ISupportInitializeNotification, IEditableObject, ICancelAddNew
    {
        public bool IsInitialized { get; private set; }
        public bool Editing;
        public int Commits;
        public int CanceledRow;
        public int CompletedRow;
        public event EventHandler Initialized;
        public void BeginInit() => IsInitialized = false;
        public void EndInit() { IsInitialized = true; Initialized?.Invoke(this, EventArgs.Empty); }
        public void BeginEdit() => Editing = true;
        public void CancelEdit() => Editing = false;
        public void EndEdit() { Editing = false; Commits++; }
        public void CancelNew(int itemIndex) => CanceledRow = itemIndex;
        public void EndNew(int itemIndex) => CompletedRow = itemIndex;
    }
    private sealed class Owner : IContainer
    {
        public int Added;
        public int Removed;
        public string LastName;
        public bool Disposed;
        public ComponentCollection Components => new ComponentCollection(new IComponent[0]);
        public void Add(IComponent component) => Added++;
        public void Add(IComponent component, string name) { Added++; LastName = name; }
        public void Remove(IComponent component) => Removed++;
        public void Dispose() => Disposed = true;
    }
}
