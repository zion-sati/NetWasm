// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Threading.Channels uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Threading.Channels's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string InvalidOperation_IncompleteAsyncOperation = "The asynchronous operation has not completed.";
        internal const string InvalidOperation_IncorrectToken = "The result of the operation was already consumed and may not be used again.";
        internal const string InvalidOperation_MultipleContinuations = "Another continuation was already registered.";
        internal const string ChannelClosedException_DefaultMessage = "The channel has been closed.";
    }
}
