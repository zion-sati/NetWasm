// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: NetWasm's ReadOnlyCollectionBase initializes InnerList lazily, which the
// constructors here dereference with the null-forgiving operator, as ComponentCollection does.

using System.Collections;

namespace System.Diagnostics
{
    public class ProcessModuleCollection : ReadOnlyCollectionBase
    {
        protected ProcessModuleCollection()
        {
        }

        public ProcessModuleCollection(ProcessModule[] processModules)
        {
            InnerList!.AddRange(processModules);
        }

        internal ProcessModuleCollection(int capacity)
        {
            if (capacity > 0)
            {
                InnerList!.Capacity = capacity;
            }
        }

        internal void Add(ProcessModule module) => InnerList.Add(module);

        internal void Insert(int index, ProcessModule module) => InnerList.Insert(index, module);

        internal void RemoveAt(int index) => InnerList.RemoveAt(index);

        public ProcessModule this[int index] => (ProcessModule)InnerList[index]!;

        public int IndexOf(ProcessModule module) => InnerList.IndexOf(module);

        public bool Contains(ProcessModule module) => InnerList.Contains(module);

        public void CopyTo(ProcessModule[] array, int index) => InnerList.CopyTo(array, index);

        internal void Dispose()
        {
            foreach (ProcessModule processModule in this)
            {
                processModule.Dispose();
            }
        }
    }
}
