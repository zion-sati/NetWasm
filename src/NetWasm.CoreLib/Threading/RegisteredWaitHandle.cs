// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// From dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce ThreadPool.Browser.cs.
namespace System.Threading;

public sealed class RegisteredWaitHandle : MarshalByRefObject
{
    internal RegisteredWaitHandle() { }
    public bool Unregister(WaitHandle? waitObject) => throw new PlatformNotSupportedException(
        "RegisteredWaitHandle native wait registration is not supported on this NetWasm platform.");
}
