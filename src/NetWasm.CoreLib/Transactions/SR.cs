// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de resources;
// only the strings this namespace references are included. LocalTransactionsNotSupported is
// NetWasm's.

namespace System.Transactions
{
    internal static class SR
    {
        internal const string BadResourceManagerId =
            "Resource Manager Identifiers cannot be Guid.Empty.";
        internal const string CurrentDelegateSet =
            "The delegate for an external current can only be set once.";
        internal const string DistributedNotSupported =
            "This platform does not support distributed transactions.";
        internal const string InvalidArgument =
            "The argument is invalid.";
        internal const string LocalTransactionsNotSupported =
            "Creating, enlisting in or completing a transaction is not supported on NetWasm.";
        internal const string PromotionFailed =
            "There was an error promoting the transaction to a distributed transaction.";
        internal const string TransactionAborted =
            "The transaction has aborted.";
        internal const string TransactionIndoubt =
            "The transaction is in doubt.";
        internal const string TransactionManagerCommunicationException =
            "Communication with the underlying transaction manager has failed.";
    }
}
