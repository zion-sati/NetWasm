// From the compiler harness's TransactionContractsCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
#pragma warning disable SYSLIB0050, SYSLIB0051
using System;
using System.Runtime.Serialization;
using System.Transactions;
namespace TransactionContractsFixture;

public sealed class Participant : IEnlistmentNotification
{
    public string Log = "";
    public void Prepare(PreparingEnlistment preparingEnlistment) => Log += "P";
    public void Commit(Enlistment enlistment) => Log += "C";
    public void Rollback(Enlistment enlistment) => Log += "R";
    public void InDoubt(Enlistment enlistment) => Log += "D";
}

public sealed class SinglePhaseParticipant : ISinglePhaseNotification
{
    public string Log = "";
    public void Prepare(PreparingEnlistment preparingEnlistment) => Log += "P";
    public void Commit(Enlistment enlistment) => Log += "C";
    public void Rollback(Enlistment enlistment) => Log += "R";
    public void InDoubt(Enlistment enlistment) => Log += "D";
    public void SinglePhaseCommit(SinglePhaseEnlistment singlePhaseEnlistment) => Log += "S";
}

public sealed class Promotable : IPromotableSinglePhaseNotification
{
    public string Log = "";
    public byte[]? Token = new byte[] { 1, 2, 3 };
    public byte[]? Promote() { Log += "M"; return Token; }
    public void Initialize() => Log += "I";
    public void SinglePhaseCommit(SinglePhaseEnlistment singlePhaseEnlistment) => Log += "S";
    public void Rollback(SinglePhaseEnlistment singlePhaseEnlistment) => Log += "R";
}

public sealed class Superior : ISimpleTransactionSuperior
{
    public string Log = "";
    public byte[]? Promote() { Log += "M"; return null; }
    public void Rollback() => Log += "R";
}

public sealed class DtcTransaction : IDtcTransaction
{
    public string Log = "";
    public void Commit(int retaining, int commitType, int reserved) => Log += "C";
    public void Abort(IntPtr reason, int retaining, int async) => Log += "A";
    public void GetTransactionInfo(IntPtr transactionInformation) => Log += "G";
}

public sealed class RestoredException : TransactionException
{
    public RestoredException(SerializationInfo info, StreamingContext context) : base(info, context) { }
}

