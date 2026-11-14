// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace System.ComponentModel
{
    public delegate void AsyncCompletedEventHandler(object? sender, AsyncCompletedEventArgs e);

    public class AsyncCompletedEventArgs : EventArgs
    {
        public AsyncCompletedEventArgs(Exception? error, bool cancelled, object? userState)
        {
            Cancelled = cancelled;
            Error = error;
            UserState = userState;
        }

        protected void RaiseExceptionIfNecessary()
        {
            if (Error != null)
            {
                throw new TargetInvocationException("An exception occurred during the operation, making the result invalid.  Check InnerException for exception details.", Error);
            }
            else if (Cancelled)
            {
                throw new InvalidOperationException("Operation has been cancelled.");
            }
        }

        public bool Cancelled { get; }
        public Exception? Error { get; }
        public object? UserState { get; }
    }
}
