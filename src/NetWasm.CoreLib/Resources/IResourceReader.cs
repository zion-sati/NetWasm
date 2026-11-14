// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Collections;

namespace System.Resources
{
    /// <summary>
    /// Abstraction to read streams of resources.
    /// </summary>
    public interface IResourceReader : IEnumerable, IDisposable
    {
        // Interface does not need to be marked with the serializable attribute
        // Closes the ResourceReader, releasing any resources associated with it.
        // This could close a network connection, a file, or do nothing.
        void Close();

        new IDictionaryEnumerator GetEnumerator();
    }
}
