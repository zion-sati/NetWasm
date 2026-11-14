// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce (AppContext.cs's events).

using System.Runtime.ExceptionServices;

namespace System;

public static partial class AppContext
{
#pragma warning disable CS0067 // events raised by the runtime
    internal static event UnhandledExceptionEventHandler? UnhandledException;

    internal static event EventHandler<FirstChanceExceptionEventArgs>? FirstChanceException;
#pragma warning restore CS0067
}
