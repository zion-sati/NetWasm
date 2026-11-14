// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// From dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce ThreadPoolWorkQueue.cs.
namespace System.Threading;

public delegate void WaitCallback(object? state);
public delegate void WaitOrTimerCallback(object? state, bool timedOut);
