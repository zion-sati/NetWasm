// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce (AppDomain.cs's events).
// NetWasm: subscribing works as upstream's accessors do. What raises them:
// - DomainUnload: never, as upstream's (the one domain is never unloaded).
// - UnhandledException: an exception escaping Main aborts the instance; raising the event first is a project
//   opt-in, not yet available, so it is not raised.
// - ProcessExit: not raised yet; raising it on the exit path must not cost a program that never subscribes.
// - FirstChanceException: not raised (it needs a hook on every throw).

using System.Runtime.ExceptionServices;

namespace System;

public sealed partial class AppDomain
{
    public event UnhandledExceptionEventHandler? UnhandledException
    {
        add { AppContext.UnhandledException += value; }
        remove { AppContext.UnhandledException -= value; }
    }

#pragma warning disable CS0067 // DomainUnload is never raised; ProcessExit is not raised yet (see above)
    public event EventHandler? DomainUnload;

    public event EventHandler<FirstChanceExceptionEventArgs>? FirstChanceException
    {
        add { AppContext.FirstChanceException += value; }
        remove { AppContext.FirstChanceException -= value; }
    }

    public event EventHandler? ProcessExit;
#pragma warning restore CS0067
}