public static class EntryPoint
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    static string? ParamName<T>(Action action) where T : ArgumentException
    {
        try { action(); return "<none>"; } catch (T e) { return e.ParamName; }
    }

    public static int Run(int input)
    {
        // TransactionOptions stores both fields without validation and compares them.
        var options = new TransactionOptions();
        if (options.Timeout != TimeSpan.Zero || options.IsolationLevel != IsolationLevel.Serializable) return 1;
        options.Timeout = TimeSpan.FromSeconds(-5);
        options.IsolationLevel = (IsolationLevel)99;
        var copy = options;
        copy.Timeout = TimeSpan.FromSeconds(30);
        if (options.Timeout != TimeSpan.FromSeconds(-5) || (int)options.IsolationLevel != 99) return 2;
        var same = new TransactionOptions { Timeout = TimeSpan.FromSeconds(30), IsolationLevel = (IsolationLevel)99 };
        if (!copy.Equals(same) || !copy.Equals((object)same) || !(copy == same) || copy != same) return 3;
        if (copy.Equals(options) || copy == options || copy.Equals(null) || copy.Equals("options")) return 4;
        if (copy.GetHashCode() != same.GetHashCode()) return 5;

        // The enums keep upstream's values.
        if ((int)IsolationLevel.Serializable != 0 || (int)IsolationLevel.RepeatableRead != 1 || (int)IsolationLevel.ReadCommitted != 2
            || (int)IsolationLevel.ReadUncommitted != 3 || (int)IsolationLevel.Snapshot != 4 || (int)IsolationLevel.Chaos != 5
            || (int)IsolationLevel.Unspecified != 6) return 6;
        if ((int)TransactionStatus.Active != 0 || (int)TransactionStatus.Committed != 1 || (int)TransactionStatus.Aborted != 2
            || (int)TransactionStatus.InDoubt != 3) return 7;
        if ((int)DependentCloneOption.RollbackIfNotComplete != 1 || (int)EnlistmentOptions.EnlistDuringPrepareRequired != 1
            || (int)TransactionScopeOption.Suppress != 2 || (int)TransactionScopeAsyncFlowOption.Enabled != 1
            || (int)EnterpriseServicesInteropOption.Full != 2) return 8;
        if (IsolationLevel.Chaos.ToString() != "Chaos" || TransactionScopeOption.RequiresNew.ToString() != "RequiresNew") return 9;

        // Exceptions keep their inheritance, messages and inner exceptions.
        var inner = new InvalidOperationException("inner");
        var plain = new TransactionException("plain", inner);
        if (plain.Message != "plain" || !ReferenceEquals(plain.InnerException, inner) || plain is not SystemException) return 10;
        if (new TransactionException(null).InnerException != null || plain.HResult != unchecked((int)0x80131501)) return 11;
        if (new TransactionAbortedException().Message != "The transaction has aborted."
            || new TransactionAbortedException(null).Message != "The transaction has aborted."
            || new TransactionAbortedException(null, inner).InnerException != inner) return 12;
        if (new TransactionInDoubtException().Message != "The transaction is in doubt."
            || new TransactionInDoubtException("x").Message != "x") return 13;
        if (new TransactionManagerCommunicationException(null).Message != "Communication with the underlying transaction manager has failed.") return 14;
        if (new TransactionPromotionException().Message != "There was an error promoting the transaction to a distributed transaction."
            || new TransactionPromotionException("p", inner).InnerException != inner) return 15;
        TransactionException derived = new TransactionAbortedException();
        if (derived is not TransactionException || new TransactionPromotionException() is not TransactionException) return 16;

        // User notification interfaces dispatch through their inherited shapes.
        var participant = new Participant();
        IEnlistmentNotification notification = participant;
        notification.Prepare(null!); notification.Commit(null!); notification.Rollback(null!); notification.InDoubt(null!);
        if (participant.Log != "PCRD") return 17;
        var single = new SinglePhaseParticipant();
        ISinglePhaseNotification singleNotification = single;
        singleNotification.SinglePhaseCommit(null!);
        ((IEnlistmentNotification)singleNotification).Rollback(null!);
        if (single.Log != "SR") return 18;
        var promotable = new Promotable();
        IPromotableSinglePhaseNotification promotableNotification = promotable;
        promotableNotification.Initialize();
        if (!ReferenceEquals(((ITransactionPromoter)promotableNotification).Promote(), promotable.Token)) return 19;
        promotableNotification.SinglePhaseCommit(null!);
        promotableNotification.Rollback(null!);
        var superior = new Superior();
        ISimpleTransactionSuperior superiorNotification = superior;
        if (((ITransactionPromoter)superiorNotification).Promote() != null) return 20;
        superiorNotification.Rollback();
        if (promotable.Log != "IMSR" || superior.Log != "MR") return 21;

        // Delegates and event data are ordinary.
        var args = new TransactionEventArgs();
        if (args.Transaction != null) return 22;
        HostCurrentTransactionCallback host = () => null;
        if (host() != null) return 23;
        int calls = 0;
        object sender = new object();
        TransactionCompletedEventHandler completed = (s, e) => { if (ReferenceEquals(s, sender) && ReferenceEquals(e, args)) calls++; };
        completed += (s, e) => calls += 10;
        completed(sender, args);
        TransactionStartedEventHandler started = (s, e) => calls += 100;
        started(null, args);
        if (calls != 111) return 24;

        // TransactionManager keeps its configuration state and validation.
        if (TransactionManager.DefaultTimeout != TimeSpan.FromMinutes(1) || TransactionManager.MaximumTimeout != TimeSpan.FromMinutes(10)) return 25;
        TransactionStartedEventHandler distributed = (s, e) => calls = -1;
        TransactionManager.DistributedTransactionStarted += distributed;
        TransactionManager.DistributedTransactionStarted -= distributed;
        if (calls != 111) return 26;
        if (TransactionManager.HostCurrentCallback != null || !Throws<ArgumentNullException>(() => TransactionManager.HostCurrentCallback = null!)) return 27;
        TransactionManager.HostCurrentCallback = host;
        if (!ReferenceEquals(TransactionManager.HostCurrentCallback, host)
            || !Throws<InvalidOperationException>(() => TransactionManager.HostCurrentCallback = host)) return 28;
        if (ParamName<ArgumentException>(() => TransactionManager.Reenlist(Guid.Empty, new byte[1], participant)) != "resourceManagerIdentifier"
            || ParamName<ArgumentNullException>(() => TransactionManager.Reenlist(Guid.NewGuid(), null!, participant)) != "recoveryInformation"
            || ParamName<ArgumentNullException>(() => TransactionManager.Reenlist(Guid.NewGuid(), new byte[1], null!)) != "enlistmentNotification") return 29;
        if (ParamName<ArgumentException>(() => TransactionManager.RecoveryComplete(Guid.Empty)) != "resourceManagerIdentifier"
            || !Throws<PlatformNotSupportedException>(() => TransactionManager.RecoveryComplete(Guid.NewGuid()))) return 30;

        // Distributed interop has the promoter GUID and rejects every operation.
        if (TransactionInterop.PromoterTypeDtc != new Guid("14229753-ffe1-428d-82b7-df73045cb8da")) return 31;
        if (ParamName<ArgumentNullException>(() => TransactionInterop.GetExportCookie(null!, new byte[1])) != "transaction"
            || ParamName<ArgumentNullException>(() => TransactionInterop.GetTransactionFromExportCookie(null!)) != "cookie"
            || ParamName<ArgumentNullException>(() => TransactionInterop.GetTransmitterPropagationToken(null!)) != "transaction"
            || ParamName<ArgumentNullException>(() => TransactionInterop.GetTransactionFromTransmitterPropagationToken(null!)) != "propagationToken"
            || ParamName<ArgumentNullException>(() => TransactionInterop.GetDtcTransaction(null!)) != "transaction"
            || ParamName<ArgumentNullException>(() => TransactionInterop.GetTransactionFromDtcTransaction(null!)) != "transactionNative"
            || ParamName<ArgumentException>(() => TransactionInterop.GetTransactionFromExportCookie(new byte[31])) != "cookie"
            || ParamName<ArgumentException>(() => TransactionInterop.GetTransactionFromTransmitterPropagationToken(new byte[23])) != "propagationToken") return 32;
        var dtc = new DtcTransaction();
        if (!Throws<PlatformNotSupportedException>(() => TransactionInterop.GetWhereabouts())
            || !Throws<PlatformNotSupportedException>(() => TransactionInterop.GetTransactionFromExportCookie(new byte[32]))
            || !Throws<PlatformNotSupportedException>(() => TransactionInterop.GetTransactionFromTransmitterPropagationToken(new byte[24]))
            || !Throws<PlatformNotSupportedException>(() => TransactionInterop.GetTransactionFromDtcTransaction(dtc))
            || dtc.Log != "") return 33;

        // Transactions and scopes validate their arguments before anything else.
        if ((Transaction?)null != null || !((Transaction?)null == null)) return 34;
        if (ParamName<ArgumentOutOfRangeException>(() => new TransactionScope(TransactionScopeOption.Required, TimeSpan.FromSeconds(-1))) != "scopeTimeout"
            || ParamName<ArgumentOutOfRangeException>(() => new TransactionScope((TransactionScopeOption)7)) != "scopeOption"
            || ParamName<ArgumentOutOfRangeException>(() => new TransactionScope((TransactionScopeAsyncFlowOption)5)) != "asyncFlowOption"
            || ParamName<ArgumentOutOfRangeException>(() => new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { Timeout = TimeSpan.FromSeconds(-1) })) != "transactionOptions.Timeout"
            || ParamName<ArgumentOutOfRangeException>(() => new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = (IsolationLevel)99 })) != "transactionIsolationLevel"
            || ParamName<ArgumentOutOfRangeException>(() => new TransactionScope(TransactionScopeOption.Required, new TransactionOptions(), (EnterpriseServicesInteropOption)9)) != "interopOption"
            || ParamName<ArgumentNullException>(() => new TransactionScope((Transaction)null!)) != "transactionToUse"
            || ParamName<ArgumentNullException>(() => new TransactionScope((Transaction)null!, TimeSpan.Zero, EnterpriseServicesInteropOption.None)) != "transactionToUse") return 35;
        if (ParamName<ArgumentOutOfRangeException>(() => new CommittableTransaction(new TransactionOptions { IsolationLevel = (IsolationLevel)99 })) != "transactionIsolationLevel"
            || ParamName<ArgumentOutOfRangeException>(() => new SubordinateTransaction((IsolationLevel)99, superior)) != "transactionIsolationLevel"
            || ParamName<ArgumentNullException>(() => new SubordinateTransaction(IsolationLevel.Serializable, null!)) != "superior") return 36;

#if !DESKTOP_ORACLE
        // Formatter deserialization stays unsupported on NetWasm.
        if (!Throws<PlatformNotSupportedException>(() => new RestoredException(new SerializationInfo(typeof(RestoredException), new FormatterConverter()), default))) return 40;
#endif
        // Transactions execute (LocalTransactionsMatchDesktopInWasmtime covers the engine); a suppressing
        // scope has no ambient transaction.
        using (var scope = new TransactionScope(TransactionScopeOption.Suppress))
        {
            if (Transaction.Current != null) return 37;
            scope.Complete();
        }

        return input + 1;
    }
}
