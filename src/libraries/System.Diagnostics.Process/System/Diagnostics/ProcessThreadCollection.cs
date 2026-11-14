// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: NetWasm's ReadOnlyCollectionBase initializes InnerList lazily, which the
// constructors here dereference with the null-forgiving operator, as ComponentCollection does.

using System.Collections;

namespace System.Diagnostics
{
    /// <devdoc>
    ///    <para>[To be supplied.]</para>
    /// </devdoc>
    public class ProcessThreadCollection : ReadOnlyCollectionBase
    {
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        protected ProcessThreadCollection()
        {
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public ProcessThreadCollection(ProcessThread[] processThreads)
        {
            InnerList!.AddRange(processThreads);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public ProcessThread this[int index]
        {
            get { return (ProcessThread)InnerList[index]!; }
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public int Add(ProcessThread thread)
        {
            return ((IList)InnerList).Add(thread);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public void Insert(int index, ProcessThread thread)
        {
            InnerList.Insert(index, thread);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public int IndexOf(ProcessThread thread)
        {
            return InnerList.IndexOf(thread);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public bool Contains(ProcessThread thread)
        {
            return InnerList.Contains(thread);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public void Remove(ProcessThread thread)
        {
            InnerList.Remove(thread);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public void CopyTo(ProcessThread[] array, int index)
        {
            InnerList.CopyTo(array, index);
        }

        internal void Dispose()
        {
            foreach (ProcessThread processThread in this)
            {
                processThread.Dispose();
            }
        }
    }
}
