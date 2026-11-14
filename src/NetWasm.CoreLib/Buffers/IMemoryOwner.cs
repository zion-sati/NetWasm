// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Copied from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce src/libraries/System.Private.CoreLib/src/System/Buffers/IMemoryOwner.cs

namespace System.Buffers
{
    /// <summary>
    /// Owner of Memory<typeparamref name="T"/> that is responsible for disposing the underlying memory appropriately.
    /// </summary>
    public interface IMemoryOwner<T> : IDisposable
    {
        /// <summary>
        /// Returns a Memory<typeparamref name="T"/>.
        /// </summary>
        Memory<T> Memory { get; }
    }
}
