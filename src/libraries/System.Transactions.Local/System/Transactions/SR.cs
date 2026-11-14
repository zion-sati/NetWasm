// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce resources;
// only the strings this namespace references are included.

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
        internal const string PromotionFailed =
            "There was an error promoting the transaction to a distributed transaction.";
        internal const string TransactionAborted =
            "The transaction has aborted.";
        internal const string TransactionIndoubt =
            "The transaction is in doubt.";
        internal const string TransactionManagerCommunicationException =
            "Communication with the underlying transaction manager has failed.";
        internal const string BadAsyncResult = "The IAsyncResult parameter must be the same parameter returned by BeginCommit.";
        internal const string EsNotSupported = "Com+ Interop features cannot be supported.";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string PromoterTypeInvalid = "The specified PromoterType is invalid.";
        internal const string PromoterTypeUnrecognized = "There is a promotable enlistment for the transaction which has a PromoterType value that is not recognized by System.Transactions. {0}";
        internal const string CannotSetCurrent = "Current cannot be set directly when Com+ Interop is enabled.";
        internal const string TransactionScopeComplete = "The current TransactionScope is already complete.";
        internal const string DistributedTxIDInTransactionException = "{0} Distributed Transaction ID is {1}";
        internal const string EnlistmentStateException = "The operation is not valid for the current state of the enlistment.";
        internal const string TransactionAlreadyCompleted = "DependentTransaction.Complete or CommittableTransaction.Commit has already been called for this transaction.";
        internal const string TransactionStateException = "The operation is not valid for the state of the transaction.";
        internal const string InvalidRecoveryInformation = "Transaction Manager in the Recovery Information does not match the configured transaction manager.";
        internal const string UnrecognizedRecoveryInformation = "The RecoveryInformation provided is not recognized by this version of System.Transactions.";
        internal const string AsyncFlowAndESInteropNotSupported = "TransactionScope with TransactionScopeAsyncFlowOption.Enabled option is not supported when the TransactionScope is used within Enterprise Service context with Automatic or Full EnterpriseServicesInteropOption enabled in parent scope.";
        internal const string DisposeScope = "The current TransactionScope is already complete. You should dispose the TransactionScope.";
        internal const string InternalError = "Internal Error";
        internal const string InvalidScopeThread = "A TransactionScope must be disposed on the same thread that it was created.";
        internal const string TransactionScopeIncorrectCurrent = "Transaction.Current has changed inside of the TransactionScope.";
        internal const string TransactionScopeInvalidNesting = "TransactionScope nested incorrectly.";
        internal const string TransactionScopeIsolationLevelDifferentFromTransaction = "The transaction specified for TransactionScope has a different IsolationLevel than the value requested for the scope.";
        internal const string TransactionScopeTimerObjectInvalid = "TransactionScope timer object is invalid.";
        internal const string CannotPromoteSnapshot = "Transactions with IsolationLevel Snapshot cannot be promoted.";
        internal const string InvalidIPromotableSinglePhaseNotificationSpecified = "The specified IPromotableSinglePhaseNotification is not the same as the one provided to EnlistPromotableSinglePhase.";
        internal const string PromotedReturnedInvalidValue = "The Promote method returned an invalid value for the distributed transaction.";
        internal const string PromotedTransactionExists = "The transaction returned from Promote already exists as a distributed transaction.";
        internal const string TooLate = "It is too late to add enlistments to this transaction.";
        internal const string TraceTransactionTimeout = "Transaction Timeout";
        internal const string UnexpectedFailureOfThreadPool = "There was an unexpected failure of QueueUserWorkItem.";
        internal const string UnexpectedTimerFailure = "There was an unexpected failure of a timer.";
        internal const string VolEnlistNoRecoveryInfo = "Volatile enlistments do not generate recovery information.";
    }
}
